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
        private Dictionary<BlockPosition, int> DrawBuildingWalls(OsmReader.Building building,
                Dictionary<BlockPosition, int> buildingPoints, int blockType, int data,
                int buildingHeight, int maxHeight, out bool extendsOverWater) {
            var start = building.BuildingNodes[0];
            var baseHeights = new Dictionary<BlockPosition, int>();
            extendsOverWater = false;
            for (int i = 1; i < building.BuildingNodes.Length; i++) {
                var from = _conv.ToBlock(start.Lat, start.Long);
                var to = _conv.ToBlock(building.BuildingNodes[i].Lat, building.BuildingNodes[i].Long);

                foreach (var point in PlotLine(from.X, from.Z, to.X, to.Z)) {
                    if (!_conv.IsValidPoint(point.Block)) {
                        continue;
                    }
                    // if we have duplicate entries for a building don't draw it twice...

                    bool overWater = IsWaterSurface(point.Block);
                    extendsOverWater |= overWater;
                    int height;
                    if (!buildingPoints.TryGetValue(point.Block, out height)) {
                        height = overWater
                            ? maxHeight
                            : GetBuildingBaseHeight(point.Block);
                        buildingPoints[point.Block] = height;
                    }

                    if (overWater && IsBuildingSupportLocation(point.Block)) {
                        DrawBuildingSupport(point.Block, maxHeight);
                    }

                    baseHeights[point.Block] = height;

                    for (int j = 0; j < (maxHeight - height) + buildingHeight; j++) {
                        if (height + j > 240) {
                            break;
                        }

                        SetMappedBlock(_bm, point.Block.X, height + j, point.Block.Z, (int)blockType, data);
                    }
                }

                start = building.BuildingNodes[i];
            }
            return baseHeights;
        }

        private bool IsWaterSurface(BlockPosition point) {
            int surfaceHeight = GetHeight(point);
            int id = _bm.GetID(point.X, surfaceHeight, point.Z);
            return id == BlockType.WATER || id == BlockType.STATIONARY_WATER;
        }

        private static bool IsBuildingSupportLocation(BlockPosition point) {
            return PositiveModulo(point.X, 6) == 0 &&
                PositiveModulo(point.Z, 6) == 0;
        }

        private void DrawBuildingSupport(BlockPosition point, int floorHeight) {
            int groundHeight = GetTerrainHeight(point);
            for (int y = groundHeight + 1; y < floorHeight; y++) {
                _bm.SetBlock(point.X, y, point.Z,
                    AcquaticBlocks.OakLog, axis: BlockAxis.Y);
            }
        }

        const int defaultWindowHeight = 2;
        const int defaultWindowSpacing = 2;
        private void DrawBuildingWindows(OsmReader.Building building, Dictionary<BlockPosition, int> buildingPoints, int data, int buildingHeight, Dictionary<BlockPosition, int> baseHeights, HashSet<BlockPosition> doors, int floorHeight) {
            var start = building.BuildingNodes[0];
            for (int i = 1; i < building.BuildingNodes.Length; i++) {
                var from = _conv.ToBlock(start.Lat, start.Long);
                var to = _conv.ToBlock(building.BuildingNodes[i].Lat, building.BuildingNodes[i].Long);

                int dist = Math.Max(Math.Abs(from.X - to.X), Math.Abs(from.Z - to.Z)) + 1;
                var rand = new Random((int)building.Id);

                // Do a 1st pass over the line to get the local maximum height, and location of the
                // doors.  The former handles buildings on hills so there's windows on base levels, and
                // the latter lets us account for doors during window placement.
                bool[] hasDoor = new bool[dist];
                int maxGroundHeight = 0;
                int doorCount = 0;
                foreach (var point in PlotLine(from.X, from.Z, to.X, to.Z)) {
                    if (!_conv.IsValidPoint(point.Block) || !baseHeights.ContainsKey(point.Block)) {
                        continue;
                    }
                    maxGroundHeight = Math.Max(maxGroundHeight, baseHeights[point.Block]);
                    if (doors.Contains(point.Block)) {
                        hasDoor[point.Row] = true;
                        doorCount++;
                    }
                }

                // Now figure out the window placement...
                bool[] windowPlacement = new bool[dist];
                bool[] groundLevelWindowPlacement = new bool[dist];
                int remaining = dist - 2;
                int startIndex = 1;

                int windowHeight = defaultWindowHeight;
                int verticalDistance = defaultWindowSpacing;
                int verticalOffset = 0;

                int blockType = BlockType.GLASS_PANE;
                int storyCount = buildingHeight / (windowHeight + verticalDistance);
                if (doorCount > 1 && storyCount <= 1) {
                    /* single story floor w/ multiple doors, let's just place windows around the
                     * doors */
                    verticalOffset = 3;
                    windowHeight = 4;
                    blockType = BlockType.GLASS;
                    for (int wallPos = 0; wallPos < groundLevelWindowPlacement.Length; wallPos++) {
                        if (hasDoor[wallPos]) {
                            groundLevelWindowPlacement[wallPos] = true;
                            if (wallPos > 0) {
                                groundLevelWindowPlacement[wallPos - 1] = true;
                            }
                            if (wallPos + 1 < groundLevelWindowPlacement.Length) {
                                groundLevelWindowPlacement[wallPos + 1] = true;
                            }
                        }
                    }
                    windowPlacement = groundLevelWindowPlacement;
                } else {
                    if (storyCount <= 1) {
                        windowHeight = 2;
                    }
                    while (remaining > 4) {
                        int totalSize = rand.Next(1, Math.Min(7, remaining / 2));
                        int spacing = rand.Next(1, Math.Max(1, Math.Min(totalSize - 1, totalSize / 6)));

                        for (int j = 0; j < totalSize - spacing; j++) {
                            windowPlacement[j + startIndex] = true;
                            windowPlacement[dist - j - startIndex - 1] = true;

                            if (!HasDoor(hasDoor, j + startIndex)) {
                                groundLevelWindowPlacement[j + startIndex] = true;
                            }
                            if (!HasDoor(hasDoor, dist - j - startIndex - 1)) {
                                groundLevelWindowPlacement[dist - j - startIndex - 1] = true;
                            }
                        }
                        startIndex += totalSize;
                        remaining -= totalSize * 2;
                    }
                }

                var points = PlotLine(from.X, from.Z, to.X, to.Z).ToArray();
                for (int curPoint = 0; curPoint < points.Length; curPoint++) {
                    var point = points[curPoint];
                    if (!_conv.IsValidPoint(point.Block) || !baseHeights.ContainsKey(point.Block)) {
                        continue;
                    }

                    var height = baseHeights[point.Block];
                    for (int j = 0; j < (maxGroundHeight - height) + buildingHeight; j++) {
                        if (height + j > 240) {
                            break;
                        }
                        // we want the windows to line up even across elevation changes,
                        // so we base them upon the ground level from maxGroundHeight.
                        int baseHeight = j - (floorHeight - height);
                        if (baseHeight <= 0) {
                            // don't place windows below ground
                            continue;
                        }
                        var targetWindowPlacement = (baseHeight / (verticalDistance + windowHeight) > 0) ? windowPlacement : groundLevelWindowPlacement;
                        if (((baseHeight + verticalOffset) % (verticalDistance + windowHeight)) >= verticalDistance && // vertical placement
                            targetWindowPlacement[point.Row] &&                 // horizontal placement
                            (baseHeight > 2 || !doors.Contains(point.Block))    // avoid doors
                           ) {
                            int targetBlock = blockType;
                            /* force corners into glass blocks so they don't draw halfway */
                            if (point.Row == 0 || point.Row == (dist - 1) ||
                                (curPoint != 0 && points[curPoint - 1].Block.X != point.Block.X && points[curPoint - 1].Block.Z != point.Block.Z) ||
                                (curPoint != points.Length - 1 && points[curPoint + 1].Block.X != point.Block.X && points[curPoint + 1].Block.Z != point.Block.Z)) {
                                targetBlock = BlockType.GLASS;
                            }
                            SetMappedBlock(_bm, point.Block.X, height + j, point.Block.Z, targetBlock, 0);
                        }
                    }
                }

                start = building.BuildingNodes[i];
            }
        }

        bool HasDoor(bool[] doors, int index) {
            if (doors[index]) {
                return true;
            } else if (index > 0 && doors[index - 1]) {
                return true;
            } else if (index < doors.Length - 1 && doors[index + 1]) {
                return true;
            }
            return false;
        }

        private static bool DoorIsClose(HashSet<BlockPosition> doors, LinePosition point, int windowWidth) {
            for (int i = -windowWidth; i <= windowWidth; i++) {
                if (doors.Contains(new BlockPosition(point.Block.X + i, point.Block.Z)) ||
                    doors.Contains(new BlockPosition(point.Block.X, point.Block.Z + i))) {
                    return true;
                }
            }
            return false;
        }

        private static void GetBuildingColor(OsmReader.Building building, int color, out int blockType, out int data) {
            blockType = BlockType.STAINED_CLAY;

            switch (building.Material) {
                case OsmReader.Material.Stone:
                    blockType = BlockType.STONE; data = (int)StoneType.STONE; return;
                case OsmReader.Material.Wood: blockType = BlockType.WOOD; data = (int)WoodType.OAK; return;
                case OsmReader.Material.Brick: blockType = BlockType.BRICK_BLOCK; data = 0; return;
                case OsmReader.Material.Concrete: blockType = BlockType.CONCRETE; data = 0; return;
                case OsmReader.Material.Sandstone: blockType = BlockType.SANDSTONE; data = 0; return;
                case OsmReader.Material.Slate: blockType = BlockType.STONE; data = (int)StoneType.POLISHED_GRANITE; return;
                case OsmReader.Material.Asphalt: blockType = BlockType.CONCRETE; data = 15; return; // black concrete
            }
            if (building.Color != null) {
                var colorMapping = FindClosestColor(building.Color.Value);
                blockType = colorMapping.VerticalBlockType;
                data = colorMapping.VerticalBlockData;
                return;
            }

            if (building.Street != null && building.HouseNumber != null) {
                data = (building.Street.GetHashCode()
                    ^ building.HouseNumber.GetHashCode() + 7) & 0xF;
                switch (building.Amenity) {
                    //case OsmReader.Amenity.Restaurant: blockType = BlockType;
                    case OsmReader.Amenity.TrainStation:
                    case OsmReader.Amenity.FireStation:
                    case OsmReader.Amenity.School:
                        blockType = BlockType.BRICK_BLOCK;
                        data = 0;
                        break;
                    case OsmReader.Amenity.Parking:
                        blockType = BlockType.STONE;
                        data = (int)StoneType.STONE;
                        break;
                    case OsmReader.Amenity.Bank:
                        blockType = BlockType.SANDSTONE;
                        data = 0;
                        break;
                    case OsmReader.Amenity.PlaceOfWorship: blockType = BlockType.NETHER_BRICK; data = 0; break;
                    case OsmReader.Amenity.CommunityCenter:
                        blockType = BlockType.OBSIDIAN;
                        data = 0;
                        break;
                    case OsmReader.Amenity.Theatre: blockType = BlockType.WOOD; data = (int)WoodType.BIRCH; break;
                }
            } else {
                data = color & 0xF;
            }
        }

    }
}
