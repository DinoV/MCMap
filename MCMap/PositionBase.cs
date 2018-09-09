using System;

namespace MinecraftMapper {
    /// <summary>
    /// Provides a mapping between lat/long to a position on the minecraft map.
    /// </summary>
    class PositionBase {
        public readonly double baseZ;
        public readonly double baseX;
        public readonly int mcX;
        public readonly int mcZ;

        public static double Lat2YMeters(double lat) { return Math.Log(Math.Tan(DegreeToRadian(lat) / 2 + Math.PI / 4)) * EarthRadius; }
        public static double Long2XMeters(double lon) { return DegreeToRadian(lon) * EarthRadius; }
        const int EarthRadius = 6378137;

        private static double DegreeToRadian(double angle) {
            return Math.PI * angle / 180.0;
        }

        public static BlockPosition Degree2Position(double lat_deg, double lon_deg, double zoom) {
            // https://wiki.openstreetmap.org/wiki/Slippy_map_tilenames#Lon..2Flat._to_tile_numbers_2
            var lat_rad = DegreeToRadian(lat_deg);
            var n = Math.Pow(2.0, zoom);
            var xtile = ((lon_deg + 180.0) / 360.0 * n);
            var ytile = (1.0 - Math.Log(Math.Tan(lat_rad) + (1 / Math.Cos(lat_rad))) / Math.PI) / 2.0 * n;
            return new BlockPosition((int)xtile, (int)ytile);
        }

        public PositionBase(double baseZ, double baseX, int mcX, int mcZ) {
            this.baseZ = Lat2YMeters(baseZ);
            this.baseX = Long2XMeters(baseX);
            this.mcX = mcX;
            this.mcZ = mcZ;
        }
    }
}
