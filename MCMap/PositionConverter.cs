using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MinecraftMapper {

    /// <summary>
    /// Position converter for EPSG:6597. We expect the Minecraft map to be updated
    /// to have 1 meter resolution.
    /// </summary>
    sealed class PositionConverter {
        // Upper-left CORNER of the original raster in EPSG:6597.
        private double RasterUpperLeftEastingUsFt;
        private double RasterUpperLeftNorthingUsFt;

        /*
         * One meter expressed in U.S. survey feet.
         *
         * 1 US survey foot = 1200 / 3937 meters
         * 1 meter = 3937 / 1200 US survey feet
         */
        private const double UsSurveyFeetPerMeter = 3937.0 / 1200.0;

        // The output raster was generated with one-meter pixels.
        private const double PixelWidthUsFt = UsSurveyFeetPerMeter;
        private const double PixelHeightUsFt = UsSurveyFeetPerMeter;

        private int CropLeftPixels;
        private int CropTopPixels;

        /*
         * These are derived from the original raster origin and crop.
         *
         * They represent the upper-left CORNER of the cropped image.
         */
        private double CroppedUpperLeftEastingUsFt;
        private double CroppedUpperLeftNorthingUsFt;

        private readonly int _maxX;
        private readonly int _maxZ;

        /// <summary>
        /// Creates the position converter for the EPSG:6597 coordinates.
        /// </summary>
        /// <param name="maxX">The maximum X coordinate of the Minecraft map</param>
        /// <param name="maxZ">The maximum Z coordinate of the Minecraft map</param>
        /// <param name="upperLeftEastingUsFt">The upper left corner of the original image</param>
        /// <param name="upperLeftNorthingUsFt">The upper left corner of the original image</param>
        /// <param name="cropLeftPixels">The number of left pixels cropped from the original image</param>
        /// <param name="cropTopPixels">The number of upper pixels cropped from the original image</param>
        public PositionConverter(int maxX, int maxZ, double upperLeftEastingUsFt = 1_264_500.0, double upperLeftNorthingUsFt = 198_000.0, int cropLeftPixels = 8, int cropTopPixels = 547) {
            _maxX = maxX;
            _maxZ = maxZ;

            RasterUpperLeftEastingUsFt = upperLeftEastingUsFt;
            RasterUpperLeftNorthingUsFt = upperLeftNorthingUsFt;
            CropLeftPixels = cropLeftPixels;
            CropTopPixels = cropTopPixels;
            CroppedUpperLeftEastingUsFt = RasterUpperLeftEastingUsFt +
                CropLeftPixels * PixelWidthUsFt;

            CroppedUpperLeftNorthingUsFt = RasterUpperLeftNorthingUsFt -
                CropTopPixels * PixelHeightUsFt;
        }

        public static PositionConverter FromProjectedRasterOrigin(int maxX, int maxZ, double upperLeftEastingUsFt, double upperLeftNorthingUsFt, int offsetX, int offsetZ) {
            return new PositionConverter(
                maxX, maxZ,
                upperLeftEastingUsFt, upperLeftNorthingUsFt,
                offsetX, offsetZ
            );
        }

        public static PositionConverter FromLatLong(int maxX, int maxZ, double lat, double lng, int offsetX, int offsetZ) {
            var projected =
                ProjectToWashingtonNorth(lat, lng);

            return new PositionConverter(
                maxX, maxZ,
                projected.EastingUsFt, projected.NorthingUsFt,
                offsetX,
                offsetZ
            );
        }

        /// <summary>
        /// Creates a converter whose origin matches the north-up projected
        /// envelope produced by GDAL for a geographic crop window.
        /// </summary>
        public static PositionConverter FromLatLongBounds(
                int maxX, int maxZ, double west, double north,
                double east, double south, int offsetX, int offsetZ) {
            if (west >= east) {
                throw new ArgumentException("west must be less than east.");
            }
            if (south >= north) {
                throw new ArgumentException("south must be less than north.");
            }

            var northWest = ProjectToWashingtonNorth(north, west);
            var northEast = ProjectToWashingtonNorth(north, east);
            var southWest = ProjectToWashingtonNorth(south, west);
            var southEast = ProjectToWashingtonNorth(south, east);

            // gdal.Translate(projWin=..., projWinSRS=EPSG:4326) creates a
            // north-up window from the projected envelope. In a Lambert
            // projection the geographic sides are not parallel to the
            // projected axes, so the origin is not generally the direct
            // projection of the northwest corner.
            double upperLeftEastingUsFt = Math.Min(
                Math.Min(northWest.EastingUsFt, northEast.EastingUsFt),
                Math.Min(southWest.EastingUsFt, southEast.EastingUsFt));
            double upperLeftNorthingUsFt = Math.Max(
                Math.Max(northWest.NorthingUsFt, northEast.NorthingUsFt),
                Math.Max(southWest.NorthingUsFt, southEast.NorthingUsFt));

            return FromProjectedRasterOrigin(maxX, maxZ, upperLeftEastingUsFt, upperLeftNorthingUsFt, offsetX, offsetZ);
        }

        /*
         * ================================================================
         * EPSG:6597 projection constants
         * ================================================================
         *
         * NAD83(2011) / Washington North (ftUS)
         * Lambert Conformal Conic, two standard parallels.
         *
         * The projection calculation is performed in meters and converted
         * to U.S. survey feet at the end.
         */

        // GRS 1980 ellipsoid.
        private const double SemiMajorAxisMeters = 6_378_137.0;
        private const double InverseFlattening = 298.257222101;

        private const double LatitudeOfFalseOriginDegrees =
            47.0;

        private const double LongitudeOfFalseOriginDegrees =
            -120.833333333333;

        private const double FirstStandardParallelDegrees =
            48.7333333333333;

        private const double SecondStandardParallelDegrees =
            47.5;

        private const double FalseEastingMeters =
            500_000.0001016;

        private const double FalseNorthingMeters =
            0.0;

        private const double DegreesToRadians =
            Math.PI / 180.0;

        /*
         * ================================================================
         * Precomputed Lambert Conformal Conic values
         * ================================================================
         */

        private static readonly double Flattening =
            1.0 / InverseFlattening;

        private static readonly double EccentricitySquared =
            Flattening * (2.0 - Flattening);

        private static readonly double Eccentricity =
            Math.Sqrt(EccentricitySquared);

        private static readonly double LatitudeOfFalseOriginRadians =
            ToRadians(LatitudeOfFalseOriginDegrees);

        private static readonly double LongitudeOfFalseOriginRadians =
            ToRadians(LongitudeOfFalseOriginDegrees);

        private static readonly double FirstStandardParallelRadians =
            ToRadians(FirstStandardParallelDegrees);

        private static readonly double SecondStandardParallelRadians =
            ToRadians(SecondStandardParallelDegrees);

        private static readonly double M1 =
            CalculateM(FirstStandardParallelRadians);

        private static readonly double M2 =
            CalculateM(SecondStandardParallelRadians);

        private static readonly double T1 =
            CalculateT(FirstStandardParallelRadians);

        private static readonly double T2 =
            CalculateT(SecondStandardParallelRadians);

        private static readonly double T0 =
            CalculateT(LatitudeOfFalseOriginRadians);

        private static readonly double ConeConstant =
            (Math.Log(M1) - Math.Log(M2)) /
            (Math.Log(T1) - Math.Log(T2));

        private static readonly double ProjectionConstant =
            M1 /
            (ConeConstant * Math.Pow(T1, ConeConstant));

        private static readonly double RadiusAtFalseOriginMeters =
            SemiMajorAxisMeters *
            ProjectionConstant *
            Math.Pow(T0, ConeConstant);

        /// <summary>
        /// Converts WGS84 latitude/longitude into the Minecraft X/Z block
        /// occupied by that location in the cropped heightmap.
        ///
        /// The returned coordinates may be outside the map. Use
        /// IsInsideMap() when that distinction matters.
        /// </summary>
        public BlockPosition ToBlock(
            double latitude,
            double longitude) {
            ValidateCoordinate(latitude, longitude);

            var pixel =
                ToCroppedImagePixelDouble(latitude, longitude);

            /*
             * GDAL's geotransform origin is the upper-left corner of the
             * upper-left pixel. Therefore floor() selects the raster cell
             * containing the geographic point.
             */

            return new BlockPosition(checked((int)Math.Floor(pixel.X)), checked((int)Math.Floor(pixel.Z)));
        }

        public bool IsValidPoint(BlockPosition point) {
            return point.X >= 0 && point.Z >= 0 && point.X < _maxX && point.Z < _maxZ;
        }

        /// <summary>
        /// Returns the continuous pixel coordinate within the cropped
        /// 4096 × 1792 image.
        ///
        /// Pixel (0,0) begins at the image's upper-left corner.
        /// The center of that pixel is at (0.5,0.5).
        /// </summary>
        public (double X, double Z) ToCroppedImagePixelDouble(
            double latitude,
            double longitude) {
            ValidateCoordinate(latitude, longitude);

            var projected =
                ProjectToWashingtonNorth(latitude, longitude);

            double imageX =
                (projected.EastingUsFt -
                 CroppedUpperLeftEastingUsFt) /
                PixelWidthUsFt;

            double imageZ =
                (CroppedUpperLeftNorthingUsFt -
                 projected.NorthingUsFt) /
                PixelHeightUsFt;

            return (imageX, imageZ);
        }

        /// <summary>
        /// Projects WGS84-style longitude/latitude into
        /// EPSG:6597 easting/northing in U.S. survey feet.
        /// </summary>
        private static (
            double EastingUsFt,
            double NorthingUsFt)
            ProjectToWashingtonNorth(
                double latitude,
                double longitude) {
            double latitudeRadians =
                ToRadians(latitude);

            double longitudeRadians =
                ToRadians(longitude);

            double t =
                CalculateT(latitudeRadians);

            double radiusMeters =
                SemiMajorAxisMeters *
                ProjectionConstant *
                Math.Pow(t, ConeConstant);

            double theta =
                ConeConstant *
                (
                    longitudeRadians -
                    LongitudeOfFalseOriginRadians
                );

            double eastingMeters =
                FalseEastingMeters +
                radiusMeters * Math.Sin(theta);

            double northingMeters =
                FalseNorthingMeters +
                RadiusAtFalseOriginMeters -
                radiusMeters * Math.Cos(theta);

            return (
                eastingMeters * UsSurveyFeetPerMeter,
                northingMeters * UsSurveyFeetPerMeter
            );
        }

        /*
         * Ellipsoidal Lambert Conformal Conic helper:
         *
         * m(phi) = cos(phi) /
         *          sqrt(1 - e² sin²(phi))
         */
        private static double CalculateM(
            double latitudeRadians) {
            double sinLatitude =
                Math.Sin(latitudeRadians);

            return
                Math.Cos(latitudeRadians) /
                Math.Sqrt(
                    1.0 -
                    EccentricitySquared *
                    sinLatitude *
                    sinLatitude);
        }

        /*
         * Ellipsoidal Lambert Conformal Conic helper:
         *
         * t(phi) =
         *   tan(pi/4 - phi/2) /
         *   ((1 - e sin(phi)) /
         *    (1 + e sin(phi)))^(e/2)
         */
        private static double CalculateT(
            double latitudeRadians) {
            double sinLatitude =
                Math.Sin(latitudeRadians);

            double eccentricityTimesSinLatitude =
                Eccentricity * sinLatitude;

            double eccentricityRatio =
                (1.0 - eccentricityTimesSinLatitude) /
                (1.0 + eccentricityTimesSinLatitude);

            return
                Math.Tan(
                    Math.PI / 4.0 -
                    latitudeRadians / 2.0) /
                Math.Pow(
                    eccentricityRatio,
                    Eccentricity / 2.0);
        }

        private static double ToRadians(
            double degrees) {
            return degrees * DegreesToRadians;
        }

        private static void ValidateCoordinate(
            double latitude,
            double longitude) {
            if (double.IsNaN(latitude) ||
                double.IsInfinity(latitude) ||
                latitude <= -90.0 ||
                latitude >= 90.0) {
                throw new ArgumentOutOfRangeException(
                    nameof(latitude),
                    latitude,
                    "Latitude must be finite and between -90 and 90.");
            }

            if (double.IsNaN(longitude) ||
                double.IsInfinity(longitude) ||
                longitude < -180.0 ||
                longitude > 180.0) {
                throw new ArgumentOutOfRangeException(
                    nameof(longitude),
                    longitude,
                    "Longitude must be finite and between -180 and 180.");
            }
        }
    }

}
