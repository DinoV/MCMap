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
        private Dictionary<BlockPosition, int> DrawBuildings() {
            int cur = 0;
            var buildingPoints = new Dictionary<BlockPosition, int>();
            HashSet<BlockPosition> roofPoints = new HashSet<BlockPosition>();
            IEnumerable<KeyValuePair<long, OsmReader.Building>> buildingList;
            if (_buildings.Length != 0) {
                buildingList = _reader.Buildings.Where(x => _buildings.Contains(x.Key));
            } else {
                buildingList = _reader.Buildings.OrderBy(x => MapOrder(_conv, x.Value.BuildingNodes.First())).Where(
                    x => _conv.IsValidPoint(_conv.ToBlock(x.Value.BuildingNodes.First().Lat, x.Value.BuildingNodes.First().Long))
                );
            }
            var count = buildingList.Count();
            // new[] { reader.Buildings[224047836], reader.Buildings[224048580], reader.Buildings[222409095], reader.Buildings[222409094] }
            //var buildings = new long[]{ 224047836 /*363250260, 4856210822, 4856214545 /*224047836, 224048580, 222409095, 222409094*/ };
            //var buildingList = _reader.Buildings.Where(x => buildings.Contains(x.Key));
            foreach (var idAndBuilding in buildingList) {
                var building = idAndBuilding.Value;
                if ((++cur % 200) == 0) {
                    SaveBlocks();
                }

                WriteLine("Building {4}: {0} {1} ({2}/{3})", building.HouseNumber, building.Street, cur, count, building.Id);
                if (TryDrawIconicBuilding(building, buildingPoints, roofPoints)) {
                    continue;
                }
                DrawBuilding(
                    building,
                    buildingPoints,
                    roofPoints,
                    (int)(idAndBuilding.Key & 0xF));
            }

            SaveBlocks();

            return buildingPoints;
        }

        private void SaveBlocks() {
            if (!_dryRun) {
                _world.SaveBlocks();
            }
        }

        private void DrawBuilding(OsmReader.Building building, Dictionary<BlockPosition, int> buildingPoints, HashSet<BlockPosition> roofPoints, int color) {
            int blockType, data;
            GetBuildingColor(building, color, out blockType, out data);


            int buildingHeight = Math.Max(6, (int)((building.Stories ?? 1) * 4) + 2);
            var nodes = building.BuildingNodes;
            int top, left, bottom, right;
            int groundMaxHeight = CalculateBounds(nodes, out top, out left, out bottom, out right, buildingPoints);
            if (groundMaxHeight == Int32.MinValue) {
                // no valid points for this building...
                return;
            }

            var baseHeights = DrawBuildingWalls(building, buildingPoints, blockType, data, buildingHeight, groundMaxHeight);
            var doors = DrawBuildingSign(building, top, left, bottom, right, baseHeights, buildingPoints);

            int floorHeight;
            var floorPoints = DrawBuildingFloor(top, left, bottom, right, baseHeights, buildingPoints, building, buildingHeight, doors, out floorHeight);

            DrawBuildingRoof(building, top, left, bottom, right, buildingHeight, groundMaxHeight, buildingPoints, roofPoints, floorPoints);

            DrawBuildingWindows(building, buildingPoints, data, buildingHeight, baseHeights, doors, floorHeight);
        }

        private int CalculateBounds(OsmReader.Node[] nodes, out int top, out int left, out int bottom, out int right, Dictionary<BlockPosition, int> buildingPoints = null) {
            top = Int32.MaxValue;
            left = Int32.MaxValue;
            bottom = Int32.MinValue;
            right = Int32.MinValue;
            int groundMaxHeight = Int32.MinValue;
            var start = nodes[0];

            for (int i = 1; i < nodes.Length; i++) {
                var from = _conv.ToBlock(start.Lat, start.Long);
                var to = _conv.ToBlock(nodes[i].Lat, nodes[i].Long);

                top = Math.Min(top, from.Z);
                bottom = Math.Max(bottom, from.Z);
                left = Math.Min(left, from.X);
                right = Math.Max(right, from.X);

                foreach (var point in PlotLine(from.X, from.Z, to.X, to.Z)) {
                    if (_conv.IsValidPoint(point.Block)) {
                        int height;
                        if (buildingPoints == null || !buildingPoints.TryGetValue(point.Block, out height)) {
                            height = GetHeight(point.Block);
                        }
                        groundMaxHeight = Math.Max(groundMaxHeight, height);
                    }
                }
                start = nodes[i];
            }

            return groundMaxHeight;
        }

    }
}
