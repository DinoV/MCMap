#!/usr/bin/env python3
"""Create a WorldPainter-ready PNG using the GDAL Python API directly.

This is the in-process equivalent of the earlier four-command workflow:

1. Open the source VRT, or build a temporary bare-earth DEM from the
   intersecting LAZ tiles in a USGS LPC project, then crop it with an
   EPSG:4326 longitude/latitude window,
   resample to a one-metre grid, and expand the east and south edges so the
   raster dimensions are multiples of 512 pixels while preserving the
   literal geographic northwest coordinate supplied with --west/--north.
2. Compute the stretch range from the originally requested (unpadded)
   window by default, then convert the full 512-aligned raster to an 8-bit
   grayscale image using that range and a configurable exponent (default:
   0.42). This preserves the contrast of the original four-command workflow
   even when alignment adds terrain to the east or south.
3. Preserve the selected water byte value while applying a configurable floor
   to non-water values, using NumPy and GDAL block I/O.
4. Create the final PNG through the PNG driver's CreateCopy() API.
5. Report the exact final aligned raster bounds in the source CRS and in
   EPSG:4326, including the four geographic corner coordinates.

No GDAL or PDAL command-line programs are invoked. LAZ input additionally
requires PDAL's Python bindings. Run the script from an environment containing
GDAL's Python bindings and NumPy, for example:

    python lidar.py \
        --vrt WA_KingCo_1_2021.vrt \
        --west -122.3539083 --north 47.5558653 \
        --east -122.2837235 --south 47.4723506 \
        --output duwamish-larger.png \
        --clip-level 100

Raster input may use any CRS understood by GDAL. It is reprojected to EPSG:6597
before the literal northwest anchor, one-metre grid, and 512-pixel alignment
are applied. ``--raster`` and the legacy name ``--vrt`` are aliases.

For example, the USGS 1/3-arc-second geographic GeoTIFF can be read remotely:

    python lidar.py \
        --raster "/vsicurl/https://prd-tnm.s3.amazonaws.com/StagedProducts/Elevation/13/TIFF/historical/n49w123/USGS_13_n49w123_20240327.tif" \
        --west -122.7033389 --north 48.2275394 \
        --east -122.6680931 --south 48.2073796 \
        --output coupeville-10m.png --clip-level 100 --overwrite

Or downloading the 3m in a zip:
    python lidar.py \
        --raster "/vsizip/vsicurl/https://prd-tnm.s3.amazonaws.com/StagedProducts/Elevation/19/IMG/ned19_n48x25_w122x75_wa_puget_sound_2000.zip/ned19_n48x25_w122x75_wa_puget_sound_2000.img" \
        --west -122.7033389  --north 48.2275394 --east -122.6680931 --south 48.2073796 \
        --output coupeville-3m.png \
        --clip-level 100 \
        --resampling bilinear \

For a USGS legacy LPC project containing LAZ point clouds instead of a DEM:

    conda install -n lidar -c conda-forge python-pdal gdal numpy

    python lidar.py \
        --usgs-lpc-project \
        "https://rockyweb.usgs.gov/vdelivery/Datasets/Staged/Elevation/LPC/Projects/legacy/WA_PSLC_2001/" \
        --west -123.56 --north 48.12 --east -123.50 --south 48.07 \
        --output pslc.png --clip-level 65 --overwrite

The first run downloads the small per-tile XML metadata index and only the LAZ
tiles intersecting the requested longitude/latitude rectangle. Later runs use
the files in --lpc-cache. LAS classification 2 (ground) is rasterized by
default; use --lpc-classification if a different project uses another class.
Known unclassified legacy projects such as WA_PSLC_2001 automatically apply
their metadata CRS and run PDAL's SMRF ground classifier. This can be overridden
with --lpc-source-srs and --lpc-ground-mode.
"""

from __future__ import annotations

import argparse
from concurrent.futures import ThreadPoolExecutor, as_completed
import hashlib
import json
import math
from dataclasses import dataclass
from pathlib import Path
import re
import sys
import tempfile
from typing import Any
from urllib.parse import parse_qs, urljoin, urlparse
from urllib.request import Request, urlopen
import xml.etree.ElementTree as ET


US_SURVEY_FEET_PER_METER = 3937.0 / 1200.0
DEFAULT_METERS_PER_PIXEL = 1.0
DEFAULT_STRETCH_EXPONENT = 0.42
DEFAULT_LAND_START = 8
DEFAULT_WATER_INPUT_VALUE = 0
DEFAULT_CHUNK_ROWS = 512
DEFAULT_TILE_SIZE_PIXELS = 512
OUTPUT_MAXIMUM = 150

# Older USGS legacy projects can omit the CRS from every LAZ header and can
# contain only ASPRS class 1 (unclassified) points.  These project-level facts
# come from the accompanying FGDC XML metadata.
LEGACY_LPC_PROJECT_DEFAULTS: dict[str, tuple[str, bool]] = {
    "WA_PSLC_2001": ("EPSG:2285", True),
}


@dataclass(frozen=True)
class Coordinate:
    """A two-dimensional coordinate."""

    x: float
    y: float


@dataclass(frozen=True)
class Bounds:
    """A rectangular envelope in GDAL -projwin order."""

    west: float
    north: float
    east: float
    south: float


@dataclass(frozen=True)
class MapBounds:
    """Projected and geographic bounds for the final aligned raster."""

    source_crs_label: str
    source_crs_argument: str | None
    projected: Bounds
    geographic: Bounds
    north_west: Coordinate
    north_east: Coordinate
    south_east: Coordinate
    south_west: Coordinate


@dataclass(frozen=True)
class LpcTile:
    """One USGS LPC tile discovered from its FGDC metadata XML."""

    name: str
    bounds: Bounds
    laz_url: str


class ProgressReporter:
    """A quiet GDAL progress callback that reports every five percent."""

    def __init__(self, label: str) -> None:
        self._label = label
        self._next_percent = 0
        self._finished = False

    def __call__(self, complete: float, message: str, callback_data: Any) -> int:
        del message, callback_data

        percent = max(0, min(100, int(complete * 100.0)))
        if percent >= self._next_percent or percent == 100:
            print(f"\r{self._label}: {percent:3d}%", end="", flush=True)
            self._next_percent = ((percent // 5) + 1) * 5

        if percent >= 100:
            self.finish()

        return 1

    def finish(self) -> None:
        if not self._finished:
            print()
            self._finished = True


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description=(
            "Crop an elevation VRT or rasterize a USGS LPC/LAZ project, "
            "create a metric raster, stretch it, apply a non-water floor, "
            "and emit an 8-bit PNG."
        )
    )

    source = parser.add_mutually_exclusive_group(required=True)
    source.add_argument(
        "--vrt",
        "--raster",
        dest="vrt",
        help=(
            "Input raster filename, URL, or GDAL /vsi path. Any "
            "GDAL-readable CRS is reprojected to EPSG:6597."
        ),
    )
    source.add_argument(
        "--usgs-lpc-project",
        help=(
            "USGS legacy LPC project URL, such as the WA_PSLC_2001 project "
            "directory or its prd-tnm S3 metadata index URL. Intersecting "
            "LAZ tiles are downloaded and rasterized as a bare-earth DEM."
        ),
    )
    parser.add_argument(
        "--output",
        required=True,
        help="Final PNG filename, for example duwamish-larger.png.",
    )

    bounds = parser.add_argument_group("geographic crop")
    bounds.add_argument("--west", type=float, help="West longitude.")
    bounds.add_argument("--north", type=float, help="North latitude.")
    bounds.add_argument("--east", type=float, help="East longitude.")
    bounds.add_argument("--south", type=float, help="South latitude.")
    bounds.add_argument(
        "--bounds",
        nargs=4,
        type=float,
        metavar=("WEST", "NORTH", "EAST", "SOUTH"),
        help=(
            "Supply all crop coordinates at once as WEST NORTH EAST SOUTH. "
            "Cannot be combined with the individual coordinate options."
        ),
    )

    parser.add_argument(
        "--projwin-srs",
        default="EPSG:4326",
        help=(
            "Coordinate reference system of the four crop values. "
            "Default: EPSG:4326."
        ),
    )
    parser.add_argument(
        "--clip-level",
        default=100,
        type=int,
        help=(
            "Minimum output value for non-water pixels, defaults to 100 which is water in Worldpainter"
        ),
    )
    parser.add_argument(
        "--land-start",
        type=int,
        default=DEFAULT_LAND_START,
        help=(
            "Stretched input value mapped exactly to --clip-level. Values "
            "between the water value and this value are clipped to the floor. "
            "Default: 8."
        ),
    )
    parser.add_argument(
        "--water-value",
        type=int,
        default=DEFAULT_WATER_INPUT_VALUE,
        help=(
            "Exact value in the stretched image preserved as water. "
            "Default: 0."
        ),
    )
    parser.add_argument(
        "--exponent",
        type=float,
        default=DEFAULT_STRETCH_EXPONENT,
        help="Exponent for the automatic 8-bit stretch. Default: 0.42.",
    )
    parser.add_argument(
        "--height-mode",
        choices=("stretched", "absolute"),
        default="absolute",
        help=(
            "'stretched' contrast-normalizes each map as before. 'absolute' "
            "maps one vertical metre to one image value/Minecraft block. "
            "Default: stretched."
        ),
    )
    parser.add_argument(
        "--minecraft-sea-level",
        type=int,
        default=61,
        help=(
            "Image value and Minecraft Y used for elevation zero in absolute "
            "mode. Default: 61."
        ),
    )
    parser.add_argument(
        "--vertical-units",
        choices=("auto", "meters", "us-survey-feet"),
        default="auto",
        help=(
            "Units of source elevation samples in absolute mode. Auto detects "
            "a vertical CRS and otherwise assumes metres. Default: auto."
        ),
    )
    parser.add_argument(
        "--stretch-scope",
        choices=("requested", "aligned"),
        default="requested",
        help=(
            "Area used to calculate the automatic stretch minimum and "
            "maximum. 'requested' ignores the 512-alignment padding and "
            "matches the contrast of the original unpadded workflow; "
            "'aligned' includes the added east/south terrain. "
            "Default: requested."
        ),
    )
    parser.add_argument(
        "--scale-min",
        type=float,
        help=(
            "Optional explicit source elevation mapped to output 0. Must be "
            "used together with --scale-max. Overrides --stretch-scope."
        ),
    )
    parser.add_argument(
        "--scale-max",
        type=float,
        help=(
            "Optional explicit source elevation mapped to output 255. Must "
            "be used together with --scale-min. Overrides --stretch-scope."
        ),
    )
    parser.add_argument(
        "--meters-per-pixel",
        type=float,
        default=DEFAULT_METERS_PER_PIXEL,
        help="Horizontal output resolution in metres. Default: 1.0.",
    )
    parser.add_argument(
        "--resampling",
        choices=("nearest", "bilinear", "cubic", "average"),
        default="bilinear",
        help=(
            "GDAL raster resampling used while reprojecting to EPSG:6597. "
            "Bilinear is recommended when expanding 3- or 10-metre DEMs to "
            "Minecraft's 1-metre grid. Default: bilinear."
        ),
    )
    parser.add_argument(
        "--tile-size",
        type=int,
        default=DEFAULT_TILE_SIZE_PIXELS,
        help=(
            "Expand the raster eastward and southward until both dimensions "
            "are multiples of this many pixels, preserving the upper-left "
            "corner. Default: 512."
        ),
    )
    parser.add_argument(
        "--chunk-rows",
        type=int,
        default=DEFAULT_CHUNK_ROWS,
        help=(
            "Rows processed at once during the NumPy clipping stage. "
            "Default: 512."
        ),
    )
    parser.add_argument(
        "--keep-intermediates",
        action="store_true",
        help=(
            "Keep the resampled TIFF, stretched PNG, and clipped TIFF in a "
            "directory next to the final output."
        ),
    )
    parser.add_argument(
        "--overwrite",
        action="store_true",
        help="Allow replacing an existing final output file.",
    )
    parser.add_argument(
        "--dry-run",
        action="store_true",
        help="Validate and print the processing plan without loading GDAL.",
    )
    lpc = parser.add_argument_group("USGS LPC/LAZ input")
    lpc.add_argument(
        "--lpc-cache",
        default=".usgs-lpc-cache",
        help="Directory used to cache metadata and downloaded LAZ tiles.",
    )
    lpc.add_argument(
        "--lpc-classification",
        type=int,
        default=2,
        help="LAS classification used for bare earth. Default: 2 (ground).",
    )
    lpc.add_argument(
        "--lpc-source-srs",
        help=(
            "Fallback CRS applied only when LAZ headers omit their spatial "
            "reference. WA_PSLC_2001 is detected as EPSG:2285 automatically."
        ),
    )
    lpc.add_argument(
        "--lpc-ground-mode",
        choices=("auto", "classified", "smrf"),
        default="auto",
        help=(
            "How ground points are obtained. 'classified' uses the existing "
            "--lpc-classification, 'smrf' classifies ground from unclassified "
            "points, and 'auto' uses known project metadata. Default: auto."
        ),
    )
    lpc.add_argument(
        "--lpc-metadata-workers",
        type=int,
        default=12,
        help="Concurrent USGS metadata requests. Default: 12.",
    )

    return parser.parse_args()


def validate_args(args: argparse.Namespace) -> None:
    individual_bounds = (args.west, args.north, args.east, args.south)
    if args.bounds is not None:
        if any(value is not None for value in individual_bounds):
            raise ValueError(
                "--bounds cannot be combined with --west, --north, --east, "
                "or --south."
            )
        args.west, args.north, args.east, args.south = args.bounds
    elif any(value is None for value in individual_bounds):
        raise ValueError(
            "Supply either --bounds WEST NORTH EAST SOUTH or all four of "
            "--west, --north, --east, and --south."
        )

    if not args.west < args.east:
        raise ValueError("--west must be less than --east.")
    if not args.south < args.north:
        raise ValueError("--south must be less than --north.")
    if not 1 <= args.clip_level <= 254:
        raise ValueError("--clip-level must be between 1 and 254.")
    if not 1 <= args.land_start <= 254:
        raise ValueError("--land-start must be between 1 and 254.")
    if not 0 <= args.water_value <= 255:
        raise ValueError("--water-value must be between 0 and 255.")
    if args.water_value == args.land_start:
        raise ValueError("--water-value and --land-start must differ.")
    if args.exponent <= 0.0:
        raise ValueError("--exponent must be greater than zero.")
    if not 0 <= args.minecraft_sea_level <= 255:
        raise ValueError("--minecraft-sea-level must be between 0 and 255.")
    if (args.scale_min is None) != (args.scale_max is None):
        raise ValueError("--scale-min and --scale-max must be supplied together.")
    if args.scale_min is not None:
        if not args.scale_min < args.scale_max:
            raise ValueError("--scale-min must be less than --scale-max.")
    if args.meters_per_pixel <= 0.0:
        raise ValueError("--meters-per-pixel must be greater than zero.")
    if args.tile_size <= 0:
        raise ValueError("--tile-size must be greater than zero.")
    if args.chunk_rows <= 0:
        raise ValueError("--chunk-rows must be greater than zero.")
    if not 0 <= args.lpc_classification <= 255:
        raise ValueError("--lpc-classification must be between 0 and 255.")
    if args.lpc_metadata_workers <= 0:
        raise ValueError("--lpc-metadata-workers must be greater than zero.")

    output = Path(args.output)
    if output.suffix.lower() != ".png":
        raise ValueError("--output must end in .png.")
    if output.exists() and not args.overwrite and not args.dry_run:
        raise FileExistsError(
            f"Output already exists: {output}. Use --overwrite to replace it."
        )

    if args.vrt:
        vrt = args.vrt
        is_virtual_path = vrt.startswith("/vsi") or "://" in vrt
        if not is_virtual_path and not Path(vrt).exists():
            raise FileNotFoundError(f"Input VRT does not exist: {vrt}")


def normalize_usgs_lpc_project_url(url: str) -> tuple[str, str]:
    """Return a browsable project root and stable project name."""

    parsed = urlparse(url)
    query_prefix = parse_qs(parsed.query).get("prefix", [""])[0]
    source = query_prefix or parsed.path
    match = re.search(r"(?:metadata|Projects)/legacy/([^/?#]+)", source)
    if match is None:
        match = re.search(r"/legacy/([^/?#]+)/?", source)
    if match is None:
        raise ValueError(
            "Could not determine the USGS LPC project name from "
            f"--usgs-lpc-project: {url}"
        )

    project_name = match.group(1)
    project_root = (
        "https://rockyweb.usgs.gov/vdelivery/Datasets/Staged/Elevation/"
        f"LPC/Projects/legacy/{project_name}/"
    )
    return project_root, project_name


def read_url(url: str) -> bytes:
    request = Request(url, headers={"User-Agent": "MCMap-lidar/1.0"})
    with urlopen(request, timeout=60) as response:
        return response.read()


def parse_lpc_metadata(xml_bytes: bytes, metadata_url: str) -> LpcTile:
    root = ET.fromstring(xml_bytes)

    def required_text(path: str) -> str:
        element = root.find(path)
        if element is None or not element.text or not element.text.strip():
            raise RuntimeError(
                f"USGS metadata is missing {path}: {metadata_url}"
            )
        return "".join(element.text.split())

    bounds = Bounds(
        west=float(required_text(".//westbc")),
        north=float(required_text(".//northbc")),
        east=float(required_text(".//eastbc")),
        south=float(required_text(".//southbc")),
    )
    laz_url = required_text(".//networkr")
    name = Path(urlparse(laz_url).path).name
    if not name.lower().endswith((".laz", ".las")):
        raise RuntimeError(
            f"USGS metadata does not reference a LAS/LAZ tile: {metadata_url}"
        )
    return LpcTile(name=name, bounds=bounds, laz_url=laz_url)


def discover_lpc_tiles(
    *, project_root: str, cache_directory: Path, workers: int
) -> list[LpcTile]:
    cache_directory.mkdir(parents=True, exist_ok=True)
    index_cache = cache_directory / "tile-index.json"
    if index_cache.exists():
        records = json.loads(index_cache.read_text(encoding="utf-8"))
        return [
            LpcTile(
                name=record["name"],
                bounds=Bounds(**record["bounds"]),
                laz_url=record["laz_url"],
            )
            for record in records
        ]

    metadata_root = urljoin(project_root, "metadata/")
    print(f"Reading USGS tile index: {metadata_root}")
    listing = read_url(metadata_root).decode("utf-8", errors="replace")
    filenames = sorted(
        set(re.findall(r'href=["\']([^"\']+\.xml)["\']', listing, re.I))
    )
    if not filenames:
        raise RuntimeError(
            "The USGS project metadata directory did not list any XML files: "
            f"{metadata_root}"
        )

    tiles: list[LpcTile] = []
    with ThreadPoolExecutor(max_workers=workers) as executor:
        futures = {
            executor.submit(
                lambda metadata_url: parse_lpc_metadata(
                    read_url(metadata_url), metadata_url
                ),
                urljoin(metadata_root, filename),
            ): filename
            for filename in filenames
        }
        completed = 0
        for future in as_completed(futures):
            tiles.append(future.result())
            completed += 1
            print(
                f"\rUSGS metadata: {completed}/{len(filenames)}",
                end="",
                flush=True,
            )
    print()
    tiles.sort(key=lambda tile: tile.name)

    records = [
        {
            "name": tile.name,
            "bounds": {
                "west": tile.bounds.west,
                "north": tile.bounds.north,
                "east": tile.bounds.east,
                "south": tile.bounds.south,
            },
            "laz_url": tile.laz_url,
        }
        for tile in tiles
    ]
    index_cache.write_text(json.dumps(records, indent=2), encoding="utf-8")
    return tiles


def bounds_intersect(left: Bounds, right: Bounds) -> bool:
    return not (
        left.east <= right.west
        or left.west >= right.east
        or left.north <= right.south
        or left.south >= right.north
    )


def download_lpc_tile(tile: LpcTile, tile_directory: Path) -> Path:
    tile_directory.mkdir(parents=True, exist_ok=True)
    destination = tile_directory / tile.name
    if destination.exists() and destination.stat().st_size > 0:
        print(f"Using cached LAZ tile: {tile.name}")
        return destination

    temporary = destination.with_suffix(destination.suffix + ".part")
    print(f"Downloading LAZ tile: {tile.name}")
    request = Request(tile.laz_url, headers={"User-Agent": "MCMap-lidar/1.0"})
    try:
        with urlopen(request, timeout=120) as response, temporary.open("wb") as output:
            total = int(response.headers.get("Content-Length", "0"))
            received = 0
            while True:
                chunk = response.read(1024 * 1024)
                if not chunk:
                    break
                output.write(chunk)
                received += len(chunk)
                if total:
                    print(
                        f"\r  {received * 100 // total:3d}% "
                        f"({received / 1048576:.1f}/{total / 1048576:.1f} MiB)",
                        end="",
                        flush=True,
                    )
        if total:
            print()
        temporary.replace(destination)
    except BaseException:
        try:
            temporary.unlink()
        except FileNotFoundError:
            pass
        raise
    return destination


def transform_bounds(osr: Any, bounds: Bounds, target_srs_text: str) -> Bounds:
    source_srs = osr.SpatialReference()
    source_srs.ImportFromEPSG(4326)
    set_traditional_axis_order(osr, source_srs)
    target_srs = osr.SpatialReference()
    if target_srs.SetFromUserInput(target_srs_text) != 0:
        raise RuntimeError(f"GDAL could not initialise {target_srs_text}.")
    set_traditional_axis_order(osr, target_srs)
    transform = osr.CoordinateTransformation(source_srs, target_srs)
    corners = [
        transform.TransformPoint(bounds.west, bounds.north),
        transform.TransformPoint(bounds.east, bounds.north),
        transform.TransformPoint(bounds.east, bounds.south),
        transform.TransformPoint(bounds.west, bounds.south),
    ]
    return Bounds(
        west=min(point[0] for point in corners),
        north=max(point[1] for point in corners),
        east=max(point[0] for point in corners),
        south=min(point[1] for point in corners),
    )


def create_lpc_dem(
    *,
    osr: Any,
    project_url: str,
    cache_root: Path,
    output_tif: Path,
    geographic_bounds: Bounds,
    metres_per_pixel: float,
    classification: int,
    source_srs: str | None,
    ground_mode: str,
    metadata_workers: int,
) -> Path:
    project_root, project_name = normalize_usgs_lpc_project_url(project_url)
    project_default_srs, project_needs_smrf = LEGACY_LPC_PROJECT_DEFAULTS.get(
        project_name, (None, False)
    )
    fallback_srs = source_srs or project_default_srs
    use_smrf = ground_mode == "smrf" or (
        ground_mode == "auto" and project_needs_smrf
    )
    project_cache = cache_root.resolve() / project_name
    tiles = discover_lpc_tiles(
        project_root=project_root,
        cache_directory=project_cache,
        workers=metadata_workers,
    )
    selected = [tile for tile in tiles if bounds_intersect(tile.bounds, geographic_bounds)]
    if not selected:
        raise RuntimeError(
            f"No {project_name} LAZ tiles intersect the requested coordinates."
        )
    print(f"Selected {len(selected)} of {len(tiles)} LAZ tiles from {project_name}.")
    local_tiles = [
        download_lpc_tile(tile, project_cache / "LAZ") for tile in selected
    ]

    try:
        import pdal
    except ImportError as error:
        raise RuntimeError(
            "The PDAL Python bindings are not installed in this environment. "
            "Run this script with 'conda run -n lidar python lidar.py ...' "
            "or install them with 'conda install -c conda-forge python-pdal'."
        ) from error

    # Crop before the potentially expensive ground classifier. SMRF's distance
    # parameters are defined in the coordinate units, so perform it in metric
    # UTM rather than either Washington State Plane CRS (whose unit is ftUS).
    processing_srs = "EPSG:32610"
    processing_bounds = transform_bounds(osr, geographic_bounds, processing_srs)
    target_srs = "EPSG:6597"
    projected = transform_bounds(osr, geographic_bounds, target_srs)
    resolution = metres_per_pixel * US_SURVEY_FEET_PER_METER
    pipeline: list[dict[str, Any]] = []
    for path in local_tiles:
        reader: dict[str, Any] = {"type": "readers.las", "filename": str(path)}
        if fallback_srs:
            reader["default_srs"] = fallback_srs
        pipeline.append(reader)
    if len(local_tiles) > 1:
        pipeline.append({"type": "filters.merge"})
    pipeline.extend(
        [
            {"type": "filters.reprojection", "out_srs": processing_srs},
            {
                "type": "filters.crop",
                "bounds": (
                    f"([{processing_bounds.west},{processing_bounds.east}],"
                    f"[{processing_bounds.south},{processing_bounds.north}])"
                ),
            },
        ]
    )
    if use_smrf:
        print(
            "The project contains unclassified points; running PDAL SMRF "
            "ground classification."
        )
        pipeline.append({"type": "filters.smrf"})
    pipeline.extend(
        [
            {
                "type": "filters.range",
                "limits": f"Classification[{classification}:{classification}]",
            },
            {"type": "filters.reprojection", "out_srs": target_srs},
            {
                "type": "writers.gdal",
                "filename": str(output_tif),
                "gdaldriver": "GTiff",
                "resolution": resolution,
                "output_type": "idw",
                "window_size": 3,
                "nodata": -9999,
                "data_type": "float32",
                "override_srs": target_srs,
            },
        ]
    )
    print("Rasterizing ground-classified LAZ points with PDAL...")
    try:
        pdal_pipeline = pdal.Pipeline(json.dumps({"pipeline": pipeline}))
        point_count = pdal_pipeline.execute()
    except RuntimeError as error:
        raise RuntimeError(f"PDAL failed to create the terrain DEM: {error}") from error
    if point_count == 0:
        raise RuntimeError(
            "PDAL found no points after ground processing and filtering for "
            f"LAS classification {classification}. Try --lpc-ground-mode "
            "smrf for unclassified data, or choose the appropriate "
            "--lpc-classification for this project."
        )
    print(f"PDAL rasterized {point_count:,} ground points.")
    if not output_tif.exists():
        raise RuntimeError("PDAL completed without creating the terrain DEM.")
    return output_tif


def load_dependencies() -> tuple[Any, Any, Any]:
    try:
        import numpy as np
    except ImportError as error:
        raise RuntimeError(
            "NumPy is not installed in this Python environment."
        ) from error

    try:
        from osgeo import gdal, osr
    except ImportError as error:
        raise RuntimeError(
            "The GDAL Python bindings (the 'osgeo' package) are not installed "
            "in this Python environment. Run the script through the Conda "
            "environment in which gdal_translate works."
        ) from error

    gdal.UseExceptions()
    return gdal, np, osr


def remove_dataset_files(path: Path) -> None:
    """Remove a raster and common sidecars left by GDAL/PAM."""

    candidates = (
        path,
        Path(f"{path}.aux.xml"),
        path.with_suffix(".wld"),
        path.with_suffix(".pgw"),
        path.with_suffix(".tfw"),
    )

    for candidate in candidates:
        try:
            candidate.unlink()
        except FileNotFoundError:
            pass


def copy_spatial_reference(source: Any, destination: Any) -> None:
    projection = source.GetProjection()
    if projection:
        destination.SetProjection(projection)

    try:
        geotransform = source.GetGeoTransform()
    except RuntimeError:
        geotransform = None

    if geotransform is not None:
        destination.SetGeoTransform(geotransform)


def set_traditional_axis_order(osr: Any, spatial_reference: Any) -> None:
    """Use conventional GIS X/Y ordering when the GDAL version supports it."""

    strategy = getattr(osr, "OAMS_TRADITIONAL_GIS_ORDER", None)
    setter = getattr(spatial_reference, "SetAxisMappingStrategy", None)
    if strategy is not None and setter is not None:
        setter(strategy)


def spatial_reference_identity(
    spatial_reference: Any,
) -> tuple[str, str | None]:
    """Return a readable CRS label and, when possible, an authority ID."""

    authority_name = spatial_reference.GetAuthorityName(None)
    authority_code = spatial_reference.GetAuthorityCode(None)

    # Some WKT variants attach the authority to a projected-CRS node rather
    # than the root. Try both WKT1 and WKT2 node names.
    if not authority_name or not authority_code:
        for node_name in ("PROJCS", "PROJCRS"):
            authority_name = spatial_reference.GetAuthorityName(node_name)
            authority_code = spatial_reference.GetAuthorityCode(node_name)
            if authority_name and authority_code:
                break

    authority_id = None
    if authority_name and authority_code:
        authority_id = f"{authority_name}:{authority_code}"

    get_name = getattr(spatial_reference, "GetName", None)
    crs_name = (
        (get_name() if get_name is not None else None)
        or spatial_reference.GetAttrValue("PROJCRS")
        or spatial_reference.GetAttrValue("PROJCS")
        or spatial_reference.GetAttrValue("GEOGCRS")
        or spatial_reference.GetAttrValue("GEOGCS")
        or "unnamed coordinate reference system"
    )

    if authority_id is None:
        return crs_name, None

    return f"{authority_id} ({crs_name})", authority_id


def pixel_to_map_coordinate(
    geotransform: tuple[float, ...],
    pixel_x: float,
    pixel_y: float,
) -> Coordinate:
    """Apply a six-element GDAL geotransform to a pixel corner."""

    return Coordinate(
        x=(
            float(geotransform[0])
            + pixel_x * float(geotransform[1])
            + pixel_y * float(geotransform[2])
        ),
        y=(
            float(geotransform[3])
            + pixel_x * float(geotransform[4])
            + pixel_y * float(geotransform[5])
        ),
    )


def read_map_bounds(
    *,
    gdal: Any,
    osr: Any,
    raster_path: Path,
) -> MapBounds:
    """Read exact projected and WGS84 bounds from the aligned raster."""

    dataset = gdal.Open(str(raster_path), gdal.GA_ReadOnly)
    if dataset is None:
        raise RuntimeError(f"GDAL could not open the aligned TIFF: {raster_path}")

    try:
        geotransform = dataset.GetGeoTransform()
        if geotransform is None or len(geotransform) != 6:
            raise RuntimeError(
                "The aligned raster does not contain a usable geotransform."
            )

        projection = dataset.GetProjectionRef() or dataset.GetProjection()
        if not projection:
            raise RuntimeError(
                "The aligned raster does not contain a coordinate reference "
                "system, so geographic bounds cannot be calculated."
            )

        source_srs = osr.SpatialReference()
        if source_srs.ImportFromWkt(projection) != 0:
            raise RuntimeError(
                "GDAL could not parse the aligned raster's coordinate "
                "reference system."
            )
        set_traditional_axis_order(osr, source_srs)

        target_srs = osr.SpatialReference()
        if target_srs.ImportFromEPSG(4326) != 0:
            raise RuntimeError("GDAL could not initialise EPSG:4326.")
        set_traditional_axis_order(osr, target_srs)

        width = float(dataset.RasterXSize)
        height = float(dataset.RasterYSize)

        projected_north_west = pixel_to_map_coordinate(
            geotransform, 0.0, 0.0
        )
        projected_north_east = pixel_to_map_coordinate(
            geotransform, width, 0.0
        )
        projected_south_east = pixel_to_map_coordinate(
            geotransform, width, height
        )
        projected_south_west = pixel_to_map_coordinate(
            geotransform, 0.0, height
        )
        projected_corners = (
            projected_north_west,
            projected_north_east,
            projected_south_east,
            projected_south_west,
        )

        projected_bounds = Bounds(
            west=min(point.x for point in projected_corners),
            north=max(point.y for point in projected_corners),
            east=max(point.x for point in projected_corners),
            south=min(point.y for point in projected_corners),
        )

        coordinate_transform = osr.CoordinateTransformation(
            source_srs, target_srs
        )

        def to_wgs84(point: Coordinate) -> Coordinate:
            transformed = coordinate_transform.TransformPoint(point.x, point.y)
            if transformed is None or len(transformed) < 2:
                raise RuntimeError(
                    "GDAL failed to transform a raster corner to EPSG:4326."
                )
            # Traditional GIS axis order makes X longitude and Y latitude.
            return Coordinate(x=float(transformed[0]), y=float(transformed[1]))

        north_west = to_wgs84(projected_north_west)
        north_east = to_wgs84(projected_north_east)
        south_east = to_wgs84(projected_south_east)
        south_west = to_wgs84(projected_south_west)
        geographic_corners = (
            north_west,
            north_east,
            south_east,
            south_west,
        )

        geographic_bounds = Bounds(
            west=min(point.x for point in geographic_corners),
            north=max(point.y for point in geographic_corners),
            east=max(point.x for point in geographic_corners),
            south=min(point.y for point in geographic_corners),
        )

        source_crs_label, source_crs_argument = spatial_reference_identity(
            source_srs
        )

        return MapBounds(
            source_crs_label=source_crs_label,
            source_crs_argument=source_crs_argument,
            projected=projected_bounds,
            geographic=geographic_bounds,
            north_west=north_west,
            north_east=north_east,
            south_east=south_east,
            south_west=south_west,
        )
    finally:
        dataset = None


def print_map_bounds(bounds: MapBounds) -> None:
    """Print final raster bounds in human- and GDAL-friendly forms."""

    projected = bounds.projected
    geographic = bounds.geographic

    print("\nFinal aligned map bounds:")
    print(f"  Source CRS: {bounds.source_crs_label}")
    print("  Projected envelope (west, north, east, south):")
    print(
        "    "
        f"{projected.west:.9f} {projected.north:.9f} "
        f"{projected.east:.9f} {projected.south:.9f}"
    )
    print("  MCMap raster origin:")
    print(f"    /pos:{projected.west:.9f},{projected.north:.9f}")
    print("  EPSG:4326 envelope (west, north, east, south):")
    print(
        "    "
        f"{geographic.west:.10f} {geographic.north:.10f} "
        f"{geographic.east:.10f} {geographic.south:.10f}"
    )
    print("  Exact EPSG:4326 corners (longitude, latitude):")
    print(
        f"    northwest: {bounds.north_west.x:.10f}, "
        f"{bounds.north_west.y:.10f}"
    )
    print(
        f"    northeast: {bounds.north_east.x:.10f}, "
        f"{bounds.north_east.y:.10f}"
    )
    print(
        f"    southeast: {bounds.south_east.x:.10f}, "
        f"{bounds.south_east.y:.10f}"
    )
    print(
        f"    southwest: {bounds.south_west.x:.10f}, "
        f"{bounds.south_west.y:.10f}"
    )

    if bounds.source_crs_argument is not None:
        print("  Reusable projected GDAL window:")
        print(
            "    -projwin "
            f"{projected.west:.9f} {projected.north:.9f} "
            f"{projected.east:.9f} {projected.south:.9f} "
            f"-projwin_srs {bounds.source_crs_argument}"
        )

    print("  Reusable geographic GDAL window:")
    print(
        "    -projwin "
        f"{geographic.west:.10f} {geographic.north:.10f} "
        f"{geographic.east:.10f} {geographic.south:.10f} "
        "-projwin_srs EPSG:4326"
    )


def round_up_to_multiple(value: int, multiple: int) -> int:
    """Round a positive integer up to the next positive multiple."""

    if value <= 0:
        raise ValueError("value must be greater than zero.")
    if multiple <= 0:
        raise ValueError("multiple must be greater than zero.")

    return ((value + multiple - 1) // multiple) * multiple


def crop_and_resample(
    *,
    gdal: Any,
    osr: Any,
    source_vrt: str,
    output_tif: Path,
    west: float,
    north: float,
    east: float,
    south: float,
    projwin_srs: str,
    metres_per_pixel: float,
    tile_size: int,
    resampling: str,
    vertical_units: str,
) -> tuple[int, int, int, int, float, str]:
    """Crop and resample while anchoring the literal input northwest point.

    A longitude/latitude rectangle generally becomes a slightly rotated or
    curved quadrilateral in a projected CRS. GDAL's normal ``projWinSRS``
    handling converts that quadrilateral to a projected axis-aligned envelope.
    The envelope's upper-left corner is not necessarily the transformed
    ``(west, north)`` point.

    This implementation transforms the literal northwest point itself and uses
    it as pixel corner (0, 0). It then grows the raster only eastward and
    southward. Consequently, a narrow part of the requested geographic box can
    lie west or north of the anchored projected grid. Such an overhang is
    reported rather than silently moving the anchor.
    """

    print("\nStage 1/4: reprojecting, cropping, aligning, and resampling")

    source = gdal.OpenEx(source_vrt, gdal.OF_RASTER | gdal.OF_READONLY)
    if source is None:
        raise RuntimeError(f"GDAL could not open the input raster: {source_vrt}")

    result = None
    try:
        projection = source.GetProjectionRef() or source.GetProjection()
        if not projection:
            raise RuntimeError(
                "The input raster has no coordinate reference system, so the "
                "requested northwest point cannot be transformed."
            )

        source_srs = osr.SpatialReference()
        if source_srs.ImportFromWkt(projection) != 0:
            raise RuntimeError(
                "GDAL could not parse the input raster coordinate reference "
                "system."
            )
        set_traditional_axis_order(osr, source_srs)

        if vertical_units == "meters":
            vertical_metres_per_unit = 1.0
            vertical_unit_label = "metres (explicit)"
        elif vertical_units == "us-survey-feet":
            vertical_metres_per_unit = 1.0 / US_SURVEY_FEET_PER_METER
            vertical_unit_label = "US survey feet (explicit)"
        else:
            vertical_unit_name = source_srs.GetAttrValue("VERT_CS|UNIT") or ""
            if "foot" in vertical_unit_name.lower() or "feet" in vertical_unit_name.lower():
                vertical_metres_per_unit = 1.0 / US_SURVEY_FEET_PER_METER
                vertical_unit_label = f"{vertical_unit_name} (from vertical CRS)"
            else:
                vertical_metres_per_unit = 1.0
                vertical_unit_label = (
                    f"{vertical_unit_name} (from vertical CRS)"
                    if vertical_unit_name
                    else "metres (assumed; source has no vertical CRS)"
                )

        target_srs = osr.SpatialReference()
        if target_srs.ImportFromEPSG(6597) != 0:
            raise RuntimeError("GDAL could not initialise output CRS EPSG:6597.")
        set_traditional_axis_order(osr, target_srs)

        request_srs = osr.SpatialReference()
        set_from_user_input = getattr(request_srs, "SetFromUserInput", None)
        if set_from_user_input is None:
            raise RuntimeError(
                "This GDAL build does not expose SpatialReference."
                "SetFromUserInput()."
            )
        if set_from_user_input(projwin_srs) != 0:
            raise RuntimeError(
                f"GDAL could not parse --projwin-srs {projwin_srs!r}."
            )
        set_traditional_axis_order(osr, request_srs)

        coordinate_transform = osr.CoordinateTransformation(request_srs, target_srs)

        def transform_point(x: float, y: float) -> Coordinate:
            transformed = coordinate_transform.TransformPoint(x, y)
            if transformed is None or len(transformed) < 2:
                raise RuntimeError(
                    "GDAL failed to transform a requested-boundary point "
                    "into output CRS EPSG:6597."
                )
            return Coordinate(float(transformed[0]), float(transformed[1]))

        # Transform the literal input northwest coordinate. This is the exact
        # EPSG:6597 coordinate assigned to the upper-left raster corner.
        anchor = transform_point(west, north)

        # Densify each edge before finding the projected extrema. This is more
        # robust than transforming only the four corners when the CRS change
        # bends an edge slightly.
        edge_segments = 100
        projected_boundary: list[Coordinate] = []
        for index in range(edge_segments + 1):
            fraction = index / edge_segments
            longitude = west + (east - west) * fraction
            latitude = south + (north - south) * fraction

            projected_boundary.append(transform_point(longitude, north))
            projected_boundary.append(transform_point(longitude, south))
            projected_boundary.append(transform_point(west, latitude))
            projected_boundary.append(transform_point(east, latitude))

        projected_west = min(point.x for point in projected_boundary)
        projected_north = max(point.y for point in projected_boundary)
        projected_east = max(point.x for point in projected_boundary)
        projected_south = min(point.y for point in projected_boundary)

        resolution_us_ft = metres_per_pixel * US_SURVEY_FEET_PER_METER

        # The output starts exactly at the transformed northwest point. Round
        # outward so the requested east and south extrema are included.
        requested_width_float = (projected_east - anchor.x) / resolution_us_ft
        requested_height_float = (anchor.y - projected_south) / resolution_us_ft
        requested_width = max(
            1,
            int(math.ceil(requested_width_float - 1e-12)),
        )
        requested_height = max(
            1,
            int(math.ceil(requested_height_float - 1e-12)),
        )

        aligned_width = round_up_to_multiple(requested_width, tile_size)
        aligned_height = round_up_to_multiple(requested_height, tile_size)
        added_columns = aligned_width - requested_width
        added_rows = aligned_height - requested_height

        lower_right_x = anchor.x + aligned_width * resolution_us_ft
        lower_right_y = anchor.y - aligned_height * resolution_us_ft

        west_overhang_units = max(0.0, anchor.x - projected_west)
        north_overhang_units = max(0.0, projected_north - anchor.y)
        west_overhang_metres = west_overhang_units / US_SURVEY_FEET_PER_METER
        north_overhang_metres = north_overhang_units / US_SURVEY_FEET_PER_METER

        print(
            "  Literal northwest anchor: "
            f"{west:.10f}, {north:.10f} ({projwin_srs})"
        )
        print(
            "  EPSG:6597 anchor:          "
            f"{anchor.x:.9f}, {anchor.y:.9f}"
        )
        source_label, _ = spatial_reference_identity(source_srs)
        print(f"  Input raster CRS:          {source_label}")
        print(f"  Source elevation units:    {vertical_unit_label}")
        print(f"  Requested size: {requested_width} x {requested_height}")
        print(f"  Aligned size:   {aligned_width} x {aligned_height}")
        print(
            f"  Expansion:      {added_columns} pixel(s) east, "
            f"{added_rows} pixel(s) south"
        )

        overhang_tolerance_metres = metres_per_pixel * 1e-6
        if (
            west_overhang_metres > overhang_tolerance_metres
            or north_overhang_metres > overhang_tolerance_metres
        ):
            print(
                "  Geographic-box tradeoff: preserving the literal northwest "
                "anchor means the projected grid cannot also contain every "
                "point of the axis-aligned geographic box without extending "
                "west or north."
            )
            if west_overhang_metres > overhang_tolerance_metres:
                print(
                    "    Maximum requested-area overhang west of the anchor: "
                    f"{west_overhang_metres:.3f} metre(s)"
                )
            if north_overhang_metres > overhang_tolerance_metres:
                print(
                    "    Maximum requested-area overhang north of the anchor: "
                    f"{north_overhang_metres:.3f} metre(s)"
                )

        progress = ProgressReporter("Crop/resample")
        source_band = source.GetRasterBand(1)
        source_no_data = None if source_band is None else source_band.GetNoDataValue()
        warp_arguments: dict[str, Any] = {
            "format": "GTiff",
            "outputType": gdal.GDT_Float32,
            "dstSRS": target_srs.ExportToWkt(),
            "outputBounds": [anchor.x, lower_right_y, lower_right_x, anchor.y],
            "width": aligned_width,
            "height": aligned_height,
            "resampleAlg": resampling,
            "dstNodata": -9999.0,
            "multithread": True,
            "creationOptions": [
                "TILED=YES",
                "COMPRESS=DEFLATE",
                "PREDICTOR=3",
                "BIGTIFF=IF_SAFER",
            ],
            "callback": progress,
        }
        if source_no_data is not None:
            warp_arguments["srcNodata"] = source_no_data
        options = gdal.WarpOptions(
            **warp_arguments,
        )

        try:
            result = gdal.Warp(str(output_tif), source, options=options)
        finally:
            progress.finish()

        if result is None:
            raise RuntimeError("gdal.Warp() did not create the aligned TIFF.")
        result.FlushCache()

        width = int(result.RasterXSize)
        height = int(result.RasterYSize)
        if width != aligned_width or height != aligned_height:
            raise RuntimeError(
                "GDAL created an unexpectedly sized aligned raster: "
                f"{width} x {height}; expected "
                f"{aligned_width} x {aligned_height}."
            )

        result_geotransform = result.GetGeoTransform()
        anchor_tolerance = max(resolution_us_ft, 1.0) * 1e-8
        if (
            abs(float(result_geotransform[0]) - anchor.x) > anchor_tolerance
            or abs(float(result_geotransform[3]) - anchor.y)
            > anchor_tolerance
        ):
            raise RuntimeError(
                "GDAL shifted the literal northwest anchor while creating "
                "the aligned raster."
            )

        output_pixel_width = float(result_geotransform[1])
        output_pixel_height = float(result_geotransform[5])
        resolution_tolerance = max(resolution_us_ft, 1.0) * 1e-8
        if (
            abs(output_pixel_width - resolution_us_ft) > resolution_tolerance
            or abs(output_pixel_height + resolution_us_ft)
            > resolution_tolerance
        ):
            raise RuntimeError(
                "GDAL created an unexpected output resolution: "
                f"({output_pixel_width}, {output_pixel_height}); expected "
                f"({resolution_us_ft}, {-resolution_us_ft})."
            )
    finally:
        result = None
        source = None

    return (
        requested_width,
        requested_height,
        width,
        height,
        vertical_metres_per_unit,
        vertical_unit_label,
    )


def compute_stretch_range(
    *,
    gdal: Any,
    np: Any,
    source_tif: Path,
    width: int,
    height: int,
    chunk_rows: int,
    label: str,
) -> tuple[float, float]:
    """Compute an exact valid-data min/max over an upper-left raster window.

    The aligned raster keeps the requested upper-left corner and pixel grid,
    so the original unpadded crop is exactly the window (0, 0, width, height).
    Computing statistics over that window prevents the east/south alignment
    padding from changing the contrast of the requested map area.
    """

    source = gdal.Open(str(source_tif), gdal.GA_ReadOnly)
    if source is None:
        raise RuntimeError(f"GDAL could not open the resampled TIFF: {source_tif}")

    band = source.GetRasterBand(1)
    if band is None:
        source = None
        raise RuntimeError("The resampled TIFF does not contain raster band 1.")

    if width <= 0 or height <= 0:
        source = None
        raise ValueError("The statistics window must have positive dimensions.")
    if width > source.RasterXSize or height > source.RasterYSize:
        source = None
        raise ValueError(
            "The statistics window extends beyond the aligned raster: "
            f"{width} x {height} versus "
            f"{source.RasterXSize} x {source.RasterYSize}."
        )

    no_data = band.GetNoDataValue()
    mask_flags = band.GetMaskFlags()
    all_valid = bool(mask_flags & gdal.GMF_ALL_VALID)
    mask_band = None if all_valid else band.GetMaskBand()

    minimum = float("inf")
    maximum = float("-inf")
    found_valid_value = False
    next_report = 0

    print(
        f"Computing the exact stretch range from {label}: "
        f"{width} x {height} pixels"
    )

    try:
        for y_offset in range(0, height, chunk_rows):
            rows = min(chunk_rows, height - y_offset)
            values = band.ReadAsArray(0, y_offset, width, rows)
            if values is None:
                raise RuntimeError(
                    f"GDAL failed reading statistics rows {y_offset} through "
                    f"{y_offset + rows - 1}."
                )

            valid = np.isfinite(values)

            if no_data is not None:
                if np.isnan(no_data):
                    valid &= ~np.isnan(values)
                else:
                    valid &= values != no_data

            if mask_band is not None:
                mask_values = mask_band.ReadAsArray(0, y_offset, width, rows)
                if mask_values is None:
                    raise RuntimeError(
                        f"GDAL failed reading the validity mask for rows "
                        f"{y_offset} through {y_offset + rows - 1}."
                    )
                valid &= mask_values != 0

            if np.any(valid):
                valid_values = values[valid]
                minimum = min(minimum, float(valid_values.min()))
                maximum = max(maximum, float(valid_values.max()))
                found_valid_value = True

            percent = int(((y_offset + rows) / height) * 100.0)
            if percent >= next_report or percent == 100:
                print(f"\rStretch statistics: {percent:3d}%", end="", flush=True)
                next_report = ((percent // 5) + 1) * 5

        print()
    finally:
        source = None

    if not found_valid_value or not minimum < maximum:
        raise RuntimeError(
            "The statistics window has no usable elevation range: "
            f"min={minimum}, max={maximum}."
        )

    return minimum, maximum


def stretch_to_byte_png(
    *,
    gdal: Any,
    source_tif: Path,
    output_png: Path,
    exponent: float,
    minimum: float,
    maximum: float,
) -> None:
    print("\nStage 2/4: applying the grayscale stretch")

    if not minimum < maximum:
        raise ValueError("The stretch minimum must be less than its maximum.")

    source = gdal.Open(str(source_tif), gdal.GA_ReadOnly)
    if source is None:
        raise RuntimeError(f"GDAL could not open the resampled TIFF: {source_tif}")

    progress = ProgressReporter("Stretch")
    options = gdal.TranslateOptions(
        format="PNG",
        outputType=gdal.GDT_Byte,
        scaleParams=[[minimum, maximum, 0.0, float(OUTPUT_MAXIMUM)]],
        exponents=[exponent],
        colorInterpretation=["gray"],
        callback=progress,
    )

    result = None
    try:
        result = gdal.Translate(str(output_png), source, options=options)
        if result is None:
            raise RuntimeError("gdal.Translate() did not create the stretched PNG.")
        result.FlushCache()
    finally:
        progress.finish()
        result = None
        source = None


def map_absolute_elevation_to_byte(
    *,
    gdal: Any,
    np: Any,
    source_tif: Path,
    output_tif: Path,
    minecraft_sea_level: int,
    vertical_metres_per_unit: float,
    chunk_rows: int,
) -> tuple[float, float, int, int]:
    """Encode absolute elevation so one metre equals one grayscale step."""

    print("\nStage 2/4: mapping absolute elevation at one metre per block")
    source = gdal.Open(str(source_tif), gdal.GA_ReadOnly)
    if source is None:
        raise RuntimeError(f"GDAL could not open the resampled TIFF: {source_tif}")
    source_band = source.GetRasterBand(1)
    if source_band is None:
        source = None
        raise RuntimeError("The resampled TIFF does not contain raster band 1.")

    width = source.RasterXSize
    height = source.RasterYSize
    driver = gdal.GetDriverByName("GTiff")
    destination = driver.Create(
        str(output_tif), width, height, 1, gdal.GDT_Byte,
        options=["TILED=YES", "COMPRESS=DEFLATE", "PREDICTOR=2"],
    )
    if destination is None:
        source = None
        raise RuntimeError(f"GDAL could not create the absolute-height TIFF: {output_tif}")

    no_data = source_band.GetNoDataValue()
    minimum = float("inf")
    maximum = float("-inf")
    clipped_low = 0
    clipped_high = 0
    next_report = 0
    try:
        copy_spatial_reference(source, destination)
        destination_band = destination.GetRasterBand(1)
        destination_band.SetColorInterpretation(gdal.GCI_GrayIndex)
        for y_offset in range(0, height, chunk_rows):
            rows = min(chunk_rows, height - y_offset)
            values = source_band.ReadAsArray(0, y_offset, width, rows)
            if values is None:
                raise RuntimeError(
                    f"GDAL failed reading rows {y_offset} through "
                    f"{y_offset + rows - 1}."
                )

            valid = np.isfinite(values)
            if no_data is not None:
                valid &= values != no_data
            elevations = values.astype(np.float64) * vertical_metres_per_unit
            if np.any(valid):
                valid_elevations = elevations[valid]
                minimum = min(minimum, float(valid_elevations.min()))
                maximum = max(maximum, float(valid_elevations.max()))

            mapped = elevations + float(minecraft_sea_level)
            clipped_low += int(np.count_nonzero(valid & (mapped < 0.0)))
            clipped_high += int(np.count_nonzero(valid & (mapped > 255.0)))
            np.clip(mapped, 0.0, 255.0, out=mapped)
            # USGS terrain DEMs commonly mark open water as NoData. Treat it
            # as elevation zero so WorldPainter fills it at the configured
            # sea level instead of creating a deep void at image value zero.
            mapped[~valid] = float(minecraft_sea_level)
            error_code = destination_band.WriteArray(
                np.rint(mapped).astype(np.uint8), 0, y_offset
            )
            if error_code not in (None, 0):
                raise RuntimeError(
                    f"GDAL failed writing rows {y_offset} through "
                    f"{y_offset + rows - 1}; error code {error_code}."
                )

            percent = int(((y_offset + rows) / height) * 100.0)
            if percent >= next_report or percent == 100:
                print(f"\rAbsolute height: {percent:3d}%", end="", flush=True)
                next_report = ((percent // 5) + 1) * 5
        print()
        destination_band.FlushCache()
        destination.FlushCache()
    finally:
        destination = None
        source = None

    if minimum == float("inf"):
        raise RuntimeError("The aligned raster contains no valid elevations.")
    return minimum, maximum, clipped_low, clipped_high


def apply_land_floor(
    *,
    gdal: Any,
    np: Any,
    source_png: Path,
    output_tif: Path,
    clip_level: int,
    land_start: int,
    water_value: int,
    chunk_rows: int,
) -> None:
    print("\nStage 3/4: applying the non-water floor")

    source = gdal.Open(str(source_png), gdal.GA_ReadOnly)
    if source is None:
        raise RuntimeError(f"GDAL could not open the stretched PNG: {source_png}")

    source_band = source.GetRasterBand(1)
    if source_band is None:
        source = None
        raise RuntimeError("The stretched PNG does not contain raster band 1.")

    width = source.RasterXSize
    height = source.RasterYSize

    driver = gdal.GetDriverByName("GTiff")
    if driver is None:
        source = None
        raise RuntimeError("The GDAL GeoTIFF driver is unavailable.")

    destination = driver.Create(
        str(output_tif),
        width,
        height,
        1,
        gdal.GDT_Byte,
        options=[
            "TILED=YES",
            "COMPRESS=DEFLATE",
            "PREDICTOR=2",
            "BIGTIFF=IF_SAFER",
        ],
    )
    if destination is None:
        source = None
        raise RuntimeError(f"GDAL could not create the clipped TIFF: {output_tif}")

    try:
        copy_spatial_reference(source, destination)

        destination_band = destination.GetRasterBand(1)
        if destination_band is None:
            raise RuntimeError("The clipped TIFF does not contain raster band 1.")
        destination_band.SetColorInterpretation(gdal.GCI_GrayIndex)

        output_span = float(OUTPUT_MAXIMUM - clip_level)
        input_span = float(OUTPUT_MAXIMUM - land_start)
        scale = output_span / input_span

        next_report = 0
        for y_offset in range(0, height, chunk_rows):
            rows = min(chunk_rows, height - y_offset)
            values = source_band.ReadAsArray(0, y_offset, width, rows)
            if values is None:
                raise RuntimeError(
                    f"GDAL failed reading rows {y_offset} through "
                    f"{y_offset + rows - 1}."
                )

            water_mask = values <= water_value
            output_values = (
                clip_level
                + (values.astype(np.float32) - float(land_start)) * scale
            )
            np.clip(
                output_values,
                float(clip_level),
                float(OUTPUT_MAXIMUM),
                out=output_values,
            )
            output_values[water_mask] = clip_level

            # GDAL's numeric conversion rounds floating-point values when
            # writing to an integer band. Make that rounding explicit.
            byte_values = np.rint(output_values).astype(np.uint8)

            error_code = destination_band.WriteArray(byte_values, 0, y_offset)
            if error_code not in (None, 0):
                raise RuntimeError(
                    f"GDAL failed writing rows {y_offset} through "
                    f"{y_offset + rows - 1}; error code {error_code}."
                )

            percent = int(((y_offset + rows) / height) * 100.0)
            if percent >= next_report or percent == 100:
                print(f"\rClip/floor: {percent:3d}%", end="", flush=True)
                next_report = ((percent // 5) + 1) * 5

        print()
        destination_band.FlushCache()
        destination.FlushCache()
    finally:
        destination = None
        source = None


def create_final_png(
    *,
    gdal: Any,
    source_tif: Path,
    output_png: Path,
) -> None:
    print("\nStage 4/4: creating the final PNG")

    source = gdal.Open(str(source_tif), gdal.GA_ReadOnly)
    if source is None:
        raise RuntimeError(f"GDAL could not open the clipped TIFF: {source_tif}")

    driver = gdal.GetDriverByName("PNG")
    if driver is None:
        source = None
        raise RuntimeError("The GDAL PNG driver is unavailable.")

    progress = ProgressReporter("PNG CreateCopy")
    result = None
    try:
        # PNG is commonly a CreateCopy-only driver. This is why the clipping
        # stage writes to GeoTIFF first and the final step uses CreateCopy().
        result = driver.CreateCopy(
            str(output_png),
            source,
            strict=0,
            options=[],
            callback=progress,
        )
        if result is None:
            raise RuntimeError("The PNG driver did not create the final image.")
        result.FlushCache()
    finally:
        progress.finish()
        result = None
        source = None


def make_working_directory(output: Path, keep: bool) -> tuple[Any, Path]:
    if not keep:
        temporary = tempfile.TemporaryDirectory(prefix=f"{output.stem}-gdal-api-")
        return temporary, Path(temporary.name)

    work_dir = output.parent / f"{output.stem}-intermediate"
    work_dir.mkdir(parents=True, exist_ok=True)

    class RetainedDirectory:
        def __enter__(self) -> str:
            return str(work_dir)

        def __exit__(self, exc_type: Any, exc_value: Any, traceback: Any) -> bool:
            return False

    return RetainedDirectory(), work_dir


def print_plan(args: argparse.Namespace, output: Path) -> None:
    resolution_us_ft = args.meters_per_pixel * US_SURVEY_FEET_PER_METER

    print("Processing plan:")
    if args.vrt:
        print(f"  Input raster: {args.vrt}")
    else:
        print(f"  USGS LPC project: {args.usgs_lpc_project}")
        print(f"  LAZ cache: {Path(args.lpc_cache).resolve()}")
        print(
            "  Point-cloud preprocessing: classification "
            f"{args.lpc_classification} -> bare-earth DEM via Python PDAL "
            f"(ground mode: {args.lpc_ground_mode})"
        )
        if args.lpc_source_srs:
            print(f"  Missing LAZ CRS fallback: {args.lpc_source_srs}")
    print(
        f"  Window: west={args.west}, north={args.north}, "
        f"east={args.east}, south={args.south} ({args.projwin_srs})"
    )
    print(
        f"  Resolution: {args.meters_per_pixel:g} metre(s)/pixel "
        f"({resolution_us_ft:.15g} ftUS/pixel in EPSG:6597)"
    )
    print(f"  Raster resampling: {args.resampling}")
    print(
        f"  Pixel alignment: multiples of {args.tile_size}; "
        "keep the literal input northwest coordinate fixed and expand "
        "east and south only"
    )
    print(f"  Height mode: {args.height_mode}")
    if args.height_mode == "absolute":
        print(
            "  Vertical mapping: one source metre = one image value = "
            "one Minecraft block"
        )
        print(f"  Real elevation 0 maps to Minecraft Y: {args.minecraft_sea_level}")
        print(f"  Source vertical units: {args.vertical_units}")
    else:
        print(f"  Stretch exponent: {args.exponent:g}")
        if args.scale_min is not None:
            print(
                f"  Stretch range: explicit {args.scale_min:g} through "
                f"{args.scale_max:g}"
            )
        else:
            print(f"  Stretch statistics scope: {args.stretch_scope}")
        print(f"  Water value preserved: {args.water_value}")
        print(
            f"  Non-water values through {args.land_start} become: "
            f"{args.clip_level}"
        )
    print(f"  Final output: {output}")
    if args.vrt:
        print("  Execution: in-process osgeo.gdal + NumPy; no subprocesses")
    else:
        print("  Execution: in-process Python PDAL + GDAL + NumPy")


def main() -> int:
    args = parse_args()

    try:
        validate_args(args)

        output = Path(args.output).resolve()
        output.parent.mkdir(parents=True, exist_ok=True)
        print_plan(args, output)

        if args.dry_run:
            return 0

        gdal, np, osr = load_dependencies()

        if args.overwrite:
            remove_dataset_files(output)

        context, work_dir = make_working_directory(
            output, args.keep_intermediates
        )

        with context:
            source_input = args.vrt
            if args.usgs_lpc_project:
                source_input = str(
                    create_lpc_dem(
                        osr=osr,
                        project_url=args.usgs_lpc_project,
                        cache_root=Path(args.lpc_cache),
                        output_tif=work_dir / f"{output.stem}-lpc-dem.tif",
                        geographic_bounds=Bounds(
                            west=args.west,
                            north=args.north,
                            east=args.east,
                            south=args.south,
                        ),
                        metres_per_pixel=args.meters_per_pixel,
                        classification=args.lpc_classification,
                        source_srs=args.lpc_source_srs,
                        ground_mode=args.lpc_ground_mode,
                        metadata_workers=args.lpc_metadata_workers,
                    )
                )

            one_metre_tif = work_dir / f"{output.stem}-1m.tif"
            stretched_png = work_dir / f"{output.stem}-stretched.png"
            clipped_tif = work_dir / f"{output.stem}-clipped.tif"

            for intermediate in (one_metre_tif, stretched_png, clipped_tif):
                remove_dataset_files(intermediate)

            (
                requested_width,
                requested_height,
                width,
                height,
                vertical_metres_per_unit,
                vertical_unit_label,
            ) = crop_and_resample(
                gdal=gdal,
                osr=osr,
                source_vrt=source_input,
                output_tif=one_metre_tif,
                west=args.west,
                north=args.north,
                east=args.east,
                south=args.south,
                projwin_srs=args.projwin_srs,
                metres_per_pixel=args.meters_per_pixel,
                tile_size=args.tile_size,
                resampling=args.resampling,
                vertical_units=args.vertical_units,
            )

            map_bounds = read_map_bounds(
                gdal=gdal,
                osr=osr,
                raster_path=one_metre_tif,
            )

            clipped_low = 0
            clipped_high = 0
            if args.height_mode == "absolute":
                minimum, maximum, clipped_low, clipped_high = (
                    map_absolute_elevation_to_byte(
                        gdal=gdal,
                        np=np,
                        source_tif=one_metre_tif,
                        output_tif=clipped_tif,
                        minecraft_sea_level=args.minecraft_sea_level,
                        vertical_metres_per_unit=vertical_metres_per_unit,
                        chunk_rows=args.chunk_rows,
                    )
                )
            else:
                if args.scale_min is not None:
                    minimum = float(args.scale_min)
                    maximum = float(args.scale_max)
                    print(
                        "\nUsing the explicit stretch range: "
                        f"{minimum:g} through {maximum:g}"
                    )
                else:
                    if args.stretch_scope == "requested":
                        statistics_width = requested_width
                        statistics_height = requested_height
                        statistics_label = "the original requested window"
                    else:
                        statistics_width = width
                        statistics_height = height
                        statistics_label = "the full 512-aligned raster"

                    minimum, maximum = compute_stretch_range(
                        gdal=gdal,
                        np=np,
                        source_tif=one_metre_tif,
                        width=statistics_width,
                        height=statistics_height,
                        chunk_rows=args.chunk_rows,
                        label=statistics_label,
                    )

                stretch_to_byte_png(
                    gdal=gdal,
                    source_tif=one_metre_tif,
                    output_png=stretched_png,
                    exponent=args.exponent,
                    minimum=minimum,
                    maximum=maximum,
                )

                apply_land_floor(
                    gdal=gdal,
                    np=np,
                    source_png=stretched_png,
                    output_tif=clipped_tif,
                    clip_level=args.clip_level,
                    land_start=args.land_start,
                    water_value=args.water_value,
                    chunk_rows=args.chunk_rows,
                )

            create_final_png(
                gdal=gdal,
                source_tif=clipped_tif,
                output_png=output,
            )

            print("\nCompleted:")
            print(
                f"  Requested crop size: {requested_width} x {requested_height}"
            )
            print(f"  Final aligned size:  {width} x {height}")
            if args.height_mode == "absolute":
                print(
                    f"  Source elevation range: {minimum:g} through "
                    f"{maximum:g} metres"
                )
                print(f"  Detected source units: {vertical_unit_label}")
                print(
                    "  Absolute mapping: Minecraft Y = round(elevation metres + "
                    f"{args.minecraft_sea_level})"
                )
                if clipped_low or clipped_high:
                    print(
                        f"  Warning: {clipped_low} pixels clipped below Y 0 and "
                        f"{clipped_high} pixels clipped above Y 255"
                    )
            else:
                print(f"  Source elevation range: {minimum:g} through {maximum:g}")
            print(f"  Output: {output}")
            if args.keep_intermediates:
                print(f"  Intermediate files: {work_dir}")

            print_map_bounds(map_bounds)

        return 0

    except (
        ValueError,
        FileNotFoundError,
        FileExistsError,
        RuntimeError,
        OSError,
    ) as error:
        print(f"Error: {error}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
