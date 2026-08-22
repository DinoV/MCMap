using CsvHelper;
using Substrate;
using Substrate.TileEntities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using static MinecraftMapper.OsmReader;

namespace MinecraftMapper {
    partial class Mapper {
        private struct ColorMapping {
            public readonly OsmReader.Color Color;
            public readonly int VerticalBlockType;
            public readonly int StairsBlockType;
            public readonly int VerticalBlockData;

            public ColorMapping(OsmReader.Color color, int vertical, int stairs, int verticalBlockData = 0) {
                Color = color;
                VerticalBlockType = vertical;
                StairsBlockType = stairs;
                VerticalBlockData = verticalBlockData;
            }
        }

        private static ColorMapping[] ColorMappings = new[] {
            new ColorMapping(
                new OsmReader.Color(0x80, 0x80, 0x80),
                BlockType.COBBLESTONE,
                BlockType.COBBLESTONE_STAIRS
            ),
            new ColorMapping(
                new OsmReader.Color(0xff, 0, 0),
                BlockType.BRICK_BLOCK,
                BlockType.BRICK_STAIRS
            ),
            new ColorMapping(
                new OsmReader.Color(0x80, 0, 0),
                BlockType.NETHER_BRICK,
                BlockType.NETHER_BRICK_STAIRS
            ),
            new ColorMapping(
                new OsmReader.Color(0x80, 0, 0x80),
                BlockType.PURPUR,
                BlockType.PURPUR_STAIRS
            ),
             new ColorMapping(
                new OsmReader.Color(0xff, 0xff, 0xff),
                BlockType.QUARTZ_BLOCK,
                BlockType.QUARTZ_STAIRS
            ),
            new ColorMapping(
                new OsmReader.Color(0xff, 0xff, 0xff),
                BlockType.SANDSTONE,
                BlockType.SANDSTONE_STAIRS
            ),
            new ColorMapping(
                new OsmReader.Color(0xff, 0xff, 0xff),
                BlockType.STAINED_CLAY,
                BlockType.SANDSTONE_STAIRS,
                0
            ),
            new ColorMapping(
                new OsmReader.Color(0xff, 0xa5, 0x00), // orange
                BlockType.STAINED_CLAY,
                BlockType.SANDSTONE_STAIRS,
                1
            ),
            new ColorMapping(
                new OsmReader.Color(0xff, 0x0, 0xff), // mangenta
                BlockType.STAINED_CLAY,
                BlockType.SANDSTONE_STAIRS,
                2
            ),
            new ColorMapping(
                new OsmReader.Color(0xad, 0xd8, 0xe6), // light blue
                BlockType.STAINED_CLAY,
                BlockType.SANDSTONE_STAIRS,
                3
            ),
            new ColorMapping(
                new OsmReader.Color(0xff, 0xff, 0xe0), // light yellow
                BlockType.STAINED_CLAY,
                BlockType.SANDSTONE_STAIRS,
                4
            ),
            new ColorMapping(
                new OsmReader.Color(0x32, 0xcd, 0x32), // lime
                BlockType.STAINED_CLAY,
                BlockType.SANDSTONE_STAIRS,
                5
            ),
            new ColorMapping(
                new OsmReader.Color(0xff, 0xc0, 0xcb), // pink
                BlockType.STAINED_CLAY,
                BlockType.SANDSTONE_STAIRS,
                6
            ),
            new ColorMapping(
                new OsmReader.Color(0x7f, 0x7f, 0x7f), // gray
                BlockType.STAINED_CLAY,
                BlockType.SANDSTONE_STAIRS,
                7
            ),
            new ColorMapping(
                new OsmReader.Color(0xaf, 0xaf, 0xaf), // light gray
                BlockType.STAINED_CLAY,
                BlockType.SANDSTONE_STAIRS,
                8
            ),
            new ColorMapping(
                new OsmReader.Color(0x00, 0xff, 0xff), // cyan
                BlockType.STAINED_CLAY,
                BlockType.SANDSTONE_STAIRS,
                9
            ),
            new ColorMapping(
                new OsmReader.Color(0xa0, 0x20, 0xf0), // purple
                BlockType.STAINED_CLAY,
                BlockType.SANDSTONE_STAIRS,
                10
            ),
            new ColorMapping(
                new OsmReader.Color(0x0, 0x0, 0xff), // blue
                BlockType.STAINED_CLAY,
                BlockType.SANDSTONE_STAIRS,
                11
            ),
            new ColorMapping(
                new OsmReader.Color(0xa5, 0x2a, 0x2a), // brown
                BlockType.STAINED_CLAY,
                BlockType.SANDSTONE_STAIRS,
                12
            ),
            new ColorMapping(
                new OsmReader.Color(0x00, 0xff, 0x00), // green
                BlockType.STAINED_CLAY,
                BlockType.SANDSTONE_STAIRS,
                13
            ),
            new ColorMapping(
                new OsmReader.Color(0xaa, 0x0, 0x00), // red
                BlockType.STAINED_CLAY,
                BlockType.SANDSTONE_STAIRS,
                14
            ),
            new ColorMapping(
                new OsmReader.Color(0x0, 0x0, 0x0), // mangenta
                BlockType.STAINED_CLAY,
                BlockType.SANDSTONE_STAIRS,
                15
            ),
        };

        private void DrawBuildingRoof(OsmReader.Building building, int top, int left, int bottom, int right, int buildingHeight, int maxHeight, Dictionary<BlockPosition, int> buildingPositions, HashSet<BlockPosition> roofPoints, HashSet<BlockPosition> floorPoints) {
            // OAKWOAD_STAIRS
            // SPRUCEWOOD_STAIRS
            // BIRCHWOOD_STAIRS
            // DARKOAK_WOOD_STAIRS
            // ACACIA_WOOD_STAIRS
            // RED_SANDSTONE_STAIRS
            // STONE_BRICK_STAIRS
            // JUNGLE_WOOD_STAIRS
            int dataType = (int)0;
            var roofStart = maxHeight + buildingHeight;
            var color = building?.Roof?.Color;
            ColorMapping roofMapping = default(ColorMapping);
            if (color == null) {
                roofMapping = ColorMappings[Math.Abs(building.BuildingNodes.First().Lat.GetHashCode() ^ (building.HouseNumber?.GetHashCode() ?? 0)) % ColorMappings.Length];
            } else {
                roofMapping = FindClosestColor(color.Value);
            }
            int stairType = roofMapping.StairsBlockType;
            int baseType = roofMapping.VerticalBlockType;
            switch (building.Roof?.Type) {
                //default:
                default:
                case OsmReader.RoofType.Flat:
                    DrawFlatRoof(top, left, bottom, right, buildingPositions, roofPoints, roofStart, roofMapping, floorPoints);
                    break;
                case OsmReader.RoofType.Hipped:
                    DrawHippedRoof(top, left, bottom, right, buildingPositions, roofPoints, dataType, roofStart, stairType, baseType);
                    break;
                case OsmReader.RoofType.Gabled:
                    DrawGabledRoof(building, top, left, bottom, right, buildingPositions, roofPoints, dataType, roofStart, stairType, baseType);
                    break;
            }
        }

        private static ColorMapping FindClosestColor(OsmReader.Color color) {
            ColorMapping roofMapping = default(ColorMapping);
            double closest = double.MaxValue;

            foreach (var mapping in ColorMappings) {
                var distance = Math.Sqrt(
                    (color.R - mapping.Color.R) * (color.R - mapping.Color.R) +
                    (color.G - mapping.Color.B) * (color.G - mapping.Color.G) +
                    (color.B - mapping.Color.R) * (color.B - mapping.Color.B)
                );
                if (distance < closest) {
                    closest = distance;
                    roofMapping = mapping;
                    if (distance == 0) {
                        break;
                    }
                }
            }

            return roofMapping;
        }

        private void DrawGabledRoof(OsmReader.Building building, int top, int left, int bottom, int right, Dictionary<BlockPosition, int> buildingPositions, HashSet<BlockPosition> roofPoints, int dataType, int roofStart, int stairType, int baseType) {
            var height = Math.Abs(top - bottom);
            var width = Math.Abs(left - right);

            if (width > height && !building.Roof.OrientationAcross) {
                for (int x = left; x <= right; x++) {
                    int tStart = -1, bStart = -1;
                    for (int y = top; y <= bottom; y++) {
                        if (buildingPositions.ContainsKey(new BlockPosition(x, y))) {
                            tStart = y;
                            break;
                        }
                    }
                    for (int y = bottom; y >= top; y--) {
                        if (buildingPositions.ContainsKey(new BlockPosition(x, y))) {
                            bStart = y;
                            break;
                        }
                    }
                    var lHeight = Math.Abs(tStart - bStart);

                    for (int i = 0; i < (lHeight + 1) / 2; i++) {
                        if (roofPoints.Contains(new BlockPosition(x, i + tStart))) {
                            continue;
                        }
                        SetMappedBlock(_bm, x, roofStart + i, i + tStart, stairType, (int)FullDirection.East);

                        SetMappedBlock(_bm, x, roofStart + i, bStart - i, stairType, (int)FullDirection.West);

                        roofPoints.Add(new BlockPosition(x, i + tStart));
                        roofPoints.Add(new BlockPosition(x, bStart - i));

                        for (int j = 0; j < i; j++) {
                            SetMappedBlock(_bm, x, roofStart + j, i + tStart, baseType, dataType);

                            SetMappedBlock(_bm, x, roofStart + j, bStart - i, baseType, dataType);
                        }
                    }

                    if ((lHeight & 0x01) == 0) {
                        var ridgePoint = new BlockPosition(x, tStart + lHeight / 2);
                        if (!roofPoints.Contains(ridgePoint)) {
                            for (int j = 0; j <= lHeight / 2; j++) {
                                SetMappedBlock(_bm, x, roofStart + j, ridgePoint.Z, baseType, dataType);
                            }
                            roofPoints.Add(ridgePoint);
                        }
                    }
                }
            } else {
                for (int y = top; y <= bottom; y++) {
                    int lStart = -1, rStart = -1;
                    for (int x = left; x <= right; x++) {
                        if (buildingPositions.ContainsKey(new BlockPosition(x, y))) {
                            lStart = x;
                            break;
                        }
                    }
                    for (int x = right; x >= left; x--) {
                        if (buildingPositions.ContainsKey(new BlockPosition(x, y))) {
                            rStart = x;
                            break;
                        }
                    }
                    var lWidth = Math.Abs(lStart - rStart);

                    for (int i = 0; i < (lWidth + 1) / 2; i++) {
                        if (roofPoints.Contains(new BlockPosition(i + lStart, y))) {
                            continue;
                        }

                        SetMappedBlock(_bm, i + lStart, roofStart + i, y, stairType, (int)FullDirection.East);

                        SetMappedBlock(_bm, rStart - i, roofStart + i, y, stairType, (int)FullDirection.West);
                        roofPoints.Add(new BlockPosition(i + lStart, y));
                        for (int j = 0; j < i; j++) {
                            SetMappedBlock(_bm, i + lStart, roofStart + j, y, baseType, dataType);

                            SetMappedBlock(_bm, rStart - i, roofStart + j, y, baseType, dataType);
                        }
                    }

                    if ((lWidth & 0x01) == 0) {
                        var ridgePoint = new BlockPosition(lStart + lWidth / 2, y);
                        if (!roofPoints.Contains(ridgePoint)) {
                            for (int j = 0; j <= lWidth / 2; j++) {
                                SetMappedBlock(_bm, ridgePoint.X, roofStart + j, y, baseType, dataType);
                            }
                            roofPoints.Add(ridgePoint);
                        }
                    }
                }
            }
        }

        private void DrawHippedRoof(int top, int left, int bottom, int right, Dictionary<BlockPosition, int> buildingPositions, HashSet<BlockPosition> roofPoints, int dataType, int roofStart, int stairType, int baseType) {
            for (int z = top; z <= bottom; z++) {
                //int tStart = -1, bStart = -1;
                int lStart = -1, rStart = -1;
                for (int x = left; x <= right; x++) {
                    if (buildingPositions.ContainsKey(new BlockPosition(x, z))) {
                        lStart = x;
                        break;
                    }
                }
                for (int x = right; x >= left; x--) {
                    if (buildingPositions.ContainsKey(new BlockPosition(x, z))) {
                        rStart = x;
                        break;
                    }
                }

                var lWidth = Math.Abs(lStart - rStart);

                int targetHeight = (lWidth + 1) / 2;
                if (z - top < lWidth / 2) {
                    targetHeight = Math.Min(targetHeight, z - top);
                } else if (bottom - z < lWidth / 2) {
                    targetHeight = Math.Min(targetHeight, bottom - z);
                }
                for (int i = 0; i <= (lWidth + 1) / 2; i++) {
                    for (int y = 0; y <= Math.Min(i, targetHeight); y++) {
                        if (roofPoints.Contains(new BlockPosition(i + lStart, z))) {
                            continue;
                        }
                        roofPoints.Add(new BlockPosition(i + lStart, z));
                        roofPoints.Add(new BlockPosition(rStart - i, z));
                        if (y == Math.Min(targetHeight, i)) {
                            FullDirection? direction = null;
                            if (i > z - top) {
                                direction = FullDirection.South;
                            } else if (i > bottom - z) {
                                direction = FullDirection.North;
                            }
                            SetMappedBlock(_bm, i + lStart, roofStart + y, z, stairType, (int)(direction ?? FullDirection.East));

                            SetMappedBlock(_bm, rStart - i, roofStart + y, z, stairType, (int)(direction ?? FullDirection.West));

                        } else {
                            SetMappedBlock(_bm, i + lStart, roofStart + y, z, baseType, dataType);

                            SetMappedBlock(_bm, rStart - i, roofStart + y, z, baseType, dataType);
                        }
                    }
                }
            }
        }

        private void DrawFlatRoof(int top, int left, int bottom, int right, Dictionary<BlockPosition, int> buildingPositions, HashSet<BlockPosition> roofPoints, int roofStart, ColorMapping roofMapping, HashSet<BlockPosition> floorPoints) {
            for (int z = top; z <= bottom; z++) {
                int lStart = -1, rStart = -1;
                for (int x = left; x <= right; x++) {
                    if (buildingPositions.ContainsKey(new BlockPosition(x, z))) {
                        lStart = x;
                        break;
                    }
                }
                for (int x = right; x >= left; x--) {
                    if (buildingPositions.ContainsKey(new BlockPosition(x, z))) {
                        rStart = x;
                        break;
                    }
                }

                for (int x = lStart; x <= rStart; x++) {
                    var point = new BlockPosition(x, z);
                    if (roofPoints.Contains(point) ||
                        (!buildingPositions.ContainsKey(point) && !floorPoints.Contains(point))) {
                        continue;
                    }
                    SetMappedBlock(_bm, x, roofStart, z, roofMapping.VerticalBlockType, roofMapping.VerticalBlockData);
                    roofPoints.Add(point);
                }
            }
        }
    }
}
