using CsvHelper;
using Kaos.Collections;
using Substrate;
using Substrate.TileEntities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;

// This example replaces all instances of one block ID with another in a world.
// Substrate will handle all of the lower-level headaches that can pop up, such
// as maintaining correct lighting or replacing TileEntity records for blocks
// that need them.

// For a more advanced Block Replace example, see replace.cs in NBToolkit.

namespace MinecraftMapper {
    class Mapper {
        private readonly NbtWorld _world;
        private readonly PositionConverter _conv;
        private readonly OsmReader _reader;
        private readonly AnvilBlockManager _bm;
        private readonly long[] _buildings, _roads;

        public Mapper(string world, string osmXml, PositionBase[] conversions, long[] buildings, long[] roads) {
            _world = NbtWorld.Open(world);
            _buildings = buildings;
            _roads = roads;
            _bm = _world.GetBlockManager() as AnvilBlockManager;
            _bm.AutoLight = true;

            var region = Path.Combine(world, "region");

            int maxX = 0, maxZ = 0;
            foreach (var file in Directory.GetFiles(region)) {
                var filenameInfo = Path.GetFileName(file).Split('.');
                int x = 0, z = 0;
                if (filenameInfo.Length == 4 &&
                    filenameInfo[0] == "r" &&
                    int.TryParse(filenameInfo[1], out x) &&
                    int.TryParse(filenameInfo[2], out z) &&
                    filenameInfo[3] == "mca") {
                    maxX = Math.Max(x, maxX);
                    maxZ = Math.Max(z, maxZ);
                }
            }

            int worldWidth = ((maxX + 1) << _bm.ChunkXLog) << RegionChunkManager.REGION_XLOG;
            int worldHeight = ((maxZ + 1) << _bm.ChunkZLog) << RegionChunkManager.REGION_ZLOG;

            _conv = new PositionConverter(worldWidth, worldHeight, conversions);
            _reader = new OsmReader(osmXml);
        }

        public void MapIt() {
            _world.Save();
            DateTime startTime = DateTime.Now;
            _reader.ReadData();
            GC.Collect();
            DateTime dataProcessing = DateTime.Now;
            var residential = new CsvReader(new StreamReader("residential.csv"));
            ReadStories(residential, "Stories");
            var commercial = new CsvReader(new StreamReader("commercial.csv"));
            ReadStories(commercial, "NbrStories");
            _reader.BuildingsByAddress.Clear();
            try {
                Console.WriteLine("{0} roads", _reader.Ways.Count);

                var roadPoints = DrawRoads();

                DrawSigns(roadPoints);

                var buildingPoints = DrawBuildings();
                _world.Save();

                DrawBarriers(buildingPoints);

                _world.Save();
            } catch (Exception e) {
                _world.Save();
                Console.WriteLine(e);
            }
            DateTime done = DateTime.Now;
            Console.WriteLine("All done, data processing {0}, total {1}", dataProcessing - startTime, done - startTime);
            Console.ReadLine();
        }

        private void DrawSigns(Dictionary<BlockPosition, RoadPoint> roadPoints) {
            int cur = 0;
            foreach (var sign in _reader.Signs.OrderBy(x => MapOrder(_conv, x.Key))) {
                if ((++cur % 100) == 0) {
                    _world.SaveBlocks();
                }

                if (sign.Value == OsmReader.SignType.StreetLamp) {
                    var pos = _conv.ToBlock(sign.Key.Lat, sign.Key.Long);
                    Direction dir = Direction.East;
                    for (int i = 0; i < 10; i++) {
                        if (roadPoints.ContainsKey(new BlockPosition(pos.X - 1, pos.Z))) {
                            dir = Direction.East;
                        } else if (roadPoints.ContainsKey(new BlockPosition(pos.X + 1, pos.Z))) {
                            dir = Direction.West;
                        } else if (roadPoints.ContainsKey(new BlockPosition(pos.X, pos.Z - 1))) {
                            dir = Direction.South;
                        } else if (roadPoints.ContainsKey(new BlockPosition(pos.X, pos.Z + 1))) {
                            dir = Direction.North;
                        }
                    }
                    Console.WriteLine("Lamp at {0} facing {1}", pos, dir);
                    DrawStreetLamp(dir, pos);
                }
            }
        }

        private void DrawBarriers(HashSet<BlockPosition> buildingPositions) {
            int cur = 0;
            foreach (var barrier in _reader.Barriers.OrderBy(x => MapOrder(_conv, x.Nodes.First()))) {
                if ((++cur % 100) == 0) {
                    _world.SaveBlocks();
                }
                int blockKind = 0, height = 1;
                switch (barrier.Kind) {
                    case OsmReader.BarrierKind.Fence: blockKind = BlockType.FENCE; break;
                    case OsmReader.BarrierKind.Gate: blockKind = BlockType.FENCE_GATE; break;
                    case OsmReader.BarrierKind.GuardRail: blockKind = BlockType.NETHER_BRICK_FENCE; break;
                    case OsmReader.BarrierKind.Hedge: blockKind = BlockType.LEAVES; height = 2; break;
                    case OsmReader.BarrierKind.RetainingWall: blockKind = BlockType.STONE_BRICK; height = 2; break;
                    case OsmReader.BarrierKind.Wall: blockKind = BlockType.STONE; height = 2; break;
                }

                var start = barrier.Nodes[0];
                for (int i = 1; i < barrier.Nodes.Length; i++) {
                    var from = _conv.ToBlock(start.Lat, start.Long);
                    Console.WriteLine("Barrier {0} {1}", from, barrier.Kind);
                    var to = _conv.ToBlock(barrier.Nodes[i].Lat, barrier.Nodes[i].Long);

                    foreach (var point in PlotLine(from.X, from.Z, to.X, to.Z)) {
                        if (buildingPositions.Contains(point.Block)) {
                            continue;
                        }

                        var curHeight = _bm.GetHeight(point.Block.X, point.Block.Z);
                        for (int y = 0; y < height; y++) {
                            _bm.SetID(point.Block.X, curHeight + y, point.Block.Z, blockKind);
                            _bm.SetData(point.Block.X, curHeight + y, point.Block.Z, 0);
                        }
                    }

                    start = barrier.Nodes[i];
                }
            }
        }

        static int Main(string[] args) {
            //string map = @"C:\Users\dino_\AppData\Roaming\.minecraft\saves\SeattleCentralDistrictCreative2";
            string map = @"C:\Users\dino_\AppData\Roaming\.minecraft\saves\LIDAR_DEM1_resized_landexpanded6";
            //string map = @"C:\Users\dino_\AppData\Roaming\.minecraft\saves\SeattleCreativeLidar4_2";
            string osmData = @"C:\Users\dino_\Downloads\map_seattle.xml";
            List<long> buildings = new List<long>();
            List<long> roads = new List<long>();
            List<PositionBase> positionBases = new List<PositionBase>();
            foreach (var arg in args) {
                if (arg == "/?" || arg == "/help") {
                    Help();
                    return 0;
                } else if (arg.StartsWith("/map:")) {
                    map = map.Substring("/map:".Length);
                } else if (arg.StartsWith("/osm:")) {
                    osmData = osmData.Substring("/osm:".Length);
                } else if (arg.StartsWith("/building:")) {
                    long building;
                    if (!long.TryParse(arg.Substring("/building:".Length), out building)) {
                        Console.WriteLine("Bad building ID: " + arg.Substring("/building:".Length));
                        return 1;
                    }
                    buildings.Add(building);
                } else if (arg.StartsWith("/road:")) {
                    long road;
                    if (!long.TryParse(arg.Substring("/road:".Length), out road)) {
                        Console.WriteLine("Bad road ID: " + arg.Substring("/road:".Length));
                        return 1;
                    }
                    roads.Add(road);
                } else if (map.StartsWith("/pos:")) {
                    var coords = map.Substring("/pos:".Length).Split(',');
                    double lat, lng;
                    int mcX, mcZ;
                    if (coords.Length != 4 ||
                        !double.TryParse(coords[0], out lat) ||
                        !double.TryParse(coords[1], out lng) ||
                        !int.TryParse(coords[2], out mcX) ||
                        !int.TryParse(coords[3], out mcZ)) {
                        Console.WriteLine("Expected /pos:<lat>,<long>,X,Y");
                        return 1;
                    }
                    positionBases.Add(new PositionBase(lat, lng, mcX, mcZ));
                }
            }

            var seattleCD = new[] {
                new PositionBase(47.6227923, -122.2825852, 8488, 2016),
                new PositionBase(47.6061313, -122.3408919, 1244, 4878),
                new PositionBase(47.5994269, -122.2865417, 7868, 6478),
            };  // SeattleCentralDistrictCreative
            var seattle = new[] {
                new PositionBase(47.68489, -122.33745, 1877, 1412),
                new PositionBase(47.6073105, -122.3420636, 1092, 15572),
                new PositionBase(47.6493244, -122.2757343, 9380, 8023),
                new PositionBase(47.5859511, -122.2863789, 7917, 19462),
                new PositionBase(47.6068780, -122.2987088, 6446, 15665)
            };

            if (positionBases.Count == 0) {
                positionBases.AddRange(seattle);
            }

            var mapper = new Mapper(map, osmData, positionBases.ToArray(), buildings.ToArray(), roads.ToArray());
            mapper.MapIt();
            return 0;
        }

        private static void Help() {
            Console.WriteLine("MCMap: ");
            Console.WriteLine(" /help                         - This help");
            Console.WriteLine(" /map:<map>                    - Specifies path to Minecraft Java Edition save directory");
            Console.WriteLine(" /osm:<osm.xml>                - Specifies path to Open Street Map .xml file");
            Console.WriteLine(" /pos:<lat>,<long>,<mcX>,<mcZ> - Provides mapping between lat/long and Minecraft X/Z, multiple are allowed");
            Console.WriteLine(" /building:<osmWay>            - Limit buildings to be drawn, multiple can be specified");
            Console.WriteLine(" /road:<osmWay>                - Limit roads to be drawn, multiple can be specified");
        }

        enum Direction {
            North = 8,
            South = 0,
            East = 12,
            West = 4
        }

        private void DrawStreetLamp(Direction direction, BlockPosition center) {
            var height = _bm.GetHeight(center.X, center.Z);
            const int LampHeight = 8, Length = 3;
            for (int i = 0; i < LampHeight; i++) {
                _bm.SetID(center.X, height + i, center.Z, BlockType.COBBLESTONE_WALL);
                _bm.SetData(center.X, height + i, center.Z, 0);
            }

            for (int i = 0; i < Length; i++) {
                int x = center.X, z = center.Z;
                switch (direction) {
                    case Direction.East: x += i; break;
                    case Direction.West: x -= i; break;
                    case Direction.North: z -= i; break;
                    case Direction.South: z += i; break;
                }

                _bm.SetID(x, height + LampHeight, z, BlockType.STONE_SLAB);
                _bm.SetData(x, height + LampHeight, z, 0);
                if (i == Length - 1) {
                    _bm.SetID(x, height + LampHeight - 1, z, BlockType.SEA_LANTERN);
                    _bm.SetData(x, height + LampHeight - 1, z, 0);
                }
            }
        }


        private void DrawBusStop(Direction direction, BlockPosition center) {
            if (center.X < 5 || center.Z < 5) {
                return;
            }
            // draw sides
            if (direction == Direction.South || direction == Direction.North) {
                // level things out...
                var maxHeight = 0;
                for (int z = 0; z < 3; z++) {
                    var zLoc = direction == Direction.North ? center.Z - z : center.Z + z;
                    for (int x = -2; x <= 2; x++) {
                        maxHeight = Math.Max(maxHeight, _bm.GetHeight(center.X + x, zLoc));
                    }
                }
                for (int z = 0; z < 3; z++) {
                    var zLoc = direction == Direction.North ? center.Z - z : center.Z + z;
                    for (int x = -2; x <= 2; x++) {
                        for (int i = _bm.GetHeight(center.X + x, zLoc); i < maxHeight; i++) {
                            _bm.SetID(center.X + x, _bm.GetHeight(center.X + x, zLoc), zLoc, BlockType.GRASS);
                            _bm.SetData(center.X + x, _bm.GetHeight(center.X + x, zLoc), zLoc, 0);
                        }
                    }
                }
                var height = maxHeight;
                // draw sides
                for (int z = 0; z < 3; z++) {
                    var zLoc = direction == Direction.North ? center.Z - z : center.Z + z;
                    for (int y = 0; y < 3; y++) {
                        if (y == 0) {
                            _bm.SetID(center.X - 2, height + y, zLoc, BlockType.STAINED_GLASS_PANE);
                            _bm.SetData(center.X - 2, height + y, zLoc, 11); // blue
                            _bm.SetID(center.X + 2, height + y, zLoc, BlockType.STAINED_GLASS_PANE);
                            _bm.SetData(center.X + 2, height + y, zLoc, 11); // blue
                        } else {
                            _bm.SetID(center.X - 2, height + y, zLoc, BlockType.GLASS_PANE);
                            _bm.SetData(center.X - 2, height + y, zLoc, 0);
                            _bm.SetID(center.X + 2, height + y, zLoc, BlockType.GLASS_PANE);
                            _bm.SetData(center.X + 2, height + y, zLoc, 0);
                        }
                    }
                    // draw top
                    for (int x = -2; x <= 2; x++) {
                        _bm.SetID(center.X + x, height + 3, zLoc, BlockType.WOOD_SLAB);
                        _bm.SetData(center.X + x, height + 3, zLoc, 4); // acacia
                    }
                }
                // draw back
                for (int x = -2; x < 2; x++) {
                    for (int y = 0; y < 3; y++) {
                        if (y == 0) {
                            _bm.SetID(center.X + x, height + y, center.Z, BlockType.STAINED_GLASS_PANE);
                            _bm.SetData(center.X + x, height + y, center.Z, 11); // blue
                        } else {
                            _bm.SetID(center.X + x, height + y, center.Z, BlockType.GLASS_PANE);
                            _bm.SetData(center.X + x, height + y, center.Z, 0);
                        }
                    }
                }
            } else {
                var maxHeight = 0;
                for (int x = 0; x < 3; x++) {
                    var xLoc = direction == Direction.West ? center.X - x : center.X + x;
                    for (int z = -2; z <= 2; z++) {
                        maxHeight = Math.Max(maxHeight, _bm.GetHeight(xLoc, center.Z + z));
                    }
                }
                for (int x = 0; x < 3; x++) {
                    var xLoc = direction == Direction.West ? center.X - x : center.X + x;
                    for (int z = -2; z <= 2; z++) {
                        for (int i = _bm.GetHeight(xLoc, center.Z + z); i < maxHeight; i++) {
                            _bm.SetID(xLoc, i, center.Z + z, BlockType.GRASS);
                            _bm.SetData(xLoc, i, center.Z + z, 0);
                        }
                    }
                }

                var height = maxHeight;
                // draw sides
                for (int x = 0; x < 3; x++) {
                    var xLoc = direction == Direction.West ? center.X - x : center.X + x;
                    for (int y = 0; y < 3; y++) {
                        if (y == 0) {
                            _bm.SetID(xLoc, height + y, center.Z - 2, BlockType.STAINED_GLASS_PANE);
                            _bm.SetData(xLoc, height + y, center.Z - 2, 11); // blue
                            _bm.SetID(xLoc, height + y, center.Z + 2, BlockType.STAINED_GLASS_PANE);
                            _bm.SetData(xLoc, height + y, center.Z + 2, 11); // blue
                        } else {
                            _bm.SetID(xLoc, height + y, center.Z - 2, BlockType.GLASS_PANE);
                            _bm.SetData(xLoc, height + y, center.Z - 2, 0);
                            _bm.SetID(xLoc, height + y, center.Z + 2, BlockType.GLASS_PANE);
                            _bm.SetData(xLoc, height + y, center.Z + 2, 0);
                        }
                    }
                    // draw top
                    for (int z = -2; z <= 2; z++) {
                        _bm.SetID(xLoc, height + 3, center.Z + z, BlockType.WOOD_SLAB);
                        _bm.SetData(xLoc, height + 3, center.Z + z, 4); // acacia
                    }
                }
                // draw back
                for (int z = -2; z < 2; z++) {
                    for (int y = 0; y < 3; y++) {
                        if (y == 0) {
                            _bm.SetID(center.X, height + y, center.Z + z, BlockType.STAINED_GLASS_PANE);
                            _bm.SetData(center.X, height + y, center.Z + z, 11); // blue
                        } else {
                            _bm.SetID(center.X, height + y, center.Z + z, BlockType.GLASS_PANE);
                            _bm.SetData(center.X, height + y, center.Z + z, 0);
                        }
                    }
                }
            }
        }

        private HashSet<BlockPosition> DrawBuildings() {
            int cur = 0;
            HashSet<BlockPosition> buildingPoints = new HashSet<BlockPosition>();
            IEnumerable<KeyValuePair<long, OsmReader.Building>> buildingList;
            if (_buildings.Length != 0) {
                buildingList = _reader.Buildings.Where(x => _buildings.Contains(x.Key));
            } else {
                buildingList = _reader.Buildings.OrderBy(x => MapOrder(_conv, x.Value.Nodes.First()));
            }
            // new[] { reader.Buildings[224047836], reader.Buildings[224048580], reader.Buildings[222409095], reader.Buildings[222409094] }
            //var buildings = new long[]{ 363250260, 4856210822, 4856214545 /*224047836, 224048580, 222409095, 222409094*/ };
            //var buildingList = _reader.Buildings.Where(x => buildings.Contains(x.Key));
            foreach (var idAndBuilding in buildingList) {
                var building = idAndBuilding.Value;
                if ((++cur % 200) == 0) {
                    _world.SaveBlocks();
                }

                Console.WriteLine("{0} {1} ({2}/{3})", building.HouseNumber, building.Street, cur, _reader.Buildings.Count);
                DrawBuilding(building, buildingPoints, (int)(idAndBuilding.Key % 16));
            }

            _world.SaveBlocks();
            return buildingPoints;
        }

        private void DrawBuilding(OsmReader.Building building, HashSet<BlockPosition> buildingPoints, int color) {
            int blockType = BlockType.STAINED_CLAY;
            int data;
            if (building.Street != null && building.HouseNumber != null) {
                data = (building.Street.GetHashCode() ^ building.HouseNumber.GetHashCode()) % 16;
                switch (building.Amenity) {
                    //case OsmReader.Amenity.Restaurant: blockType = BlockType;
                    case OsmReader.Amenity.TrainStation: blockType = BlockType.BRICK_BLOCK; break;
                    case OsmReader.Amenity.FireStation: blockType = BlockType.BRICK_BLOCK; break;
                    case OsmReader.Amenity.School: blockType = BlockType.BRICK_BLOCK; break;
                    case OsmReader.Amenity.Parking:
                        blockType = BlockType.STONE;
                        data = (int)StoneBrickType.NORMAL;
                        break;
                    case OsmReader.Amenity.Bank: blockType = BlockType.SANDSTONE; break;
                    case OsmReader.Amenity.PlaceOfWorship: blockType = BlockType.NETHER_BRICK; break;
                    case OsmReader.Amenity.CommunityCenter:
                        blockType = BlockType.OBSIDIAN;
                        break;
                    case OsmReader.Amenity.Theatre: blockType = BlockType.WOOD; data = (int)WoodType.BIRCH; break;
                }
            } else {
                data = color;
            }

            int buildingHeight = Math.Min(4, (int)((building.Stories ?? 1) * 4));
            int maxHeight = Int32.MinValue;
            var start = building.Nodes[0];
            var houseLoc = _conv.ToBlock(start.Lat, start.Long);
            for (int i = 1; i < building.Nodes.Length; i++) {
                var from = _conv.ToBlock(start.Lat, start.Long);
                var to = _conv.ToBlock(building.Nodes[i].Lat, building.Nodes[i].Long);

                foreach (var point in PlotLine(from.X, from.Z, to.X, to.Z)) {
                    if (_conv.IsValidPoint(point.Block)) {
                        var height = _bm.GetHeight(point.Block.X, point.Block.Z);
                        maxHeight = Math.Max(maxHeight, height);
                    }
                }
            }

            start = building.Nodes[0];
            for (int i = 1; i < building.Nodes.Length; i++) {
                var from = _conv.ToBlock(start.Lat, start.Long);
                var to = _conv.ToBlock(building.Nodes[i].Lat, building.Nodes[i].Long);

                foreach (var point in PlotLine(from.X, from.Z, to.X, to.Z)) {
                    if (_conv.IsValidPoint(point.Block)) {
                        // if we have duplicate entries for a building don't draw it twice...
                        if (buildingPoints.Contains(point.Block)) {
                            continue;
                        }
                        buildingPoints.Add(point.Block);

                        var height = _bm.GetHeight(point.Block.X, point.Block.Z);
                        //Console.WriteLine("{0},{1},{2}", point.X, height, point.Z);

                        for (int j = 0; j < (maxHeight - height) + buildingHeight; j++) {
                            if (height - 1 + j > 240) {
                                break;
                            }
                            _bm.SetID(point.Block.X, height - 1 + j, point.Block.Z, (int)blockType);
                            _bm.SetData(point.Block.X, height - 1 + j, point.Block.Z, data);
                        }
                    }
                }

                start = building.Nodes[i];
                //Console.WriteLine("From {0},{1} to {2},{3}", from.X, from.Z, to.X, to.Z);
            }

            int direction = 0;
            if (!string.IsNullOrWhiteSpace(building.HouseNumber) && !string.IsNullOrWhiteSpace(building.Street)) {
                List<OsmReader.Way> roads;
                BlockPosition signLoc = houseLoc;
                if (_reader.RoadsByName.TryGetValue(building.Street, out roads)) {
                    // Find the closest point between a random point on the building and the road...
                    var first = building.Nodes.First();

                    var points = roads.SelectMany(x => x.Nodes);
                    OsmReader.Node closestRoadPoint = ClosestPoint(first, points);

                    var roadLoc = _conv.ToBlock(closestRoadPoint.Lat, closestRoadPoint.Long);
                    // And then find the closest building point to the closest road point
                    //OsmReader.Node closestBuildingPoint = ClosestPoint(closestRoadPoint, building.Nodes);

                    // Figure out whre the sign is placed.  This can be a primary point from a single node
                    // where it would be placed on an overhead map, or we'll find the closest point between
                    // the building and a road point.
                    OsmReader.Node signPoint = null, buildingWallPoint = null;
                    if (building.PrimaryLat != null && building.PrimaryLong != null) {
                        signPoint = new OsmReader.Node(building.PrimaryLat.Value, building.PrimaryLong.Value);
                        double distance = double.MaxValue;
                        var prevBuildingPoint = building.Nodes.Last();
                        buildingWallPoint = building.Nodes.First();
                        foreach (var buildingPoint in building.Nodes) {
                            var angle1 = Math.Atan2(prevBuildingPoint.Lat - buildingPoint.Lat, prevBuildingPoint.Long - buildingPoint.Long) * 180 / Math.PI;
                            var angle2 = Math.Atan2(signPoint.Lat - buildingPoint.Lat, signPoint.Long - buildingPoint.Long) * 180 / Math.PI;
                            if (Math.Abs(angle1 - angle2) < 15 ||
                                (Math.Abs(angle1 - angle2) > (180 - 7.5) && Math.Abs(angle1 - angle2) < (180 + 7.5)) ||
                                (Math.Abs(angle1 - angle2) > (360 - 11) && Math.Abs(angle1 - angle2) < (360 + 11))) {
                                // If we have something like:
                                //      +---------------------------------------------------------------+
                                //      |                                                               |
                                // +----+                                        *                      |
                                // |                                                                    |
                                // The node at the * can end up being reported as being close the horizontal
                                // line, so we do a specific check to avoid that and discount angles that
                                // we're aligned with.
                                continue;
                            }
                            var pointOnBuilding = FindPerpindicualPoint(signPoint, prevBuildingPoint, buildingPoint);
                            double newDistance = Math.Sqrt(
                                (signPoint.Lat - pointOnBuilding.Lat) * (signPoint.Lat - pointOnBuilding.Lat) +
                                (signPoint.Long - pointOnBuilding.Long) * (signPoint.Long - pointOnBuilding.Long)
                            );
                            //var prevMC = _conv.ToBlock(prevBuildingPoint.Lat, prevBuildingPoint.Long);
                            //var buildingMC = _conv.ToBlock(buildingPoint.Lat, buildingPoint.Long);
                            //var buildingPerpMC = _conv.ToBlock(pointOnBuilding.Lat, pointOnBuilding.Long);
                            if (newDistance < distance) {
                                buildingWallPoint = pointOnBuilding;
                                distance = newDistance;
                            }
                            prevBuildingPoint = buildingPoint;
                        }
                    } else {
                        double distance = double.MaxValue;
                        foreach (var buildingPoint in building.Nodes) {
                            foreach (var roadPoint in points) {
                                double newDistance = Math.Sqrt(
                                    (buildingPoint.Lat - roadPoint.Lat) * (buildingPoint.Lat - roadPoint.Lat) +
                                    (buildingPoint.Long - roadPoint.Long) * (buildingPoint.Long - roadPoint.Long)
                                );
                                if (newDistance < distance) {
                                    signPoint = buildingPoint;
                                    distance = newDistance;
                                }
                            }
                        }
                        buildingWallPoint = signPoint;
                    }

                    OsmReader.Node comparison = NextClosestPoint(signPoint, closestRoadPoint, points);

                    // Figure out the direction of the house vs the road...
                    var pointOnRoad = FindPerpindicualPoint(signPoint, comparison, closestRoadPoint);
                    var angle = Math.Atan2(signPoint.Lat - pointOnRoad.Lat, signPoint.Long - pointOnRoad.Long) * 180 / Math.PI;
                    var buildingLoc = _conv.ToBlock(buildingWallPoint.Lat, buildingWallPoint.Long);
                    var signMC = _conv.ToBlock(signPoint.Lat, signPoint.Long);
                    if ((angle > -45 && angle < 45) || angle > 135 || angle < -135) {
                        if (closestRoadPoint.Long > signPoint.Long) { // this comparison is backwards due to negative longitude in Seattle
                            signLoc = new BlockPosition(buildingLoc.X + 1, buildingLoc.Z);
                            direction = 12; // east
                        } else {
                            signLoc = new BlockPosition(buildingLoc.X - 1, buildingLoc.Z);
                            direction = 4; // west
                        }
                    } else {
                        if (closestRoadPoint.Lat < signPoint.Lat) {
                            signLoc = new BlockPosition(buildingLoc.X, buildingLoc.Z + 1);
                            direction = 0;// south
                        } else {
                            signLoc = new BlockPosition(buildingLoc.X, buildingLoc.Z - 1);
                            direction = 8; // north
                        }
                    }
                }

                List<string> names = new List<string>();
                AddSignName(names, building.Name);

                AlphaBlock block = new AlphaBlock(BlockType.SIGN_POST);
                var ent = block.GetTileEntity() as TileEntitySign;
                AddSignName(names, building.HouseNumber);
                SetSignName(names, ent);
                var houseHeight = _bm.GetHeight(signLoc.X, signLoc.Z);
                _bm.SetBlock(signLoc.X, houseHeight, signLoc.Z, block);
                _bm.SetData(signLoc.X, houseHeight, signLoc.Z, direction);
            }
        }

        private static OsmReader.Node NextClosestPoint(OsmReader.Node from, OsmReader.Node closestPoint, IEnumerable<OsmReader.Node> points) {
            OsmReader.Node prev = null, next = null;
            var enumerator = points.GetEnumerator();
            while (enumerator.MoveNext()) {
                if (enumerator.Current == closestPoint) {
                    if (enumerator.MoveNext()) {
                        next = enumerator.Current;
                    }
                    break;
                }
                prev = enumerator.Current;
            }
            OsmReader.Node comparison = null;
            if (prev != null && next != null) {
                comparison = ClosestPoint(from, new[] { prev, next });
            } else if (prev != null) {
                comparison = prev;
            } else {
                comparison = next;
            }

            return comparison;
        }

        struct SegmentIntersection : IEquatable<SegmentIntersection> {
            public readonly RoadSegment A, B;

            public SegmentIntersection(RoadSegment a, RoadSegment b) {
                A = a;
                B = b;
            }

            public override int GetHashCode() {
                return A.GetHashCode() ^ B.GetHashCode();
            }

            public bool Equals(SegmentIntersection other) {
                return (A.Equals(other.A) && B.Equals(other.B)) ||
                    (A.Equals(other.B) && B.Equals(other.A));
            }

            public override bool Equals(object obj) {
                if (obj is SegmentIntersection) {
                    return Equals((SegmentIntersection)obj);
                }
                return false;
            }
        }

        struct RoadIntersection : IEquatable<RoadIntersection> {
            public readonly OsmReader.Way A, B;

            public RoadIntersection(OsmReader.Way a, OsmReader.Way b) {
                A = a;
                B = b;
            }

            public bool IsSegmentFromRoadA(SegmentIntersection segment) {
                if (A.Name != null && B.Name != null) {
                    return segment.A.Way.Name == A.Name;
                }
                return A == segment.A.Way;
            }


            public override int GetHashCode() {
                if (A.Name != null && B.Name != null) {
                    return A.Name.GetHashCode() ^ B.Name.GetHashCode();
                }
                return A.GetHashCode() ^ B.GetHashCode();
            }

            public bool Equals(RoadIntersection other) {
                if (A.Name != null && B.Name != null) {
                    return (A.Name.Equals(other.A.Name) && B.Name.Equals(other.B.Name)) ||
                        (A.Name.Equals(other.B.Name) && B.Name.Equals(other.A.Name));
                }
                return (A.Equals(other.A) && B.Equals(other.B)) ||
                    (A.Equals(other.B) && B.Equals(other.A));
            }

            public override bool Equals(object obj) {
                if (obj is RoadIntersection) {
                    return Equals((RoadIntersection)obj);
                }
                return false;
            }
        }

        class RoadSegment : IEquatable<RoadSegment> {
            public readonly OsmReader.Way Way;
            public readonly OsmReader.Node Start, End;

            public RoadSegment(OsmReader.Way way, OsmReader.Node start, OsmReader.Node end) {
                Start = start;
                End = end;
                Way = way;
            }

            public override int GetHashCode() {
                return Way.GetHashCode() ^ Start.GetHashCode() ^ End.GetHashCode();
            }

            public override bool Equals(object obj) {
                if (obj is RoadSegment) {
                    return Equals((RoadSegment)obj);
                }
                return false;
            }

            public bool Equals(RoadSegment other) {
                return Start == other.Start &&
                    End == other.End &&
                    Way == other.Way;
            }
        }

        private static int MapOrder(PositionConverter conv, OsmReader.Node node) {
            var position = conv.ToBlock(node.Lat, node.Long);
            return (position.Z >> 5) * 100 + position.X >> 5;
        }

        private Dictionary<BlockPosition, RoadPoint> DrawRoads() {
            int cur = 0;
            // We group the roads by their name here, and then order them by a position in the road.  The grouping
            // by name ensures that we process all of the road segments, which may be split out as multiple ways,
            // at once.  Once a road is entirely processed we'll process it's intersections and draw them on.
            // We order by a position on the map to handle very large maps - this allows us to be reading/writing
            // in approximately the same area and to avoid lots of additional reads/writes.
            IEnumerable<IGrouping<string, OsmReader.Way>> roads;
            if (_roads.Length != 0) {
                roads = _reader.Ways.Where(x => _roads.Contains(x.Key)).Select(x => x.Value).GroupBy(x => x.Name ?? "").OrderBy(x => MapOrder(_conv, x.First().Nodes.First()));
            } else {
                roads = _reader.Ways.Values.GroupBy(x => x.Name ?? "").OrderBy(x => MapOrder(_conv, x.First().Nodes.First()));
            }
            var roadPoints = new Dictionary<BlockPosition, RoadPoint>();
            var intersections = new Dictionary<RoadIntersection, Dictionary<SegmentIntersection, List<BlockPosition>>>();
            //var intersections = new Dictionary<RoadIntersection, List<BlockPosition>>();
            //var roads = new[] { reader.Ways[6358491], reader.Ways[6358495], reader.Ways[158718178], reader.Ways[241283900], reader.Ways[6433374] };
            foreach (var roadGroup in roads) {
                foreach (var way in roadGroup) {
                    if ((++cur % 100) == 0) {
                        _world.SaveBlocks();
                    }

                    if (way.RoadType != OsmReader.RoadType.Service) {
                        if (way.Name == null ||
                            (way.RoadType != OsmReader.RoadType.Motorway &&
                            way.RoadType != OsmReader.RoadType.Residential &&
                            way.RoadType != OsmReader.RoadType.FootWay &&
                            way.RoadType != OsmReader.RoadType.Primary &&
                            way.RoadType != OsmReader.RoadType.Secondary &&
                            way.RoadType != OsmReader.RoadType.Path &&
                            way.RoadType != OsmReader.RoadType.Trunk) ||
                            way.Layer != null) {
                            continue;
                        }
                    }

                    Console.WriteLine("{0} ({1}/{2})", way.Name, cur, _reader.Ways.Count);
                    var style = GetRoadStyle(way);
                    if (style != null) {
                        style.Render(roadPoints, intersections);
                    }
                }

                List<OsmReader.Node> busStops;
                if (_reader.BusStops.TryGetValue(roadGroup.Key, out busStops)) {
                    Console.WriteLine("Adding {0} bus stops", busStops.Count);
                    foreach (var stop in busStops) {

                        var closest = ClosestPoint(stop, roadGroup.SelectMany(x => x.Nodes));
                        var nextClosest = NextClosestPoint(stop, closest, roadGroup.SelectMany(x => x.Nodes));

                        var pointOnRoad = FindPerpindicualPoint(stop, closest, nextClosest);
                        var angle = Math.Atan2(stop.Lat - pointOnRoad.Lat, stop.Long - pointOnRoad.Long) * 180 / Math.PI;

                        if ((angle > -45 && angle < 45) || angle > 135 || angle < -135) {
                            if (pointOnRoad.Long > stop.Long) { // this comparison is backwards due to negative longitude in Seattle
                                DrawBusStop(Direction.East, _conv.ToBlock(stop.Lat, stop.Long));
                            } else {
                                var loc = _conv.ToBlock(stop.Lat, stop.Long);
                                loc = new BlockPosition(loc.X + 2, loc.Z);
                                DrawBusStop(Direction.West, _conv.ToBlock(stop.Lat, stop.Long));
                            }
                        } else {
                            if (pointOnRoad.Lat < stop.Lat) {
                                DrawBusStop(Direction.South, _conv.ToBlock(stop.Lat, stop.Long));
                            } else {
                                var loc = _conv.ToBlock(stop.Lat, stop.Long);
                                loc = new BlockPosition(loc.X, loc.Z + 2);
                                DrawBusStop(Direction.North, loc);
                            }
                        }
                    }
                }
                _reader.BusStops.Remove(roadGroup.Key);

                DrawIntersections(roadPoints, intersections);
                intersections.Clear();
            }

            foreach (var roadGroup in roads) {
                foreach (var way in roadGroup) {
                    Console.WriteLine("Zebra {0}", _conv.ToBlock(way.Nodes[0].Lat, way.Nodes[0].Long));
                    if (way.Crossing == OsmReader.CrossingType.Zebra) {
                        BlockPosition[] blockPositions;
                        if (way.Nodes.Length > 1) {
                            blockPositions = way.Nodes.Select(x => _conv.ToBlock(x.Lat, x.Long)).ToArray();
                        } else {
                            Console.WriteLine("Single point zebra");
                            // we have a single point for a zebra, if it's on a road, we'll render it...
                            var pos = _conv.ToBlock(way.Nodes[0].Lat, way.Nodes[0].Long);
                            RoadPoint zebraPoint;
                            if (!roadPoints.TryGetValue(pos, out zebraPoint)) {
                                continue;
                            }

                            // we create a line across the road to attempt to draw the zebra on from...
                            var angle = Math.Atan2(
                                zebraPoint.Segment.Start.Lat - zebraPoint.Segment.End.Lat,
                                zebraPoint.Segment.Start.Long - zebraPoint.Segment.End.Long
                            );

                            int width = GetRoadStyle(zebraPoint.Segment.Way).Width;
                            blockPositions = new[] {
                                new BlockPosition((int)(Math.Cos(angle)*width + pos.X), (int)(Math.Sin(angle)*width + pos.Z)),
                                new BlockPosition((int)(-Math.Cos(angle)*width + pos.X), (int)(-Math.Sin(angle)*width + pos.Z))
                            };
                        }

                        var from = blockPositions[0];
                        for (int i = 1; i < blockPositions.Length; i++) {
                            var to = blockPositions[i];

                            foreach (var point in PlotLine(from.X, from.Z, to.X, to.Z, 5)) {
                                if (roadPoints.ContainsKey(point.Block)) {
                                    if ((point.Row & 0x01) != 0) {
                                        var height = _bm.GetHeight(point.Block.X, point.Block.Z);
                                        _bm.SetID(point.Block.X, height - 1, point.Block.Z, BlockType.QUARTZ_BLOCK);
                                        _bm.SetData(point.Block.X, height - 1, point.Block.Z, 0);
                                    }
                                }
                            }
                        }

                        if ((++cur % 100) == 0) {
                            _world.SaveBlocks();
                        }
                    }
                }
            }
            _world.SaveBlocks();
            return roadPoints;
        }

        struct RoadPoint {
            public readonly RoadSegment Segment;
            public int Height;

            public RoadPoint(RoadSegment segment, int height) {
                Segment = segment;
                Height = height;
            }
        }

        class RoadRenderer {
            public readonly int Id, Data, Width;
            private readonly int? _leftEdgeId, _rightEdgeId;
            protected readonly PositionConverter _conv;
            protected readonly AnvilBlockManager _bm;
            public readonly OsmReader.Way Way;


            public RoadRenderer(PositionConverter conv, AnvilBlockManager bm, OsmReader.Way way, int width, int id, int data, int? leftEdgeId = null, int? rightEdgeId = null) {
                _conv = conv;
                _bm = bm;
                Way = way;
                Width = width;
                Id = id;
                Data = data;
                _leftEdgeId = leftEdgeId;
                _rightEdgeId = rightEdgeId;
            }

            private const int SidewalkWidth = 3;

            public void Render(Dictionary<BlockPosition, RoadPoint> roadPoints, Dictionary<RoadIntersection, Dictionary<SegmentIntersection, List<BlockPosition>>> intersections) {
                var start = Way.Nodes[0];
                int targetWidth = Width;
                if (_leftEdgeId != null) {
                    targetWidth += SidewalkWidth;
                }
                if (_rightEdgeId != null) {
                    targetWidth += SidewalkWidth;
                }
                for (int i = 1; i < Way.Nodes.Length; i++) {
                    var from = _conv.ToBlock(start.Lat, start.Long);
                    var to = _conv.ToBlock(Way.Nodes[i].Lat, Way.Nodes[i].Long);
                    var curSegment = new RoadSegment(Way, start, Way.Nodes[i]);

                    foreach (var point in PlotLine(from.X, from.Z, to.X, to.Z, targetWidth)) {
                        if (_conv.IsValidPoint(point.Block) && point.Block.X > 1 && point.Block.Z > 1) {
                            RoadPoint existingRoad;
                            bool isIntersection = roadPoints.TryGetValue(point.Block, out existingRoad);
                            if (isIntersection) {
                                if (existingRoad.Segment.Way == Way ||
                                    (existingRoad.Segment.Way.Name != null && existingRoad.Segment.Way.Name == Way.Name)) {
                                    continue;
                                }

                                var curIntersection = new SegmentIntersection(curSegment, existingRoad.Segment);
                                Dictionary<SegmentIntersection, List<BlockPosition>> segmentPoints;
                                List<BlockPosition> intersectionPoints;
                                var intersect = new RoadIntersection(Way, existingRoad.Segment.Way);
                                if (!intersections.TryGetValue(intersect, out segmentPoints)) {
                                    segmentPoints = intersections[intersect] = new Dictionary<SegmentIntersection, List<BlockPosition>>();
                                }
                                if (!segmentPoints.TryGetValue(curIntersection, out intersectionPoints)) {
                                    intersectionPoints = segmentPoints[curIntersection] = new List<BlockPosition>();
                                }
                                intersectionPoints.Add(point.Block);

                                // wider roads take precedence over less wide roads
                                if (GetRoadWidth(existingRoad.Segment.Way) > Width) {
                                    continue;
                                }
                            }

                            int height;
                            if (isIntersection) {
                                // use the existing tracked height, in case we added a raised sidewalk,
                                // rather than the current height.
                                height = existingRoad.Height;
                            } else {
                                height = _bm.GetHeight(point.Block.X, point.Block.Z);
                            }

                            if (_leftEdgeId != null && point.Column < SidewalkWidth) {
                                if (!isIntersection) {
                                    _bm.SetID(point.Block.X, height, point.Block.Z, _leftEdgeId.Value);
                                    _bm.SetData(point.Block.X, height, point.Block.Z, 0);
                                    roadPoints[point.Block] = new RoadPoint(curSegment, height);
                                    ClearPlantLife(_bm, point.Block, height + 1);
                                }
                            } else if (_rightEdgeId != null && point.Column >= targetWidth - SidewalkWidth) {
                                if (!isIntersection) {
                                    _bm.SetID(point.Block.X, height, point.Block.Z, _rightEdgeId.Value);
                                    _bm.SetData(point.Block.X, height, point.Block.Z, 0);
                                    roadPoints[point.Block] = new RoadPoint(curSegment, height);
                                    ClearPlantLife(_bm, point.Block, height + 1);
                                }
                            } else {
                                if (DrawRoadPoint(point, height)) {
                                    roadPoints[point.Block] = new RoadPoint(curSegment, height);
                                    ClearPlantLife(_bm, point.Block, height);
                                }
                            }

                        }
                    }

                    start = Way.Nodes[i];
                    //Console.WriteLine("From {0},{1} to {2},{3}", from.X, from.Z, to.X, to.Z);
                }
            }

            public bool DrawRoadPoint(LinePosition point, int height) {
                _bm.SetID(point.Block.X, height - 1, point.Block.Z, Id);
                _bm.SetData(point.Block.X, height - 1, point.Block.Z, Data);
                return true;
            }
        }

        private static void ClearPlantLife(AnvilBlockManager bm, BlockPosition position, int height) {
            for (int i = 0; i < 3; i++) {
                var block = bm.GetID(position.X, height + i, position.Z);
                if (block != BlockType.AIR) {
                    bm.SetID(position.X, height + i, position.Z, BlockType.AIR);
                    bm.SetData(position.X, height + i, position.Z, 0);
                }
            }
        }

        private RoadRenderer GetRoadStyle(OsmReader.Way way) {
            int width = GetRoadWidth(way);
            int id = BlockType.STONE_BRICK, data = 0;
            switch (way.Surface) {
                case OsmReader.Surface.Cobblestone: id = BlockType.COBBLESTONE; break;
                case OsmReader.Surface.Asphalt: id = BlockType.CONCRETE; data = 15; break; // black concrete
                case OsmReader.Surface.Clay: id = BlockType.CLAY_BLOCK; break;
                case OsmReader.Surface.Dirt: id = BlockType.DIRT; break;
                case OsmReader.Surface.Gravel: id = BlockType.GRAVEL; break;
                case OsmReader.Surface.Metal: id = BlockType.IRON_BLOCK; break;
                case OsmReader.Surface.Stone: id = BlockType.STONE; break;
                case OsmReader.Surface.Brick: id = BlockType.BRICK_BLOCK; break;
                case OsmReader.Surface.Sand: id = BlockType.SAND; break;
                case OsmReader.Surface.Wood: id = BlockType.WOOD; break;
                case OsmReader.Surface.GravelGrass: id = BlockType.MOSS_STONE; break;
                case OsmReader.Surface.Compacted: id = BlockType.FARMLAND; break;
                case OsmReader.Surface.FineGravel: id = BlockType.GRAVEL; break;
                case OsmReader.Surface.Ground: id = BlockType.DIRT; break;
                case OsmReader.Surface.RailroadTies: id = BlockType.WOOD; break;
                case OsmReader.Surface.Concrete: id = BlockType.CONCRETE; data = 8; break; //light gray concrete
                default:
                case OsmReader.Surface.None:
                    switch (way.RoadType) {
                        case OsmReader.RoadType.Path:
                            id = BlockType.GRAVEL;
                            break;
                        case OsmReader.RoadType.Primary:
                        case OsmReader.RoadType.Secondary:
                        case OsmReader.RoadType.Trunk:
                            id = BlockType.CONCRETE; data = 8;
                            break;
                        case OsmReader.RoadType.Service:
                            width = 3;
                            id = BlockType.CONCRETE_POWDER;
                            data = 0;
                            break;
                        case OsmReader.RoadType.FootWay:
                            if (way.Crossing != OsmReader.CrossingType.None) {
                                if (way.Crossing == OsmReader.CrossingType.Zebra) {
                                    return null;
                                }
                            } else {
                                width = 2;
                                id = BlockType.SANDSTONE;
                            }
                            break;
                        case OsmReader.RoadType.Motorway:
                            width = (way.Lanes ?? 2) * 5;
                            id = BlockType.CONCRETE; data = 8;
                            break;
                        default:
                            id = BlockType.CONCRETE; data = 8;
                            break;
                    }
                    break;
            }

            int? leftEdge = null, rightEdge = null;
            switch (way.Sidewalk) {
                case OsmReader.Sidewalk.Left:
                    leftEdge = BlockType.STONE_SLAB;
                    break;
                case OsmReader.Sidewalk.Right:
                    rightEdge = BlockType.STONE_SLAB;
                    break;
                case OsmReader.Sidewalk.Both:
                    leftEdge = rightEdge = BlockType.STONE_SLAB;
                    break;
            }
            return new RoadRenderer(_conv, _bm, way, width, id, data, leftEdge, rightEdge);
        }

        private void DrawIntersections(Dictionary<BlockPosition, RoadPoint> roadPoints, Dictionary<RoadIntersection, Dictionary<SegmentIntersection, List<BlockPosition>>> intersections) {
            List<string> aName = new List<string>(4);
            List<string> bName = new List<string>(4);
            foreach (var roadIntersection in intersections.OrderBy(x => MapOrder(_conv, x.Key.A.Nodes.First()))) {
                BlockPosition topLeft = new BlockPosition(Int32.MaxValue, Int32.MaxValue),
                              topRight = new BlockPosition(Int32.MinValue, Int32.MaxValue),
                              bottomLeft = new BlockPosition(Int32.MaxValue, Int32.MinValue),
                              bottomRight = new BlockPosition(Int32.MinValue, Int32.MinValue);
                if (string.IsNullOrEmpty(roadIntersection.Key.A.Name) ||
                    string.IsNullOrEmpty(roadIntersection.Key.B.Name)) {
                    continue;
                }
                double aAngleTotal = 0, bAngleTotal = 0;
                int angleCount = 0;
                OsmReader.SignType signType = OsmReader.SignType.None;
                foreach (var segmentIntersection in roadIntersection.Value) {
                    if (signType == OsmReader.SignType.None) {
                        if (segmentIntersection.Key.A.Start == segmentIntersection.Key.B.Start ||
                            segmentIntersection.Key.A.Start == segmentIntersection.Key.B.End) {

                            if (_reader.Signs.TryGetValue(segmentIntersection.Key.A.Start, out signType)) {
                                if (signType != OsmReader.SignType.Stop) {
                                    Console.WriteLine("Sign type is {0}", signType);
                                }
                            }
                        } else if (segmentIntersection.Key.A.End == segmentIntersection.Key.B.Start ||
                             segmentIntersection.Key.A.End == segmentIntersection.Key.B.End) {
                            if (_reader.Signs.TryGetValue(segmentIntersection.Key.A.End, out signType)) {
                                if (signType != OsmReader.SignType.Stop) {
                                    Console.WriteLine("Sign type is {0}", signType);
                                }
                            }
                        }
                    }
                    // now generate the signs, we generally want to generate something like this, where
                    // the big A's and B's represent the path of the road, and the little a's and b's
                    // represent where we want to draw the signs at.  
                    //         bAAAAAAAAa
                    //BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB
                    //BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB
                    //BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB
                    //BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB
                    //BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB
                    //		   aAAAAAAAAb
                    //			AAAAAAAA
                    //			AAAAAAAA
                    //          AAAAAAAA
                    foreach (var point in segmentIntersection.Value) {
                        if (point.X <= topLeft.X && point.Z <= topLeft.Z) {
                            topLeft = point;
                        }
                        if (point.X >= topRight.X && point.Z <= topRight.Z) {
                            topRight = point;
                        }
                        if (point.X <= bottomLeft.X && point.Z >= bottomLeft.Z) {
                            bottomLeft = point;
                        }
                        if (point.X >= bottomRight.X && point.Z >= bottomRight.Z) {
                            bottomRight = point;
                        }
                    }

                    if (roadIntersection.Key.IsSegmentFromRoadA(segmentIntersection.Key)) {
                        // Now determine which street is running more north/south, and which is running more east/west
                        aAngleTotal += Math.Atan2(
                            segmentIntersection.Key.A.Start.Lat - segmentIntersection.Key.A.End.Lat,
                            segmentIntersection.Key.A.Start.Long - segmentIntersection.Key.A.End.Long
                        ) * 180 / Math.PI;
                        bAngleTotal += Math.Atan2(
                            segmentIntersection.Key.B.Start.Lat - segmentIntersection.Key.B.End.Lat,
                            segmentIntersection.Key.B.Start.Long - segmentIntersection.Key.B.End.Long
                        ) * 180 / Math.PI;
                    } else {
                        bAngleTotal += Math.Atan2(
                            segmentIntersection.Key.A.Start.Lat - segmentIntersection.Key.A.End.Lat,
                            segmentIntersection.Key.A.Start.Long - segmentIntersection.Key.A.End.Long
                        ) * 180 / Math.PI;
                        aAngleTotal += Math.Atan2(
                            segmentIntersection.Key.B.Start.Lat - segmentIntersection.Key.B.End.Lat,
                            segmentIntersection.Key.B.Start.Long - segmentIntersection.Key.B.End.Long
                        ) * 180 / Math.PI;
                    }
                    angleCount++;
                }

                aAngleTotal /= angleCount;
                bAngleTotal /= angleCount;
                aName.Clear();
                bName.Clear();
                AddSignName(aName, roadIntersection.Key.A.Name);
                AddSignName(bName, roadIntersection.Key.B.Name);
                if ((bAngleTotal > -45 && bAngleTotal < 45) || bAngleTotal > 135 || bAngleTotal < -135) {
                } else {
                    var tmpName = aName;
                    aName = bName;
                    bName = tmpName;
                }

                if (signType == OsmReader.SignType.TrafficSignal) {
                    // Traffic lights are different than simple signs...  For a simple sign we display
                    // the name of the street on the right hand side before you enter the intersection.
                    // For a traffic light we dispaly it on the opposite side of the street, extending
                    // from the right hand side.
                    //         aAAAAAAAAb
                    //BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB
                    //BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB
                    //BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB
                    //BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB
                    //BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB
                    //		   bAAAAAAAAa
                    //			AAAAAAAA
                    //			AAAAAAAA
                    //          AAAAAAAA

                    DrawTrafficLight(
                        bName,
                        GetSignNonRoadPoint(roadPoints, bottomLeft.X - 1, bottomLeft.Z + 1, Direction.East),
                        Direction.East,
                        GetNumberOfTrafficLights(roadIntersection.Key.A)
                    );
                    DrawTrafficLight(bName, GetSignNonRoadPoint(roadPoints, topRight.X + 1, topRight.Z - 1, Direction.West), Direction.West, GetNumberOfTrafficLights(roadIntersection.Key.A));
                    DrawTrafficLight(aName, GetSignNonRoadPoint(roadPoints, topLeft.X - 1, topLeft.Z - 1, Direction.South), Direction.South, GetNumberOfTrafficLights(roadIntersection.Key.B));
                    DrawTrafficLight(aName, GetSignNonRoadPoint(roadPoints, bottomRight.X + 1, bottomRight.Z + 1, Direction.North), Direction.North, GetNumberOfTrafficLights(roadIntersection.Key.B));
                } else {
                    DrawRoadSign(signType, bName, roadPoints, topLeft.X - 1, topLeft.Z - 1, Direction.North);
                    DrawRoadSign(signType, bName, roadPoints, bottomRight.X + 1, bottomRight.Z + 1, Direction.South);
                    DrawRoadSign(signType, aName, roadPoints, topRight.X + 1, topRight.Z - 1, Direction.East);
                    DrawRoadSign(signType, aName, roadPoints, bottomLeft.X - 1, bottomLeft.Z + 1, Direction.West);
                }
            }
        }

        private static int GetNumberOfTrafficLights(OsmReader.Way way) {
            if (way.OneWay) {
                return way.Lanes ?? 1;
            }
            return (way.Lanes ?? 2) / 2;
        }

        private void DrawRoadSign(OsmReader.SignType signType, List<string> name, Dictionary<BlockPosition, RoadPoint> roadPoints, int x, int z, Direction direction) {
            BlockPosition target = GetSignNonRoadPoint(roadPoints, x, z, direction);

            switch (signType) {
                case OsmReader.SignType.TrafficSignal:
                case OsmReader.SignType.Stop:
                    DrawTrafficSign(signType, name, direction, target);
                    break;
                default:
                    DrawSimpleSign(name, direction, target);
                    break;
            }
        }

        private static BlockPosition GetSignNonRoadPoint(Dictionary<BlockPosition, RoadPoint> roadPoints, int x, int z, Direction direction) {
            BlockPosition target;
            switch (direction) {
                case Direction.West: target = FindNonRoadPoint(roadPoints, 0, 1, new BlockPosition(x, z)); break;
                case Direction.East: target = FindNonRoadPoint(roadPoints, 0, -1, new BlockPosition(x, z)); break;
                case Direction.North: target = FindNonRoadPoint(roadPoints, -1, 0, new BlockPosition(x, z)); break;
                case Direction.South: target = FindNonRoadPoint(roadPoints, 1, 0, new BlockPosition(x, z)); break;
                default:
                    throw new ArgumentException(nameof(direction));
            }

            return target;
        }

        private void DrawSimpleSign(List<string> name, Direction direction, BlockPosition target) {
            var height = _bm.GetHeight(target.X, target.Z);
            var block = new AlphaBlock(BlockType.SIGN_POST);
            var ent = block.GetTileEntity() as TileEntitySign;
            SetSignName(name, ent);

            if (_conv.IsValidPoint(target)) {
                _bm.SetBlock(target.X, height, target.Z, block);
                _bm.SetData(target.X, height, target.Z, (int)direction);
            }
        }

        private void DrawTrafficSign(OsmReader.SignType signType, List<string> name, Direction direction, BlockPosition target) {
            var height = _bm.GetHeight(target.X, target.Z);
            const int StopSignHeight = 4;
            Console.WriteLine("Sign {0} {1}", target, signType);
            for (int i = 0; i < StopSignHeight; i++) {
                _bm.SetID(target.X, height + i, target.Z, BlockType.COBBLESTONE_WALL);
                _bm.SetData(target.X, height + i, target.Z, 0);
            }
            AlphaBlock block = new AlphaBlock(BlockType.WALL_SIGN);
            var ent = block.GetTileEntity() as TileEntitySign;
            SetSignName(name, ent);
            int signX = target.X, signZ = target.Z;
            switch (direction) {
                case Direction.West: signX--; break;
                case Direction.East: signX++; break;
                case Direction.South: signZ--; break;
                case Direction.North: signZ++; break;
            }
            if (_conv.IsValidPoint(target)) {
                _bm.SetBlock(signX, height + 2, signZ, block);
                int data;
                switch (direction) {
                    case Direction.North: data = 2; break;
                    case Direction.South: data = 3; break;
                    case Direction.West: data = 4; break;
                    case Direction.East: data = 5; break;
                    default:
                        throw new InvalidOperationException();
                }
                _bm.SetData(signX, height + 2, signZ, data);
            }

            AlphaBlock bannerBlock = new AlphaBlock(BlockType.STANDING_BANNER);
            var bannerEnt = bannerBlock.GetTileEntity() as TileEntityBanner;
            if (signType == OsmReader.SignType.Stop) {  
                //bannerEnt.BaseColor = BannerColor.Red;
                //bannerEnt.Patterns = new BannerPattern[0];
                bannerEnt.BaseColor = BannerColor.LightGray;
                bannerEnt.Patterns = new BannerPattern[] {
                            new BannerPattern(BannerColor.Red, BannerStyles.MiddleRectangle),
                            new BannerPattern(BannerColor.LightGray, BannerStyles.TopStripe),
                            new BannerPattern(BannerColor.LightGray, BannerStyles.BottomStripe),
                            new BannerPattern(BannerColor.LightGray, BannerStyles.Border),
                        };  
            } else {
                bannerEnt.BaseColor = BannerColor.LightGray;
                bannerEnt.Patterns = new BannerPattern[] {
                            new BannerPattern(BannerColor.Red, BannerStyles.TopTriangle),
                            new BannerPattern(BannerColor.Green, BannerStyles.BottomTriangle),
                            new BannerPattern(BannerColor.Yellow, BannerStyles.MiddleCircle),
                            new BannerPattern(BannerColor.Black, BannerStyles.CurlyBorder),
                            new BannerPattern(BannerColor.Black, BannerStyles.Border),
                        };
            }

            _bm.SetBlock(target.X, height + StopSignHeight, target.Z, bannerBlock);
            _bm.SetData(target.X, height + StopSignHeight, target.Z, (int)direction);
        }

        private void DrawTrafficLight(List<string> name, BlockPosition target, Direction direction, int lanes) {
            var height = _bm.GetHeight(target.X, target.Z);
            const int TrafficLightHeight = 7;
            Console.WriteLine("Sign {0} TrafficLight", target);
            // draw the vertical traffic light pieces...
            // We start with a piece of polished andisite, then an anvil, and have an
            // overlapping armor stand to look like cross walk buttons.
            var entities = _bm.GetChunk(target.X, height, target.Z).Entities;
            var armor = new TypedEntity("minecraft:armor_stand");
            armor.Position = new Vector3() { X = target.X + .5, Y = height, Z = target.Z + .5};
            armor.IsOnGround = true;
            _bm.SetID(target.X, height, target.Z, BlockType.STONE);
            _bm.SetData(target.X, height, target.Z, (int)StoneType.POLISHED_ANDESITE);
            _bm.SetID(target.X, height + 1, target.Z, BlockType.ANVIL);
            _bm.SetData(target.X, height + 1, target.Z, 1);
            if (direction == Direction.North || direction == Direction.South) {                
                armor.Rotation = new Orientation() { Yaw = Math.PI / 2 };
            }
            entities.Add(armor);

            for (int i = 2; i < TrafficLightHeight; i++) {
                _bm.SetID(target.X, height + i, target.Z, BlockType.COBBLESTONE_WALL);
                _bm.SetData(target.X, height + i, target.Z, 0);
            }
            // our direction indicates the direction the horizontal pole runs, from
            // which we derive our other directions...
            int xDelta = 0, zDelta = 0;
            switch (direction) {
                case Direction.West: xDelta = -1;break;
                case Direction.South: zDelta = 1;break;
                case Direction.North:zDelta = -1; break;
                case Direction.East: xDelta = 1; break;
                default: throw new NotImplementedException();
            }

            // draw the 1st three cobble stone horizontal pieces
            int xLoc = target.X + xDelta, zLoc = target.Z + zDelta;
            for (int i = 0; i < 3; i++) {
                _bm.SetID(xLoc, height + TrafficLightHeight - 1, zLoc, BlockType.COBBLESTONE_WALL);
                _bm.SetData(xLoc, height + TrafficLightHeight - 1, zLoc, 0);
                xLoc += xDelta;
                zLoc += zDelta;
            }
            for (int lane = 0; lane < lanes; lane++) {
                // Draw the traffic light...
                for (int i = 0; i < 2; i++) {
                    _bm.SetID(xLoc, height + TrafficLightHeight - 1 - i, zLoc, BlockType.WOOL);
                    _bm.SetData(xLoc, height + TrafficLightHeight - 1 - i, zLoc, (int)WoolColor.BLACK); 
                }

                AlphaBlock bannerBlock = new AlphaBlock(BlockType.WALL_BANNER);
                var bannerEnt = bannerBlock.GetTileEntity() as TileEntityBanner;
                bannerEnt.BaseColor = BannerColor.LightGray;
                bannerEnt.Patterns = new BannerPattern[] {
                    new BannerPattern(BannerColor.Red, BannerStyles.TopTriangle),
                    new BannerPattern(BannerColor.Green, BannerStyles.BottomTriangle),
                    new BannerPattern(BannerColor.Yellow, BannerStyles.MiddleCircle),
                    new BannerPattern(BannerColor.Black, BannerStyles.CurlyBorder),
                    new BannerPattern(BannerColor.Black, BannerStyles.Border),
                };

                int lightX, lightZ, lightDir;
                GetTrafficSignalSignInfo(direction, xLoc, zLoc, out lightX, out lightZ, out lightDir);

                _bm.SetBlock(lightX, height + TrafficLightHeight - 1, lightZ, bannerBlock);
                _bm.SetData(lightX, height + TrafficLightHeight - 1, lightZ, (int)lightDir);

                xLoc += xDelta;
                zLoc += zDelta;

                // draw three more horizontal pieces
                for (int i = 0; i < 3; i++) {
                    _bm.SetID(xLoc, height + TrafficLightHeight - 1, zLoc, BlockType.COBBLESTONE_WALL);
                    _bm.SetData(xLoc, height + TrafficLightHeight - 1, zLoc, 0);
                    xLoc += xDelta;
                    zLoc += zDelta;
                }

                // then place the sign if we're on our first part...
                if (lane == 0) {
                    AlphaBlock block = new AlphaBlock(BlockType.WALL_SIGN);
                    var ent = block.GetTileEntity() as TileEntitySign;
                    SetSignName(name, ent);
                    int signX, signZ, signDir;
                    GetTrafficSignalSignInfo(direction, xLoc - (xDelta * 2), zLoc - (zDelta * 2), out signX, out signZ, out signDir);
                    if (_conv.IsValidPoint(target)) {
                        _bm.SetBlock(signX, height + TrafficLightHeight - 1, signZ, block);
                        _bm.SetData(signX, height + TrafficLightHeight - 1, signZ, signDir);
                    }
                }
            }
        }

        private void GetTrafficSignalSignInfo(Direction direction, int xPos, int zPos, out int signX, out int signZ, out int signDir) {
            signX = xPos;
            signZ = zPos;
            switch (direction) {
                case Direction.West: signZ++; signDir = 3; break; // for north bound traffic, sign faces south
                case Direction.East: signZ--; signDir = 2; break; // for south bound traffic, sign faces north
                case Direction.South: signX++; signDir = 5; break; // for east bound traffic, sign faces west
                case Direction.North: signX--; signDir = 4; break; // for west bound traffic, sign faces east
                default:
                    throw new InvalidOperationException();
            }
        }

        private static BlockPosition FindNonRoadPoint(Dictionary<BlockPosition, RoadPoint> roadPoints, int xDir, int yDir, BlockPosition target) {
            int count = 0;
            while (roadPoints.ContainsKey(target) && count++ < 10) {
                target = new BlockPosition(target.X + xDir, target.Z + yDir);
            }

            return target;
        }

        private static int GetRoadWidth(OsmReader.Way way) {
            return (way.Lanes ?? 2) * 6;
        }

        struct Point {
            public readonly double Long, Lat;

            public Point(double x, double y) {
                Long = x;
                Lat = y;
            }
        }

        /// <summary>
        /// https://stackoverflow.com/questions/6630596/find-a-line-intersecting-a-known-line-at-right-angle-given-a-point
        /// </summary>
        /// <param name="from"></param>
        /// <param name="p1"></param>
        /// <param name="p2"></param>
        /// <returns></returns>
        private static OsmReader.Node FindPerpindicualPoint(OsmReader.Node from, OsmReader.Node p1, OsmReader.Node p2) {
            double t = ((from.Long - p1.Long) * (p2.Long - p1.Long) + (from.Lat - p1.Lat) * (p2.Lat - p1.Lat)) /
                    ((p2.Long - p1.Long) * (p2.Long - p1.Long) + (p2.Lat - p1.Lat) * (p2.Lat - p1.Lat));
            return new OsmReader.Node(
                p1.Lat + t * (p2.Lat - p1.Lat),
                p1.Long + t * (p2.Long - p1.Long)
            );
        }

        private static OsmReader.Node ClosestPoint(OsmReader.Node first, IEnumerable<OsmReader.Node> points) {
            OsmReader.Node closest = null;
            double closestDist = 0;
            foreach (var point in points) {
                var xDiff = first.Lat - point.Lat;
                var yDiff = first.Long - point.Long;
                var dist = Math.Sqrt(xDiff * xDiff + yDiff * yDiff);
                if (closest == null || dist < closestDist) {
                    closest = point;
                    closestDist = dist;
                }
            }

            return closest;
        }

        private void ReadStories(CsvReader residential, string storiesField) {
            residential.Read();
            residential.ReadHeader();
            List<string> address = new List<string>();
            while (residential.Read()) {
                var buildingNo = residential.GetField("BuildingNumber");
                if (!String.IsNullOrWhiteSpace(buildingNo)) {
                    address.Add(buildingNo.Trim());
                }
                var prefix = residential.GetField("DirectionPrefix");
                if (!String.IsNullOrWhiteSpace(prefix)) {
                    prefix = FixSuffix(prefix);
                    address.Add(prefix.Trim());
                }
                var streetName = residential.GetField("StreetName");
                if (!String.IsNullOrWhiteSpace(streetName)) {
                    address.Add(streetName.Trim());
                }
                var streetType = residential.GetField("StreetType");
                switch (streetType.Trim().ToUpper()) {
                    case "AVE": streetType = "Avenue"; break;
                    case "STR":
                    case "ST": streetType = "Street"; break;
                    case "PL": streetType = "Place"; break;
                    case "DR": streetType = "Drive"; break;
                    case "HWY": streetType = "Highway"; break;
                    case "CT": streetType = "Court"; break;
                    case "WAY": streetType = "Way"; break;
                    case "LN": streetType = "Lane"; break;
                    case "BLVD": streetType = "Boulevard"; break;
                    case "TER": streetType = "Terrace"; break;
                    case "RD": streetType = "Road"; break;
                    case "LOOP": streetType = "Loop"; break;
                    case "CIR": streetType = "Circle"; break;
                    case "TRL": streetType = "Trail"; break;
                    case "PW":
                    case "PKWY": streetType = "Parkway"; break;
                    case "WALK": streetType = "Walk"; break;
                    case "PT": streetType = "Point"; break;
                    case "EXT": streetType = "Extension"; break;
                    case "": break;
                    default:
                        break;
                }
                if (!String.IsNullOrWhiteSpace(streetType)) {
                    address.Add(streetType.Trim());
                }
                var suffix = residential.GetField("DirectionSuffix").Trim();
                if (!string.IsNullOrWhiteSpace(suffix)) {
                    suffix = FixSuffix(suffix);
                    address.Add(suffix);
                }
                var fullAddress = string.Join(" ", address);
                OsmReader.Building building;
                if (_reader.BuildingsByAddress.TryGetValue(fullAddress, out building)) {
                    var storiesStr = residential.GetField(storiesField);
                    float stories;
                    if (float.TryParse(storiesStr, out stories)) {
                        building.Stories = stories;
                    }
                } else {
                    //Console.WriteLine(fullAddress);
                }
                //Console.WriteLine(fullAddress.ToLower());
                address.Clear();
            }
        }

        private static string FixSuffix(string prefix) {
            switch (prefix.Trim().ToUpper()) {
                case "S": prefix = "South"; break;
                case "N": prefix = "North"; break;
                case "NE": prefix = "Northeast"; break;
                case "NW": prefix = "Northwest"; break;
                case "SE": prefix = "Southeast"; break;
                case "SW": prefix = "Southwest"; break;
                case "E": prefix = "East"; break;
                case "W": prefix = "West"; break;
            }

            return prefix;
        }

        private static void SetSignName(List<string> names, TileEntitySign ent) {
            for (int i = 0; i < names.Count; i++) {
                if (names[i].Length > 17) {
                    names[i] = names[i].Substring(0, 17);
                }
            }
            if (names.Count > 0) {
                ent.Text1 = SignJson(names[0]);
                if (names.Count > 1) {
                    ent.Text2 = SignJson(names[1]);
                    if (names.Count > 2) {
                        ent.Text3 = SignJson(names[2]);
                        if (names.Count > 3) {
                            ent.Text4 = SignJson(names[3]);
                        }
                    }
                }
            }
        }

        private static string SignJson(string name) {
            return string.Format("{{\"text\":\"{0}\"}}", name.Replace("\"", "\\\""));
        }

        private static void AddSignName(List<string> names, string name) {
            if (name != null) {
                while (name.Length > 17) {
                    var space = name.Substring(0, 17).LastIndexOf(' ');
                    if (space == -1) {
                        break;
                    }
                    names.Add(name.Substring(0, space));
                    name = name.Substring(space + 1);
                }
            }
            if (!string.IsNullOrWhiteSpace(name)) {
                names.Add(name);
            }
        }

        struct LinePosition {
            public readonly BlockPosition Block;
            /// <summary>
            /// The column (along the length of the line)
            /// </summary>
            public readonly int Column;
            /// <summary>
            /// The row (the index into the width)
            /// </summary>
            public readonly int Row;

            public LinePosition(BlockPosition block, int column, int row) {
                Block = block;
                Column = column;
                Row = row;
            }
        }

        static IEnumerable<LinePosition> PlotLine(int x0, int y0, int x1, int y1, int width = 1) {
            if (Math.Abs(y1 - y0) < Math.Abs(x1 - x0)) {
                if (x0 > x1) {
                    return PlotLineLow(x1, y1, x0, y0, width);
                } else {
                    return PlotLineLow(x0, y0, x1, y1, width);
                }
            } else if (y0 > y1) {
                return PlotLineHigh(x1, y1, x0, y0, width);
            }
            return PlotLineHigh(x0, y0, x1, y1, width);
        }

        static IEnumerable<LinePosition> PlotLineHigh(int x0, int y0, int x1, int y1, int width = 1) {
            int dx = x1 - x0;
            int dy = y1 - y0;
            int xi = 1;
            if (dx < 0) {
                xi = -1;
                dx = -dx;
            }
            int D = 2 * dx - dy;
            int x = x0;
            for (int y = y0; y <= y1; y++) {
                int end = width / 2;
                if ((width & 0x01) != 0) {
                    end++;
                }
                for (int i = -width / 2; i < end; i++) {
                    if (x + 1 >= 0) {
                        yield return new LinePosition(new BlockPosition(x + i, y), i - (-width / 2), y);
                    }
                }
                if (D > 0) {
                    x = x + xi;
                    D -= 2 * dy;
                }
                D += 2 * dx;
            }
        }

        static IEnumerable<LinePosition> PlotLineLow(int x0, int y0, int x1, int y1, int width = 1) {
            int dx = x1 - x0;
            int dy = y1 - y0;
            int yi = 1;
            if (dy < 0) {
                yi = -1;
                dy = -dy;
            }
            int D = 2 * dy - dx;
            int y = y0;
            for (int x = x0; x <= x1; x++) {
                int end = width / 2;
                if ((width & 0x01) != 0) {
                    end++;
                }
                for (int i = -width / 2; i < end; i++) {
                    yield return new LinePosition(new BlockPosition(x, y + i), i - (-width / 2), x);
                }
                if (D > 0) {
                    y = y + yi;
                    D -= 2 * dx;
                }
                D += 2 * dy;
            }
        }
    }
}
