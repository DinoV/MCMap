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
        static BlockPosition[] _queueDirs = new[] {
            new BlockPosition(1, 0),
            new BlockPosition(-1, 0),
            new BlockPosition(0, 1),
            new BlockPosition(0, -1),
        };

        private HashSet<BlockPosition> DrawBuildingFloor(int top, int left, int bottom,
                int right, Dictionary<BlockPosition, int> baseHeights,
                Dictionary<BlockPosition, int> buildingPoints, OsmReader.Building building,
                int buildingHeight, HashSet<BlockPosition> doors, int? fixedFloorHeight,
                out int avgHeight) {
            int storyCount = buildingHeight / (defaultWindowHeight + defaultWindowSpacing); // matching current values in 
            int blockType = 0, blockData = 0;
            switch (building.Id % 14) {
                case 0:
                case 1:
                case 2:
                case 3:
                case 4:
                case 5:
                    blockType = BlockType.WOOD_SLAB;
                    blockData = (int)(((building.Id % 14)) % 6) | 0x08;
                    break;
                case 6:
                case 7:
                case 8:
                case 9:
                case 10:
                case 11:
                case 12:
                case 13:
                    blockType = BlockType.STONE_SLAB;
                    blockData = (int)((building.Id % 8)) | 0x08;
                    break;
            }

            int maxHeight, minHeight;
            avgHeight = 0;
            var floorPoints = GetFloorPoints(top, left, bottom, right, baseHeights, out minHeight, out maxHeight, out avgHeight);

            foreach (var door in doors) {
                // if we have a door that's lower than the avg floor height
                // let's adjust to the door height instead
                if (baseHeights[door] < avgHeight) {
                    avgHeight = baseHeights[door];
                } else if (baseHeights[door] > avgHeight) {
                }
            }

            if (fixedFloorHeight.HasValue) {
                avgHeight = fixedFloorHeight.Value;
                minHeight = avgHeight;
                maxHeight = avgHeight;
            }

            foreach (var point in floorPoints) {
                bool overWater = fixedFloorHeight.HasValue && IsWaterSurface(point);
                var height = fixedFloorHeight ?? GetHeight(point);

                if (overWater && IsBuildingSupportLocation(point)) {
                    DrawBuildingSupport(point, height);
                }

                // Smooth out building interiors somewhat...
                if (maxHeight - minHeight <= 2) {
                    if (height == avgHeight + 1) {
                        _bm.SetBlock(point.X, height, point.Z, AcquaticBlocks.Air);
                        height--;
                    } else if (height == avgHeight - 1) {
                        height++;
                    }
                }
                buildingPoints[point] = height;
                SetMappedBlock(_bm, point.X, height, point.Z, blockType, blockData);
                if ((point.X - left) % 8 == 0 && (point.Z - top) % 8 == 0) {
                    _bm.SetBlock(point.X, height + 1, point.Z, AcquaticBlocks.Torch);
                }
            }

            var stairType = GetStairsForBlock(blockType, blockData);
            if (maxHeight - minHeight <= 2) {
                foreach (var door in doors) {
                    // if we dropped the floor in front of a door let's add a step back up
                    if (baseHeights[door] > avgHeight) {
                        DrawDoorStairs(baseHeights, floorPoints, door, stairType);
                    }
                }
            }
            if (storyCount > 1) {
                List<StairInfo> stairs = GetStairLocations(top, left, bottom, right, building, floorPoints);

                for (int i = 1; i < storyCount; i++) {
                    HashSet<BlockPosition> stairPoints = new HashSet<BlockPosition>();
                    foreach (var stair in stairs) {
                        DrawStairs(blockType, blockData, i, stairPoints, stair);
                    }

                    foreach (var point in floorPoints) {
                        if (stairPoints.Contains(point)) {
                            continue;
                        }

                        int height = avgHeight + (defaultWindowHeight + defaultWindowSpacing) * i;
                        SetMappedBlock(_bm, point.X, height, point.Z, blockType, blockData);
                        if ((point.X - left) % 8 == 0 && (point.Z - top) % 8 == 0) {
                            _bm.SetBlock(point.X, height + 1, point.Z, AcquaticBlocks.Torch);
                        }
                    }
                }
            }
            return floorPoints;
        }

        private void DrawStairs(int blockType, int blockData, int story, HashSet<BlockPosition> stairPoints, StairInfo stair) {
            BlockPosition pos;

            switch (stair.Dir) {
                case FullDirection.West: pos = new BlockPosition(stair.X + StepCountPerFloor - 1, stair.Z); break;
                case FullDirection.North: pos = new BlockPosition(stair.X, stair.Z + StepCountPerFloor - 1); break;
                case FullDirection.East: pos = new BlockPosition(stair.X, stair.Z); break;
                case FullDirection.South: pos = new BlockPosition(stair.X, stair.Z); break;
                default:
                    throw new InvalidOperationException();
            }

            var height = GetHeight(pos);
            if (story > 1) {
                height += (defaultWindowHeight + defaultWindowSpacing) - 1;
            }

            for (int step = 0; step < StepCountPerFloor; step++) {
                for (int width = 0; width < StairsWidth; width++) {
                    switch (stair.Dir) {
                        case FullDirection.West: pos = new BlockPosition(stair.X + StepCountPerFloor - 1 - step, stair.Z + width); break;
                        case FullDirection.North: pos = new BlockPosition(stair.X + width, stair.Z + StepCountPerFloor - 1 - step); break;
                        case FullDirection.East: pos = new BlockPosition(stair.X + step, stair.Z + width); break;
                        case FullDirection.South: pos = new BlockPosition(stair.X + width, stair.Z + step); break;
                        default:
                            throw new InvalidOperationException();
                    }
                    int stepData = (step & 0x01) == 0
                        ? blockData & ~0x08
                        : blockData | 0x08;
                    SetMappedBlock(
                        _bm, pos.X, height + 1, pos.Z,
                        blockType, stepData);
                    stairPoints.Add(pos);
                }
                if ((step & 0x01) != 0) {
                    height++;
                }
            }
        }

        const int StepCountPerFloor = 7;
        const int StairsWidth = 3;


        private static List<StairInfo> GetStairLocations(int top, int left, int bottom, int right, OsmReader.Building building, HashSet<BlockPosition> floorPoints) {
            List<StairInfo> stairs = new List<StairInfo>();
            var random = new Random((int)building.Id);
            HashSet<BlockPosition> stairPoints = new HashSet<BlockPosition>();
            for (int i = 0; i < Math.Max(1, floorPoints.Count / 800); i++) {
                var start = building.BuildingNodes[0];
                int z = 0, x = 0;
                bool ok = false;
                FullDirection dir = (FullDirection)(random.Next() % 4);
                if (top + 2 >= bottom - 2 || left + 2 >= right - 2) {
                    continue;
                }
                for (int attempt = 0; attempt < 10 && !ok; attempt++) {
                    z = random.Next(top + 2, bottom - 2);
                    x = random.Next(left + 2, right - 2);

                    ok = true;
                    for (int step = -3; step < StepCountPerFloor + 3 && ok; step++) {
                        for (int width = 0; width < StairsWidth; width++) {
                            BlockPosition pos;
                            if (dir == FullDirection.East || dir == FullDirection.West) {
                                pos = new BlockPosition(x + step, z + width);
                            } else {
                                pos = new BlockPosition(x + width, z + step);
                            }

                            if (!floorPoints.Contains(pos) || stairPoints.Contains(pos)) {
                                ok = false;
                                break;
                            }
                        }
                    }
                }
                if (ok) {
                    for (int step = -3; step < StepCountPerFloor + 3 && ok; step++) {
                        for (int width = 0; width < 3; width++) {
                            BlockPosition pos;
                            if (dir == FullDirection.East || dir == FullDirection.West) {
                                pos = new BlockPosition(x + step, z + width);
                            } else {
                                pos = new BlockPosition(x + width, z + step);
                            }
                            stairPoints.Add(pos);
                        }
                    }
                    stairs.Add(new StairInfo(dir, x, z));
                }
            }

            return stairs;
        }

        struct StairInfo {
            public readonly FullDirection Dir;
            public readonly int X, Z;

            public StairInfo(FullDirection dir, int x, int z) {
                Dir = dir;
                X = x;
                Z = z;
            }
        }

        private void DrawDoorStairs(Dictionary<BlockPosition, int> baseHeights, HashSet<BlockPosition> floorPoints, BlockPosition door, int stairType) {
            if (floorPoints.Contains(new BlockPosition(door.X - 1, door.Z))) {
                SetMappedBlock(_bm, door.X - 1, baseHeights[door], door.Z, stairType, (int)FullDirection.East);
                if (floorPoints.Contains(new BlockPosition(door.X - 1, door.Z - 1))) {
                    SetMappedBlock(_bm, door.X - 1, baseHeights[door], door.Z - 1, stairType, (int)FullDirection.East);
                }
                if (floorPoints.Contains(new BlockPosition(door.X - 1, door.Z + 1))) {
                    SetMappedBlock(_bm, door.X - 1, baseHeights[door], door.Z + 1, stairType, (int)FullDirection.East);
                }
            } else if (floorPoints.Contains(new BlockPosition(door.X + 1, door.Z))) {
                SetMappedBlock(_bm, door.X + 1, baseHeights[door], door.Z, stairType, (int)FullDirection.West);
                if (floorPoints.Contains(new BlockPosition(door.X + 1, door.Z - 1))) {
                    SetMappedBlock(_bm, door.X + 1, baseHeights[door], door.Z - 1, stairType, (int)FullDirection.West);
                }
                if (floorPoints.Contains(new BlockPosition(door.X + 1, door.Z + 1))) {
                    SetMappedBlock(_bm, door.X + 1, baseHeights[door], door.Z + 1, stairType, (int)FullDirection.West);
                }
            } else if (floorPoints.Contains(new BlockPosition(door.X, door.Z - 1))) {
                SetMappedBlock(_bm, door.X, baseHeights[door], door.Z - 1, stairType, (int)FullDirection.North);
                if (floorPoints.Contains(new BlockPosition(door.X - 1, door.Z - 1))) {
                    SetMappedBlock(_bm, door.X - 1, baseHeights[door], door.Z - 1, stairType, (int)FullDirection.North);
                }
                if (floorPoints.Contains(new BlockPosition(door.X + 1, door.Z - 1))) {
                    SetMappedBlock(_bm, door.X + 1, baseHeights[door], door.Z - 1, stairType, (int)FullDirection.North);
                }
            } else if (floorPoints.Contains(new BlockPosition(door.X, door.Z + 1))) {
                SetMappedBlock(_bm, door.X, baseHeights[door], door.Z + 1, stairType, (int)FullDirection.South);
                if (floorPoints.Contains(new BlockPosition(door.X - 1, door.Z + 1))) {
                    SetMappedBlock(_bm, door.X - 1, baseHeights[door], door.Z + 1, stairType, (int)FullDirection.South);
                }
                if (floorPoints.Contains(new BlockPosition(door.X + 1, door.Z + 1))) {
                    SetMappedBlock(_bm, door.X + 1, baseHeights[door], door.Z + 1, stairType, (int)FullDirection.South);
                }
            }
        }

        private static int GetStairsForBlock(int blockType, int blockData) {
            switch (blockType) {
                case BlockType.STONE_SLAB:
                    switch (blockData & 0x07) {
                        case 0:
                        case 1:
                            blockType = BlockType.SANDSTONE_STAIRS;
                            break;
                        case 2:
                            blockType = BlockType.STONE_BRICK_STAIRS;
                            break;
                        case 3:
                            blockType = BlockType.COBBLESTONE_STAIRS;
                            break;
                        case 4:
                            blockType = BlockType.BRICK_STAIRS;
                            break;
                        case 5:
                            blockType = BlockType.STONE_BRICK_STAIRS;
                            break;
                        case 6:
                            blockType = BlockType.NETHER_BRICK_STAIRS;
                            break;
                        case 7:
                            blockType = BlockType.QUARTZ_STAIRS;
                            break;
                        default:
                            Console.WriteLine("Huh?");
                            break;
                    }
                    break;
                case BlockType.WOOD_SLAB:
                    switch (blockData & 0x07) {
                        case 0: blockType = BlockType.WOOD_STAIRS; break;
                        case 1: blockType = BlockType.SPRUCE_WOOD_STAIRS; break;
                        case 2: blockType = BlockType.BIRCH_WOOD_STAIRS; break;
                        case 3: blockType = BlockType.JUNGLE_WOOD_STAIRS; break;
                        case 4: blockType = BlockType.ACACIA_WOOD_STAIRS; break;
                        case 5: blockType = BlockType.DARK_OAK_WOOD_STAIRS; break;
                        default:
                            Console.WriteLine("Huh?");
                            break;
                    }
                    break;
                default:
                    Console.WriteLine("Huh?");
                    break;
            }

            return blockType;
        }

        private int FillArea(int top, int left, int bottom, int right, Dictionary<BlockPosition, int> baseHeights, int blockType, int blockData, int? fillHeight = null) {
            int maxHeight, minHeight, avgHeight;
            foreach (var point in GetFloorPoints(top, left, bottom, right, baseHeights, out maxHeight, out minHeight, out avgHeight)) {
                var height = fillHeight ?? GetHeight(point);

                SetMappedBlock(_bm, point.X, height, point.Z, blockType, blockData);
            }
            return maxHeight;
        }

        private int FillArea(int top, int left, int bottom, int right, Dictionary<BlockPosition, int> baseHeights, Action<BlockPosition> drawPoint) {
            int maxHeight, minHeight, avgHeight;
            foreach (var point in GetFloorPoints(top, left, bottom, right, baseHeights, out maxHeight, out minHeight, out avgHeight)) {
                drawPoint(point);
            }
            return maxHeight;
        }

        private HashSet<BlockPosition> GetFloorPoints(int top, int left, int bottom, int right, Dictionary<BlockPosition, int> baseHeights, out int minHeight, out int maxHeight, out int avgHeight) {
            // First find a point in the building.  We start at the mid-point,
            // walk to hit a wall, and keep on walking until we hit a non-wall.
            int centerX = (left + right) / 2;
            BlockPosition? startPoint = null;
            bool hitBuilding = false;
            maxHeight = Int32.MinValue;
            minHeight = Int32.MaxValue;
            avgHeight = 0;

            for (int z = top; z <= bottom; z++) {
                if (baseHeights.ContainsKey(new BlockPosition(centerX, z))) {
                    hitBuilding = true;
                } else if (hitBuilding) {
                    startPoint = new BlockPosition(centerX, z);
                    break;
                }
            }
            if (startPoint == null || !_conv.IsValidPoint(startPoint.Value)) {
                return new HashSet<BlockPosition>();
            }
            int newPoints = 0;
            Queue<BlockPosition> points = new Queue<BlockPosition>();
            points.Enqueue(startPoint.Value);

            HashSet<BlockPosition> floorPoints = new HashSet<BlockPosition>();
            if (!_conv.IsValidPoint(startPoint.Value)) {
                return floorPoints;
            }
            floorPoints.Add(startPoint.Value);
            int allHeights = 0;
            while (points.Count != 0) {
                var next = points.Dequeue();

                var height = GetHeight(next);
                allHeights += height;
                maxHeight = Math.Max(height, maxHeight);
                minHeight = Math.Min(height, minHeight);
                newPoints++;

                for (int i = 0; i < _queueDirs.Length; i++) {
                    var cand = new BlockPosition(next.X + _queueDirs[i].X, next.Z + _queueDirs[i].Z);
                    if (cand.X < left || cand.X > right || cand.Z < top || cand.Z > bottom) {
                        continue;
                    }
                    if (!baseHeights.ContainsKey(cand) && !floorPoints.Contains(cand)) {
                        if (!_conv.IsValidPoint(cand)) {
                            continue;
                        }
                        floorPoints.Add(cand);
                        points.Enqueue(cand);
                    }
                }
            }
            if (floorPoints.Count > 0) {
                var tmpAvg = (float)allHeights / floorPoints.Count;
                avgHeight = (int)Math.Round(tmpAvg);
            }
            return floorPoints;
        }

    }
}
