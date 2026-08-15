using Substrate;
using System;
using System.Collections.Generic;
using System.Linq;

namespace MinecraftMapper {
    partial class Mapper {
        private const long ClimatePledgeArenaWayId = 25123756;
        private const long SpaceNeedleWayId = 12903132;
        private static readonly HashSet<long> SpaceNeedlePartWayIds = new HashSet<long> {
            394254329, 394254331, 394254332, 394254333, 394254334,
            394254335, 394254336, 394254337,
            394254484, 394254485, 394254486, 394254487, 394254488,
            394956342, 394956343, 394956344
        };
        private static readonly HashSet<long> ClimatePledgeArenaPartWayIds = new HashSet<long> {
            411521517, 411521531, 1270549342,
            1352647807, 1352647808, 1352647809, 1352647810, 1352647811, 1352647812,
            1352665595, 1352665596, 1352665597, 1352665598,
            1352666108, 1352666109, 1352666110, 1352666111,
            1352666989, 1352666990, 1352666991,
            1354412677
        };

        private bool TryDrawIconicBuilding(OsmReader.Building building,
                Dictionary<BlockPosition, int> buildingPoints,
                HashSet<BlockPosition> roofPoints) {
            // These building:part ways describe the arena's real roof and annexes.
            // The generic renderer treats each one as an independent rectangular
            // building, which buries the custom arena under a large box.
            if (ClimatePledgeArenaPartWayIds.Contains(building.Id) ||
                    SpaceNeedlePartWayIds.Contains(building.Id)) return true;

            switch (building.Id) {
                case ClimatePledgeArenaWayId:
                    DrawClimatePledgeArena(building, buildingPoints, roofPoints);
                    return true;
                case SpaceNeedleWayId:
                    DrawSpaceNeedle(building, buildingPoints, roofPoints);
                    return true;
                default:
                    return false;
            }
        }

        private void DrawSpaceNeedle(OsmReader.Building building,
                Dictionary<BlockPosition, int> buildingPoints,
                HashSet<BlockPosition> roofPoints) {
            List<BlockPosition> footprint = GetIconicFootprint(building);
            if (footprint.Count < 3) return;

            int centerX = (int)Math.Round(footprint.Average(point => point.X));
            int centerZ = (int)Math.Round(footprint.Average(point => point.Z));
            int radiusX = Math.Max(8, (footprint.Max(point => point.X) - footprint.Min(point => point.X)) / 2);
            int radiusZ = Math.Max(8, (footprint.Max(point => point.Z) - footprint.Min(point => point.Z)) / 2);
            int baseY = GetIconicBaseHeight(footprint, 4);
            Console.WriteLine($"Rendering the Space Needle at {centerX},{baseY},{centerZ}");

            // An iconic building may be rendered over a world that already contains
            // the generic version.  Remove that version before measuring/drawing the
            // recognizable silhouette; otherwise repeated runs leave a solid tower.
            ClearIconicEllipse(centerX, centerZ, radiusX + 2, radiusZ + 2, baseY + 1, 255);

            // Preserve the real 184 m proportion when the world ceiling permits it,
            // and scale the landmark down as a unit if terrain is ever raised.
            int totalHeight = Math.Max(80, Math.Min(184, 250 - baseY));
            int legJoinY = baseY + (int)Math.Round(totalHeight * 0.63);
            int saucerY = baseY + (int)Math.Round(totalHeight * 0.78);
            int antennaTopY = baseY + totalHeight;

            // Three slender, splayed legs are the dominant ground-level silhouette.
            for (int leg = 0; leg < 3; leg++) {
                double angle = -Math.PI / 2.0 + leg * Math.PI * 2.0 / 3.0;
                int footX = centerX + (int)Math.Round(Math.Cos(angle) * radiusX * 0.72);
                int footZ = centerZ + (int)Math.Round(Math.Sin(angle) * radiusZ * 0.72);
                DrawIconicBeam(footX, baseY + 1, footZ, centerX, legJoinY, centerZ,
                    1, AcquaticBlocks.SmoothQuartz, buildingPoints);
            }

            DrawIconicCylinder(centerX, centerZ, baseY + 3, saucerY - 3,
                1, AcquaticBlocks.LightBlueStainedGlass, buildingPoints);

            int saucerRadiusX = Math.Max(10, (int)Math.Round(radiusX * 0.88));
            int saucerRadiusZ = Math.Max(10, (int)Math.Round(radiusZ * 0.88));
            DrawIconicEllipsoidLayer(centerX, centerZ, saucerY - 4,
                Math.Max(3, saucerRadiusX - 8), Math.Max(3, saucerRadiusZ - 8),
                AcquaticBlocks.SmoothQuartz, buildingPoints, roofPoints);
            DrawIconicEllipsoidLayer(centerX, centerZ, saucerY - 3,
                Math.Max(5, saucerRadiusX - 5), Math.Max(5, saucerRadiusZ - 5),
                AcquaticBlocks.SmoothQuartz, buildingPoints, roofPoints);
            DrawIconicEllipsoidLayer(centerX, centerZ, saucerY - 2,
                Math.Max(7, saucerRadiusX - 2), Math.Max(7, saucerRadiusZ - 2),
                AcquaticBlocks.SmoothQuartz, buildingPoints, roofPoints);
            DrawIconicEllipsoidLayer(centerX, centerZ, saucerY - 1,
                saucerRadiusX, saucerRadiusZ,
                AcquaticBlocks.SmoothQuartz, buildingPoints, roofPoints);
            for (int y = saucerY; y <= saucerY + 2; y++) {
                DrawIconicEllipseRing(centerX, centerZ, y,
                    saucerRadiusX, saucerRadiusZ,
                    AcquaticBlocks.CyanStainedGlass,
                    buildingPoints, roofPoints);
            }
            DrawIconicEllipsoidLayer(centerX, centerZ, saucerY + 3,
                saucerRadiusX, saucerRadiusZ,
                AcquaticBlocks.SmoothQuartz, buildingPoints, roofPoints);
            DrawIconicEllipsoidLayer(centerX, centerZ, saucerY + 4,
                saucerRadiusX - 4, saucerRadiusZ - 4,
                AcquaticBlocks.SmoothQuartz, buildingPoints, roofPoints);

            // A shallow mechanical cap sits directly on the saucer.  The former
            // tall glass cylinder here made the top look like a second tower.
            DrawIconicEllipsoidLayer(centerX, centerZ, saucerY + 5,
                Math.Max(5, saucerRadiusX / 2), Math.Max(5, saucerRadiusZ / 2),
                AcquaticBlocks.SmoothQuartz, buildingPoints, roofPoints);
            DrawIconicEllipsoidLayer(centerX, centerZ, saucerY + 6,
                Math.Max(3, saucerRadiusX / 3), Math.Max(3, saucerRadiusZ / 3),
                AcquaticBlocks.LightGrayConcrete, buildingPoints, roofPoints);
            DrawIconicCylinder(centerX, centerZ, saucerY + 7, saucerY + 9,
                1, AcquaticBlocks.LightGrayConcrete, buildingPoints);
            DrawIconicCylinder(centerX, centerZ, saucerY + 10, antennaTopY,
                0, AcquaticBlocks.IronBlock, buildingPoints);
            for (int y = saucerY + 14; y <= antennaTopY; y += 8) {
                _bm.SetBlock(centerX, y, centerZ, AcquaticBlocks.SeaLantern);
            }
        }

        private void DrawClimatePledgeArena(OsmReader.Building building,
                Dictionary<BlockPosition, int> buildingPoints,
                HashSet<BlockPosition> roofPoints) {
            List<BlockPosition> polygon = GetIconicFootprint(building);
            if (polygon.Count < 3) return;

            int left = polygon.Min(point => point.X);
            int right = polygon.Max(point => point.X);
            int top = polygon.Min(point => point.Z);
            int bottom = polygon.Max(point => point.Z);
            int centerX = (left + right) / 2;
            int centerZ = (top + bottom) / 2;
            // Sample well beyond the arena and its component ways.  A close sample
            // on a previously rendered world can mistake an old generic wall for
            // terrain and stack the replacement arena on top of it.
            int baseY = GetIconicBaseHeight(polygon, 15);
            Console.WriteLine($"Rendering Climate Pledge Arena at {centerX},{baseY},{centerZ}");
            ClearIconicPolygon(polygon, baseY + 1, 255);
            foreach (long partId in ClimatePledgeArenaPartWayIds) {
                OsmReader.Building part;
                if (_reader.Buildings.TryGetValue(partId, out part)) {
                    List<BlockPosition> partPolygon = GetIconicFootprint(part);
                    if (partPolygon.Count >= 3)
                        ClearIconicPolygon(partPolygon, baseY + 1, 255);
                }
            }
            int availableHeight = Math.Max(12, Math.Min(35, 250 - baseY));
            int wallHeight = Math.Max(7, availableHeight / 3);
            int roofRise = Math.Max(6, availableHeight - wallHeight);

            for (int z = top; z <= bottom; z++) {
                for (int x = left; x <= right; x++) {
                    if (!_conv.IsValidPoint(new BlockPosition(x, z)) ||
                            !IsInsidePolygon(x + 0.5, z + 0.5, polygon)) continue;

                    bool edge = !IsInsidePolygon(x - 0.5, z + 0.5, polygon) ||
                        !IsInsidePolygon(x + 1.5, z + 0.5, polygon) ||
                        !IsInsidePolygon(x + 0.5, z - 0.5, polygon) ||
                        !IsInsidePolygon(x + 0.5, z + 1.5, polygon);
                    var point = new BlockPosition(x, z);
                    buildingPoints[point] = baseY;

                    double nx = (x + 0.5 - centerX) / Math.Max(1.0, (right - left) / 2.0);
                    double nz = (z + 0.5 - centerZ) / Math.Max(1.0, (bottom - top) / 2.0);
                    // The Coliseum roof is a hyperbolic paraboloid, not a dome.
                    // Its opposing high and low edges give the arena its signature
                    // sweeping profile.
                    double saddle = Math.Max(-1.0, Math.Min(1.0, nx * nx - nz * nz));
                    int roofY = baseY + wallHeight + roofRise / 2 +
                        (int)Math.Round(saddle * roofRise * 0.48);
                    roofY = Math.Max(baseY + wallHeight + 1,
                        Math.Min(baseY + wallHeight + roofRise, roofY));

                    if (edge) {
                        for (int y = baseY + 1; y < roofY; y++) {
                            string wall = ((x + z + y) & 7) == 0
                                ? AcquaticBlocks.SeaLantern
                                : AcquaticBlocks.CyanStainedGlass;
                            _bm.SetBlock(x, y, z, wall);
                        }
                    }

                    string roofBlock = ((x - left) / 6) % 5 == 1
                        ? AcquaticBlocks.BlackConcrete
                        : AcquaticBlocks.SmoothQuartz;
                    _bm.SetBlock(x, roofY - 1, z, AcquaticBlocks.LightGrayConcrete);
                    _bm.SetBlock(x, roofY, z, roofBlock);
                    roofPoints.Add(point);
                }
            }

            // The four historic roof supports make the broad roof silhouette
            // legible from ground level.
            int insetX = Math.Max(5, (right - left) / 7);
            int insetZ = Math.Max(5, (bottom - top) / 7);
            foreach (var support in new[] {
                    new BlockPosition(left + insetX, top + insetZ),
                    new BlockPosition(right - insetX, top + insetZ),
                    new BlockPosition(left + insetX, bottom - insetZ),
                    new BlockPosition(right - insetX, bottom - insetZ) }) {
                DrawIconicCylinder(support.X, support.Z, baseY + 1,
                    GetArenaRoofHeight(support.X, support.Z, centerX, centerZ,
                        left, right, top, bottom, baseY, wallHeight, roofRise), 2,
                    AcquaticBlocks.LightGrayConcrete, buildingPoints);
            }
        }

        private List<BlockPosition> GetIconicFootprint(OsmReader.Building building) {
            var points = building.BuildingNodes
                .Select(node => _conv.ToBlock(node.Lat, node.Long))
                .Where(point => _conv.IsValidPoint(point))
                .ToList();
            if (points.Count > 1 && points[0].Equals(points[points.Count - 1]))
                points.RemoveAt(points.Count - 1);
            return points;
        }

        private int GetIconicBaseHeight(IList<BlockPosition> footprint, int padding) {
            int left = footprint.Min(point => point.X) - padding;
            int right = footprint.Max(point => point.X) + padding;
            int top = footprint.Min(point => point.Z) - padding;
            int bottom = footprint.Max(point => point.Z) + padding;
            var heights = new List<int>();
            for (int x = left; x <= right; x++) {
                AddIconicTerrainHeight(heights, x, top);
                AddIconicTerrainHeight(heights, x, bottom);
            }
            for (int z = top + 1; z < bottom; z++) {
                AddIconicTerrainHeight(heights, left, z);
                AddIconicTerrainHeight(heights, right, z);
            }
            if (heights.Count == 0) return 0;
            heights.Sort();
            return heights[(int)Math.Floor((heights.Count - 1) * 0.75)];
        }

        private void AddIconicTerrainHeight(List<int> heights, int x, int z) {
            var point = new BlockPosition(x, z);
            if (_conv.IsValidPoint(point)) heights.Add(GetTerrainHeight(point));
        }

        private int GetArenaRoofHeight(int x, int z, int centerX, int centerZ,
                int left, int right, int top, int bottom, int baseY,
                int wallHeight, int roofRise) {
            double nx = (x + 0.5 - centerX) / Math.Max(1.0, (right - left) / 2.0);
            double nz = (z + 0.5 - centerZ) / Math.Max(1.0, (bottom - top) / 2.0);
            double saddle = Math.Max(-1.0, Math.Min(1.0, nx * nx - nz * nz));
            return Math.Max(baseY + wallHeight + 1,
                Math.Min(baseY + wallHeight + roofRise,
                    baseY + wallHeight + roofRise / 2 +
                    (int)Math.Round(saddle * roofRise * 0.48)));
        }

        private void ClearIconicPolygon(IList<BlockPosition> polygon, int bottomY, int topY) {
            for (int z = polygon.Min(point => point.Z); z <= polygon.Max(point => point.Z); z++)
                for (int x = polygon.Min(point => point.X); x <= polygon.Max(point => point.X); x++)
                    if (IsInsidePolygon(x + 0.5, z + 0.5, polygon))
                        ClearIconicColumn(x, z, bottomY, topY);
        }

        private void ClearIconicEllipse(int centerX, int centerZ, int radiusX, int radiusZ,
                int bottomY, int topY) {
            for (int z = centerZ - radiusZ; z <= centerZ + radiusZ; z++)
                for (int x = centerX - radiusX; x <= centerX + radiusX; x++) {
                    double distance = Math.Pow((double)(x - centerX) / radiusX, 2) +
                        Math.Pow((double)(z - centerZ) / radiusZ, 2);
                    if (distance <= 1.0) ClearIconicColumn(x, z, bottomY, topY);
                }
        }

        private void ClearIconicColumn(int x, int z, int bottomY, int topY) {
            var point = new BlockPosition(x, z);
            if (!_conv.IsValidPoint(point)) return;
            for (int y = Math.Max(0, bottomY); y <= Math.Min(255, topY); y++)
                _bm.SetBlock(x, y, z, AcquaticBlocks.Air);
        }

        private void DrawIconicBeam(int x0, int y0, int z0, int x1, int y1, int z1,
                int radius, string blockName, Dictionary<BlockPosition, int> buildingPoints) {
            int steps = Math.Max(Math.Abs(x1 - x0), Math.Max(Math.Abs(y1 - y0), Math.Abs(z1 - z0)));
            for (int step = 0; step <= steps; step++) {
                double t = steps == 0 ? 0 : (double)step / steps;
                int x = (int)Math.Round(x0 + (x1 - x0) * t);
                int y = (int)Math.Round(y0 + (y1 - y0) * t);
                int z = (int)Math.Round(z0 + (z1 - z0) * t);
                for (int dz = -radius; dz <= radius; dz++)
                    for (int dx = -radius; dx <= radius; dx++)
                        if (dx * dx + dz * dz <= radius * radius)
                            SetIconicBlock(x + dx, y, z + dz, blockName, buildingPoints);
            }
        }

        private void DrawIconicCylinder(int centerX, int centerZ, int bottomY, int topY,
                int radius, string blockName, Dictionary<BlockPosition, int> buildingPoints) {
            for (int y = bottomY; y <= topY; y++)
                for (int z = centerZ - radius; z <= centerZ + radius; z++)
                    for (int x = centerX - radius; x <= centerX + radius; x++)
                        if ((x - centerX) * (x - centerX) + (z - centerZ) * (z - centerZ) <= radius * radius)
                            SetIconicBlock(x, y, z, blockName, buildingPoints);
        }

        private void DrawIconicEllipsoidLayer(int centerX, int centerZ, int y,
                int radiusX, int radiusZ, string blockName,
                Dictionary<BlockPosition, int> buildingPoints, HashSet<BlockPosition> roofPoints) {
            for (int z = centerZ - radiusZ; z <= centerZ + radiusZ; z++)
                for (int x = centerX - radiusX; x <= centerX + radiusX; x++) {
                    double distance = Math.Pow((double)(x - centerX) / radiusX, 2) +
                        Math.Pow((double)(z - centerZ) / radiusZ, 2);
                    if (distance <= 1.0) {
                        SetIconicBlock(x, y, z, blockName, buildingPoints);
                        roofPoints.Add(new BlockPosition(x, z));
                    }
                }
        }

        private void DrawIconicEllipseRing(int centerX, int centerZ, int y,
                int radiusX, int radiusZ, string blockName,
                Dictionary<BlockPosition, int> buildingPoints, HashSet<BlockPosition> roofPoints) {
            for (int z = centerZ - radiusZ; z <= centerZ + radiusZ; z++)
                for (int x = centerX - radiusX; x <= centerX + radiusX; x++) {
                    double outer = Math.Pow((double)(x - centerX) / radiusX, 2) +
                        Math.Pow((double)(z - centerZ) / radiusZ, 2);
                    double inner = Math.Pow((double)(x - centerX) / Math.Max(1, radiusX - 2), 2) +
                        Math.Pow((double)(z - centerZ) / Math.Max(1, radiusZ - 2), 2);
                    if (outer <= 1.0 && inner >= 1.0) {
                        SetIconicBlock(x, y, z, blockName, buildingPoints);
                        roofPoints.Add(new BlockPosition(x, z));
                    }
                }
        }

        private void SetIconicBlock(int x, int y, int z, string blockName,
                Dictionary<BlockPosition, int> buildingPoints) {
            var point = new BlockPosition(x, z);
            if (!_conv.IsValidPoint(point) || y < 0 || y > 250) return;
            _bm.SetBlock(x, y, z, blockName);
            if (!buildingPoints.ContainsKey(point))
                buildingPoints[point] = GetTerrainHeight(point);
        }
    }
}
