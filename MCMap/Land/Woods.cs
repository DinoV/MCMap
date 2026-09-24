using Substrate;
using System;
using System.Collections.Generic;
using System.Linq;

namespace MinecraftMapper {
    partial class Mapper {
        private const int WoodlandSpacing = 8;
        private const int WoodlandCanopyRadius = 2;

        private void DrawWoodlands(
                Dictionary<BlockPosition, RoadPoint> roadPoints,
                Dictionary<BlockPosition, int> buildingPoints) {
            var occupied = new HashSet<BlockPosition>();
            foreach (OsmReader.LandInfo woodland in _reader.Woodlands) {
                DrawWoodland(woodland, roadPoints, buildingPoints, occupied);
            }
        }

        private void DrawWoodland(
                OsmReader.LandInfo woodland,
                Dictionary<BlockPosition, RoadPoint> roadPoints,
                Dictionary<BlockPosition, int> buildingPoints,
                HashSet<BlockPosition> occupied) {
            BlockPosition[] polygon = woodland.Nodes
                .Select(node => _conv.ToBlock(node.Lat, node.Long))
                .ToArray();
            if (polygon.Length < 3) return;

            int left = polygon.Min(point => point.X);
            int right = polygon.Max(point => point.X);
            int top = polygon.Min(point => point.Z);
            int bottom = polygon.Max(point => point.Z);
            int rendered = 0;
            int saplings = 0;

            // A stable grid with per-cell jitter gives an organic distribution while
            // ensuring repeated runs produce the same woodland for a given OSM way.
            for (int cellZ = top; cellZ <= bottom; cellZ += WoodlandSpacing) {
                for (int cellX = left; cellX <= right; cellX += WoodlandSpacing) {
                    int seed = WoodlandSeed(woodland.WayId, cellX, cellZ);
                    int x = cellX + PositiveModulo(seed, 5) - 2;
                    int z = cellZ + PositiveModulo(seed >> 5, 5) - 2;
                    var point = new BlockPosition(x, z);
                    if (!CanPlantWoodlandTree(
                            point, polygon, roadPoints, buildingPoints, occupied)) {
                        continue;
                    }

                    int groundY = GetTerrainHeight(point);
                    if (groundY <= 0 || groundY >= 304 || IsWaterBlock(point, groundY)) {
                        continue;
                    }

                    ClearNonSolidBlocks(point, groundY + 1, groundY + 3);
                    if (PositiveModulo(seed >> 10, 6) == 0 &&
                            CanSupportSapling(point, groundY)) {
                        _bm.SetBlock(point.X, groundY + 1, point.Z,
                            WoodlandSapling(seed), stage: 0);
                        saplings++;
                    } else {
                        DrawWoodlandTree(point, groundY, seed);
                        rendered++;
                    }

                    for (int dx = -WoodlandCanopyRadius; dx <= WoodlandCanopyRadius; dx++) {
                        for (int dz = -WoodlandCanopyRadius; dz <= WoodlandCanopyRadius; dz++) {
                            occupied.Add(new BlockPosition(point.X + dx, point.Z + dz));
                        }
                    }
                }
            }

            WriteLine("Rendered woodland {0}: {1} trees and {2} saplings.",
                woodland.WayId, rendered, saplings);
        }

        private bool CanPlantWoodlandTree(
                BlockPosition point,
                IList<BlockPosition> polygon,
                Dictionary<BlockPosition, RoadPoint> roadPoints,
                Dictionary<BlockPosition, int> buildingPoints,
                HashSet<BlockPosition> occupied) {
            for (int dx = -WoodlandCanopyRadius; dx <= WoodlandCanopyRadius; dx++) {
                for (int dz = -WoodlandCanopyRadius; dz <= WoodlandCanopyRadius; dz++) {
                    var canopyPoint = new BlockPosition(point.X + dx, point.Z + dz);
                    if (!_conv.IsValidPoint(canopyPoint) ||
                            !IsInsidePolygon(canopyPoint.X + 0.5, canopyPoint.Z + 0.5, polygon) ||
                            roadPoints.ContainsKey(canopyPoint) ||
                            buildingPoints.ContainsKey(canopyPoint) ||
                            occupied.Contains(canopyPoint)) {
                        return false;
                    }
                }
            }
            return true;
        }

        private void DrawWoodlandTree(BlockPosition point, int groundY, int seed) {
            int trunkHeight = 4 + PositiveModulo(seed >> 14, 3);
            string log;
            string leaves;
            switch (PositiveModulo(seed >> 18, 3)) {
                case 0:
                    log = AcquaticBlocks.SpruceLog;
                    leaves = AcquaticBlocks.SpruceLeaves;
                    break;
                case 1:
                    log = AcquaticBlocks.BirchLog;
                    leaves = AcquaticBlocks.BirchLeaves;
                    break;
                default:
                    log = AcquaticBlocks.OakLog;
                    leaves = AcquaticBlocks.OakLeaves;
                    break;
            }

            for (int y = groundY + 1; y <= groundY + trunkHeight; y++) {
                _bm.SetBlock(point.X, y, point.Z, log, axis: BlockAxis.Y);
            }

            int canopyBase = groundY + trunkHeight - 2;
            for (int layer = 0; layer < 4; layer++) {
                int radius = layer < 2 ? 2 : 1;
                int y = canopyBase + layer;
                for (int dx = -radius; dx <= radius; dx++) {
                    for (int dz = -radius; dz <= radius; dz++) {
                        if (dx == 0 && dz == 0 && y <= groundY + trunkHeight) continue;
                        if (radius == 2 && Math.Abs(dx) == 2 && Math.Abs(dz) == 2 &&
                                PositiveModulo(seed + dx * 31 + dz * 17 + layer * 13, 3) == 0) {
                            continue;
                        }
                        int leafX = point.X + dx;
                        int leafZ = point.Z + dz;
                        if (_bm.GetStringID(leafX, y, leafZ) == AcquaticBlocks.Air) {
                            _bm.SetBlock(leafX, y, leafZ, leaves,
                                persistent: true,
                                distance: Math.Max(1, Math.Abs(dx) + Math.Abs(dz)));
                        }
                    }
                }
            }
        }

        private bool IsWaterBlock(BlockPosition point, int y) {
            int id = _bm.GetID(point.X, y, point.Z);
            return id == BlockType.WATER || id == BlockType.STATIONARY_WATER;
        }

        private bool CanSupportSapling(BlockPosition point, int y) {
            string block = _bm.GetStringID(point.X, y, point.Z);
            return block == AcquaticBlocks.GrassBlock ||
                block == AcquaticBlocks.Dirt ||
                block == AcquaticBlocks.CoarseDirt ||
                block == AcquaticBlocks.Podzol ||
                block == AcquaticBlocks.MossBlock;
        }

        private static string WoodlandSapling(int seed) {
            switch (PositiveModulo(seed >> 18, 3)) {
                case 0: return AcquaticBlocks.SpruceSapling;
                case 1: return AcquaticBlocks.BirchSapling;
                default: return AcquaticBlocks.OakSapling;
            }
        }

        private static int WoodlandSeed(long wayId, int x, int z) {
            long value = unchecked(wayId ^ ((long)x * 73856093L) ^ ((long)z * 19349663L));
            value ^= value >> 33;
            value *= unchecked((long)0xff51afd7ed558ccdUL);
            value ^= value >> 33;
            return unchecked((int)(value ^ (value >> 32)));
        }

    }
}
