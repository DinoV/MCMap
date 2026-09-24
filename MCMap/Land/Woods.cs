using Substrate;
using System;
using System.Collections.Generic;
using System.Linq;

namespace MinecraftMapper {
    partial class Mapper {
        private const int WoodlandSpacing = 8;
        private const int WoodlandCanopyRadius = 2;

        private enum WoodlandTreeSpecies {
            Spruce,
            Birch,
            Oak,
            Poplar,
        }

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
                    WoodlandTreeSpecies species = WoodlandSpecies(seed);
                    if (PositiveModulo(seed >> 10, 6) == 0 &&
                            CanSupportSapling(point, groundY)) {
                        DrawWoodlandSapling(point, groundY, species);
                        saplings++;
                    } else {
                        DrawWoodlandTree(point, groundY, seed, species);
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

        private void DrawWoodlandTree(
                BlockPosition point,
                int groundY,
                int seed,
                WoodlandTreeSpecies species) {
            bool isPoplar = species == WoodlandTreeSpecies.Poplar;
            int trunkHeight = (isPoplar ? 6 : 4) + PositiveModulo(seed >> 14, 3);
            string log;
            string leaves;
            switch (species) {
                case WoodlandTreeSpecies.Spruce:
                    log = AcquaticBlocks.SpruceLog;
                    leaves = AcquaticBlocks.SpruceLeaves;
                    break;
                case WoodlandTreeSpecies.Birch:
                    log = AcquaticBlocks.BirchLog;
                    leaves = AcquaticBlocks.BirchLeaves;
                    break;
                case WoodlandTreeSpecies.Poplar:
                    log = AcquaticBlocks.PoplarLog;
                    switch (PositiveModulo(seed >> 25, 3)) {
                        case 0: leaves = AcquaticBlocks.RedPoplarLeaves; break;
                        case 1: leaves = AcquaticBlocks.OrangePoplarLeaves; break;
                        default: leaves = AcquaticBlocks.YellowPoplarLeaves; break;
                    }
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
                int radius = isPoplar ? 1 : (layer < 2 ? 2 : 1);
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
                            if (isPoplar) {
                                // Poplar leaves have no distance/persistent state
                                // properties in 26.3.
                                _bm.SetBlock(leafX, y, leafZ, leaves);
                            } else {
                                _bm.SetBlock(leafX, y, leafZ, leaves,
                                    persistent: true,
                                    distance: Math.Max(1, Math.Abs(dx) + Math.Abs(dz)));
                            }
                        }
                    }
                }
            }
        }

        private void DrawWoodlandSapling(
                BlockPosition point,
                int groundY,
                WoodlandTreeSpecies species) {
            string sapling;
            switch (species) {
                case WoodlandTreeSpecies.Spruce: sapling = AcquaticBlocks.SpruceSapling; break;
                case WoodlandTreeSpecies.Birch: sapling = AcquaticBlocks.BirchSapling; break;
                case WoodlandTreeSpecies.Poplar:
                    // Unlike legacy saplings, poplar_sapling has no stage property.
                    _bm.SetBlock(point.X, groundY + 1, point.Z, AcquaticBlocks.PoplarSapling);
                    return;
                default: sapling = AcquaticBlocks.OakSapling; break;
            }
            _bm.SetBlock(point.X, groundY + 1, point.Z, sapling, stage: 0);
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

        private static WoodlandTreeSpecies WoodlandSpecies(int seed) {
            // Keep the original spruce/birch/oak choice for most cells and use
            // a separate hash band to introduce poplars without biasing it.
            if (PositiveModulo(seed >> 22, 4) == 0)
                return WoodlandTreeSpecies.Poplar;
            return (WoodlandTreeSpecies)PositiveModulo(seed >> 18, 3);
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
