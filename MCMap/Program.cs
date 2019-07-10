using CsvHelper;
using Substrate;
using Substrate.TileEntities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MinecraftMapper {
    class Mapper {
        private readonly NbtWorld _world;
        private readonly PositionConverter _conv;
        private readonly OsmReader _reader;
        private readonly AnvilBlockManager _bm;
        private readonly long[] _buildings, _roads;
        private readonly StreamWriter _log;
        private readonly bool _dryRun = false;

        public Mapper(string log, string world, string osmXml, PositionBase[] conversions, long[] buildings, long[] roads) {
            _world = NbtWorld.Open(world);
            _buildings = buildings;
            _roads = roads;
            _bm = _world.GetBlockManager() as AnvilBlockManager;
            _bm.AutoLight = true;
            if (log != null) {
                _log = new StreamWriter(new FileStream(log, FileMode.Create, FileAccess.ReadWrite));
            }
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
            /*
            HashSet<BlockPosition> buildingPoints = new HashSet<BlockPosition>();
            DrawBuilding(
                new OsmReader.Building("Test", "1234", "main St", OsmReader.Amenity.None, OsmReader.Material.None,
                new OsmReader.RoofInfo(OsmReader.RoofType.Gabled, null, null, null, null, false),
                new OsmReader.Node[] {
                    new OsmReader.Node(47.6922268, -122.3524505),
                    new OsmReader.Node(47.6922699, -122.3524514),
                    new OsmReader.Node(47.6922701, -122.3524313),
                    new OsmReader.Node(47.6923199, -122.3524323),
                    new OsmReader.Node(47.6923204, -122.3523824),
                    new OsmReader.Node(47.6923272, -122.3523825),
                    new OsmReader.Node(47.6923275, -122.3523532),
                    new OsmReader.Node(47.6923192, -122.3523531),
                    new OsmReader.Node(47.6923196, -122.3523109),
                    new OsmReader.Node(47.6922282, -122.3523090),
                    new OsmReader.Node(47.6922268, -122.3524505),

                }),
                buildingPoints,
                1
            );
            _world.Save();*/
        }

        private void WriteLine(object msg) {
            if (_log != null) {
                _log.WriteLine(msg);
            }
            Console.WriteLine(msg);
        }

        private void WriteLine(string msg, object arg) {
            if (_log != null) {
                _log.WriteLine(msg, arg);
            }
            Console.WriteLine(msg, arg);
        }

        private void WriteLine(string msg, object arg0, object arg1) {
            if (_log != null) {
                _log.WriteLine(msg, arg0, arg1);
            }
            Console.WriteLine(msg, arg0, arg1);
        }

        private void WriteLine(string msg, params object[] args) {
            if (_log != null) {
                _log.WriteLine(msg, args);
            }
            Console.WriteLine(msg, args);
        }

        public void MapIt() {
            //Save();
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
                WriteLine("{0} roads", _reader.Ways.Count);

                var roadPoints = DrawRoads();

                DrawStreetLamps(roadPoints);
                _world.Level.Spawn = new SpawnPoint(6084, _world.Level.Spawn.Y, 14566);
                var buildingPoints = DrawBuildings();

                Save();


                DrawBarriers(buildingPoints);

                Save();
            } catch (Exception e) {
                Save();
                WriteLine(e);
            }
            DateTime done = DateTime.Now;
            WriteLine("All done, data processing {0}, total {1}", dataProcessing - startTime, done - startTime);
            Console.ReadLine();
        }

        private void Save() {
            if (!_dryRun) {
                _world.Save();
            }
        }

        private void DrawStreetLamps(Dictionary<BlockPosition, RoadPoint> roadPoints) {
            int cur = 0;
            foreach (var sign in _reader.Signs.OrderBy(x => MapOrder(_conv, x.Key))) {
                if ((++cur % 100) == 0) {
                    SaveBlocks();
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
                    WriteLine("Lamp at {0} facing {1}", pos, dir);
                    DrawStreetLamp(dir, pos);
                }
            }
        }

        private void DrawBarriers(HashSet<BlockPosition> buildingPositions) {
            int cur = 0;
            foreach (var barrier in _reader.Barriers.OrderBy(x => MapOrder(_conv, x.Nodes.First()))) {
                if ((++cur % 100) == 0) {
                    SaveBlocks();
                }
                int blockKind = 0, height = 1;
                switch (barrier.Kind) {
                    case OsmReader.BarrierKind.Fence: blockKind = BlockType.FENCE; break;
                    case OsmReader.BarrierKind.Gate: blockKind = BlockType.FENCE_GATE; break;
                    case OsmReader.BarrierKind.GuardRail: blockKind = BlockType.NETHER_BRICK_FENCE; break;
                    case OsmReader.BarrierKind.Hedge: blockKind = BlockType.LEAVES; height = 2; break;
                    case OsmReader.BarrierKind.RetainingWall: blockKind = BlockType.STONE_BRICK; height = 2; break;
                    case OsmReader.BarrierKind.Wall:
                        switch (barrier.Wall) {
                            case OsmReader.Wall.Brick:
                                blockKind = BlockType.BRICK_BLOCK; break;
                            default:
                                blockKind = BlockType.STONE;
                                break;
                        }
                        height = 4;
                        break;
                }

                var start = barrier.Nodes[0];
                HashSet<BlockPosition> wallPoints = new HashSet<BlockPosition>();
                for (int i = 1; i < barrier.Nodes.Length; i++) {
                    var from = _conv.ToBlock(start.Lat, start.Long);
                    WriteLine("Barrier {0} {1}", from, barrier.Kind);
                    var to = _conv.ToBlock(barrier.Nodes[i].Lat, barrier.Nodes[i].Long);

                    foreach (var point in PlotLine(from.X, from.Z, to.X, to.Z)) {
                        if (buildingPositions.Contains(point.Block) ||
                            wallPoints.Contains(point.Block)) {
                            continue;
                        }
                        wallPoints.Add(point.Block);

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
            //string map = @"C:\Users\dino_\AppData\Roaming\.minecraft\saves\SeattleCentralDistrict";
            // string map = @"C:\Users\dino_\AppData\Roaming\.minecraft\saves\New World";
            //string map = @"C:\Users\dino_\AppData\Roaming\.minecraft\saves\Seattle";
            string map = @"C:\Users\dino_\AppData\Roaming\.minecraft\saves\Hackathon";
            var world = NbtWorld.Open(map);
            //var block = world.GetBlockManager().GetBlock(0, 130, 0);
            Console.WriteLine(world);
            //string map = @"C:\Users\dino_\AppData\Roaming\.minecraft\saves\SeattleCentralDistrict";
            
            //string map = @"C:\Users\dino_\AppData\Roaming\.minecraft\saves\SeattleCreativeLidar4_2";
            string log = null;
            //string osmData = @"C:\Users\dino_\Downloads\SeattleTargetted.osm";
            string osmData = @"C:\Users\dino_\Downloads\SeattleCDMini.osm";
            List<long> buildings = new List<long>();
            List<long> roads = new List<long>();
            List<PositionBase> positionBases = new List<PositionBase>();
            foreach (var arg in args) {
                if (arg == "/?" || arg == "/help") {
                    Help();
                    return 0;
                } else if (arg.StartsWith("/log:")) {
                    log = arg.Substring("/log:".Length);
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
            //                    47.6152895, -122.3062076
            //                    47.6164976, -122.3029356
            //  Columbia & 26th 47.6164554, -122.2987516
            //  20th and East Olive 47.6090430, -122.3063048
            /*var seattleCDMini = new[] {
                new PositionBase(47.6090430, -122.2987516, 10, 1450),  //26th and East Columbia
                new PositionBase(47.6171548, -122.3061587, 946, 97)   
            };*/
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
                new PositionBase(47.6068780, -122.2987088, 6446, 15665),
                new PositionBase(47.6683035, -122.2757863, 9353, 4472)
            };

            if (positionBases.Count == 0) {
                positionBases.AddRange(seattle);
            }
            /*
            roads = new List<long>() {
                481291015, 343521145, 481291013,
                361784412, 337739269, 
                396055523,  // 23rd ave north of union
                460419028, // 23rd ave south of union
                243349945,
                40416132, // east union street
                256994206, // east union
                396055524, // east union
                525497324, // 18th Ave
                620762218, // East madison between 16th and 19th ave
            };
            buildings = new List<long>() {
                228850098, 136571516, 456138164
            };*/
            var mapper = new Mapper(log, map, osmData, positionBases.ToArray(), buildings.ToArray(), roads.ToArray());
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
            South = 0,
            SouthSouthWest = 1,
            SouthWest = 2,
            WestSouthWest = 3,
            West = 4,
            WestNorthWest = 5,
            NorthWest = 6,
            NorthNorthWest = 7,
            North = 8,
            NorthNorthEast = 9,
            NorthEast = 10,
            EastNorthEast = 11,
            East = 12,
            EastSouthEast = 13,
            SouthEast = 14,
            SouthSouthEast = 15,
        }

        enum FullDirection {
            East,
            West,
            South,
            North
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
            HashSet<BlockPosition> roofPoints = new HashSet<BlockPosition>();
            IEnumerable<KeyValuePair<long, OsmReader.Building>> buildingList;
            if (_buildings.Length != 0) {
                buildingList = _reader.Buildings.Where(x => _buildings.Contains(x.Key));
            } else {
                buildingList = _reader.Buildings.OrderBy(x => MapOrder(_conv, x.Value.BuildingNodes.First()));
            }
            // new[] { reader.Buildings[224047836], reader.Buildings[224048580], reader.Buildings[222409095], reader.Buildings[222409094] }
            //var buildings = new long[]{ 224047836 /*363250260, 4856210822, 4856214545 /*224047836, 224048580, 222409095, 222409094*/ };
            //var buildingList = _reader.Buildings.Where(x => buildings.Contains(x.Key));
            foreach (var idAndBuilding in buildingList) {
                var building = idAndBuilding.Value;
                if ((++cur % 200) == 0) {
                    Console.WriteLine("Saving blocks...");
                    SaveBlocks();
                }

                WriteLine("{0} {1} ({2}/{3})", building.HouseNumber, building.Street, cur, _reader.Buildings.Count);
                DrawBuilding(building, buildingPoints, roofPoints, (int)(idAndBuilding.Key % 16));
            }

            SaveBlocks();

            return buildingPoints;
        }

        private void SaveBlocks() {
            if (!_dryRun) {
                _world.SaveBlocks();
            }
        }

        private void DrawBuilding(OsmReader.Building building, HashSet<BlockPosition> buildingPoints, HashSet<BlockPosition> roofPoints, int color) {
            int blockType, data;
            GetBuildingColor(building, color, out blockType, out data);

            int top = Int32.MaxValue, left = Int32.MaxValue, bottom = Int32.MinValue, right = Int32.MinValue;

            int buildingHeight = Math.Max(6, (int)((building.Stories ?? 1) * 4) + 2);
            int maxHeight = Int32.MinValue;
            var start = building.BuildingNodes[0];

            for (int i = 1; i < building.BuildingNodes.Length; i++) {
                var from = _conv.ToBlock(start.Lat, start.Long);
                var to = _conv.ToBlock(building.BuildingNodes[i].Lat, building.BuildingNodes[i].Long);

                top = Math.Min(top, from.Z);
                bottom = Math.Max(bottom, from.Z);
                left = Math.Min(left, from.X);
                right = Math.Max(right, from.X);

                foreach (var point in PlotLine(from.X, from.Z, to.X, to.Z)) {
                    if (_conv.IsValidPoint(point.Block)) {
                        var height = _bm.GetHeight(point.Block.X, point.Block.Z);
                        maxHeight = Math.Max(maxHeight, height);
                    }
                }
                start = building.BuildingNodes[i];
            }

            start = building.BuildingNodes[0];
            var baseHeights = new Dictionary<BlockPosition, int>();
            for (int i = 1; i < building.BuildingNodes.Length; i++) {
                var from = _conv.ToBlock(start.Lat, start.Long);
                var to = _conv.ToBlock(building.BuildingNodes[i].Lat, building.BuildingNodes[i].Long);

                foreach (var point in PlotLine(from.X, from.Z, to.X, to.Z)) {
                    if (_conv.IsValidPoint(point.Block)) {
                        // if we have duplicate entries for a building don't draw it twice...
                        if (buildingPoints.Contains(point.Block)) {
                            continue;
                        }
                        buildingPoints.Add(point.Block);

                        var height = GetHeight(point.Block);
                        //WriteLine("{0},{1},{2}", point.X, height, point.Z);
                        baseHeights[point.Block] = height;
                        for (int j = 0; j < (maxHeight - height) + buildingHeight; j++) {
                            if (height - 1 + j > 240) {
                                break;
                            }
                            _bm.SetID(point.Block.X, height - 1 + j, point.Block.Z, (int)blockType);
                            _bm.SetData(point.Block.X, height - 1 + j, point.Block.Z, data);
                        }
                    }
                }

                start = building.BuildingNodes[i];
                //WriteLine("From {0},{1} to {2},{3}", from.X, from.Z, to.X, to.Z);
            }


            DrawBuildingRoof(building, top, left, bottom, right, buildingHeight, maxHeight, buildingPoints, roofPoints);
            
            
            DrawBuildingSign(building, top, left, bottom, right, baseHeights, buildingPoints);
        }

        private static void GetBuildingColor(OsmReader.Building building, int color, out int blockType, out int data) {
            blockType = BlockType.STAINED_CLAY;
            
            switch (building.Material) {
                case OsmReader.Material.Stone:
                    blockType = BlockType.STONE; data = (int)StoneType.STONE; return;
                case OsmReader.Material.Wood: blockType = BlockType.WOOD; data = (int)WoodType.OAK; return;
                case OsmReader.Material.Brick: blockType = BlockType.BRICK_BLOCK; data = 0; return;
                case OsmReader.Material.Concrete: blockType = BlockType.CONCRETE; data = 0; return;
                case OsmReader.Material.Sandstone: blockType = BlockType.SANDSTONE; data = 0;return;
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
                data = (building.Street.GetHashCode() ^ building.HouseNumber.GetHashCode() + 7) % 16;
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
                    case OsmReader.Amenity.PlaceOfWorship: blockType = BlockType.NETHER_BRICK; data = 0; break;
                    case OsmReader.Amenity.CommunityCenter:
                        blockType = BlockType.OBSIDIAN;
                        break;
                    case OsmReader.Amenity.Theatre: blockType = BlockType.WOOD; data = (int)WoodType.BIRCH; break;
                }
            } else {
                data = color;
            }
        }

        private void DrawBuildingSign(OsmReader.Building building, int top, int left, int bottom, int right, Dictionary<BlockPosition, int> baseHeights, HashSet<BlockPosition> buildingPoints) {
            DrawSignItems(building, top, left, bottom, right, baseHeights, buildingPoints);

            if (building.Street == null || building.HouseNumber == null) {
                return;
            }
            Direction direction = 0;
            var start = building.BuildingNodes[0];
            var houseLoc = _conv.ToBlock(start.Lat, start.Long);
            List<OsmReader.Way> roads;
            BlockPosition signLoc = houseLoc;
            if (_reader.RoadsByName.TryGetValue(building.Street, out roads)) {
                // Find the closest point between a random point on the building and the road...
                var first = building.BuildingNodes.First();

                var points = roads.SelectMany(x => x.Nodes);
                OsmReader.Node closestRoadPoint = ClosestPoint(first, points);



                var roadLoc = _conv.ToBlock(closestRoadPoint.Lat, closestRoadPoint.Long);
                // And then find the closest building point to the closest road point
                //OsmReader.Node closestBuildingPoint = ClosestPoint(closestRoadPoint, building.Nodes);

                // Figure out whre the sign is placed.  This can be a primary point from a single node
                // where it would be placed on an overhead map, or we'll find the closest point between
                // the building and a road point.
                Console.WriteLine("----");
                OsmReader.Node signPoint = null, buildingWallPoint = null;

                double distance = double.MaxValue;
                /* See if we can find the line which is mostly parallel to the road
                    * that is closest to the road */
                var nextClosest = NextClosestPoint(first, closestRoadPoint, points);

                OsmReader.Node prev = building.BuildingNodes[0];
                double closestPerpWall = double.MaxValue;
                OsmReader.Node building1 = null, building2 = null;
                for (int i = 1; i < building.BuildingNodes.Length; i++) {
                    var dx1 = closestRoadPoint.Lat - nextClosest.Lat;
                    var dy1 = closestRoadPoint.Long - nextClosest.Long;
                    var dx2 = prev.Lat - building.BuildingNodes[i].Lat;
                    var dy2 = prev.Long - building.BuildingNodes[i].Long;
                    var cosAngle = Math.Abs((dx1 * dx2 + dy1 * dy2) / Math.Sqrt((dx1 * dx1 + dy1 * dy1) * (dx2 * dx2 + dy2 * dy2)));
                    if (cosAngle >= 0.99) {
                        // lines are basically parallel
                        var perpPoint = FindPerpindicualPoint(closestRoadPoint, prev, building.BuildingNodes[i]);
                        var dist = Math.Sqrt(
                            (perpPoint.Lat - closestRoadPoint.Lat) * (perpPoint.Lat - closestRoadPoint.Lat) +
                            (perpPoint.Long - closestRoadPoint.Long) * (perpPoint.Long - closestRoadPoint.Long)
                        );
                        if (dist < closestPerpWall) {
                            closestPerpWall = dist;
                            building1 = prev;
                            building2 = building.BuildingNodes[i];
                        }
                    }

                    prev = building.BuildingNodes[i];
                }

                if (building1 != null) {
                    /* We found the closest mostly parallel line */
                    var front = _conv.ToBlock(building1.Lat, building1.Long);
                    var other = _conv.ToBlock(building2.Lat, building2.Long);
                    signPoint = building1;
                    var doorX = (front.X + other.X) / 2;
                    var doorZ = (front.Z + other.Z) / 2;
                    buildingWallPoint = building1;
                    int doorHeight;
                    if (baseHeights.TryGetValue(new BlockPosition(doorX, doorZ), out doorHeight)) {
                        DoorDirection facing = 0;
                        if (doorZ == other.Z) {
                            if (building1.Lat < closestRoadPoint.Lat) {
                                facing = DoorDirection.North;
                            } else {
                                facing = DoorDirection.South;
                            }
                        } else {
                            if (building1.Long < closestRoadPoint.Long) {
                                facing = DoorDirection.East;
                            } else {
                                facing = DoorDirection.West;
                            }
                        }
                        DrawDoor(facing, doorX, doorZ, doorHeight);
                    }
                } else {
                    foreach (var buildingPoint in building.BuildingNodes) {
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
                //var pointOnRoad = FindPerpindicualPoint(signPoint, comparison, closestRoadPoint);
                //var angle = Math.Atan2(signPoint.Lat - pointOnRoad.Lat, signPoint.Long - pointOnRoad.Long) * 180 / Math.PI;
                var angle = Math.Atan2(signPoint.Lat - buildingWallPoint.Lat, signPoint.Long - buildingWallPoint.Long) * 180 / Math.PI;
                var buildingLoc = _conv.ToBlock(buildingWallPoint.Lat, buildingWallPoint.Long);
                if ((angle > -45 && angle < 45) || angle > 135 || angle < -135) {
                    if (closestRoadPoint.Long > signPoint.Long) { // this comparison is backwards due to negative longitude in Seattle
                        signLoc = new BlockPosition(buildingLoc.X + 1, buildingLoc.Z);
                        direction = Direction.East;
                    } else {
                        signLoc = new BlockPosition(buildingLoc.X - 1, buildingLoc.Z);
                        direction = Direction.West;
                    }
                } else {
                    if (closestRoadPoint.Lat < signPoint.Lat) {
                        signLoc = new BlockPosition(buildingLoc.X, buildingLoc.Z + 1);
                        direction = Direction.South;
                    } else {
                        signLoc = new BlockPosition(buildingLoc.X, buildingLoc.Z - 1);
                        direction = Direction.North;
                    }
                }

                DrawSign(signLoc, direction, building.Name, building.HouseNumber);
            }
        }

        enum DoorDirection {
            East = 0,
            South = 1,
            West = 2,
            North = 3
        }

        private void DrawDoor(DoorDirection facing, int doorX, int doorZ, int doorHeight) {
            _bm.SetID(doorX, doorHeight + 1, doorZ, (int)BlockType.WOOD_DOOR);
            _bm.SetData(doorX, doorHeight + 1, doorZ, 0);
            
            _bm.SetID(doorX, doorHeight + 2, doorZ, (int)BlockType.WOOD_DOOR);
            _bm.SetData(doorX, doorHeight + 2, doorZ, 0x08 | (int)facing);
        }

        private void DrawSign(BlockPosition signLoc, Direction direction, params string[] text) {
            DrawSign(signLoc, direction, BlockType.SIGN_POST, null, text);
        }

        private void DrawSign(BlockPosition signLoc, Direction direction, int signType = BlockType.SIGN_POST, int ? height = null, params string[] text) {
            List<string> names = new List<string>();
            foreach (var str in text) {
                AddSignName(names, str);
            }
            AlphaBlock block = new AlphaBlock(signType);
            var ent = block.GetTileEntity() as TileEntitySign;
            SetSignName(names, ent);
            if (height == null) {
                height = GetHeight(signLoc) + 1;
            }
            _bm.SetBlock(signLoc.X, height.Value, signLoc.Z, block);
            _bm.SetData(signLoc.X, height.Value, signLoc.Z, (int)direction);
        }

        private int GetHeight(BlockPosition signLoc) {
            var groundHeight = _bm.GetHeight(signLoc.X, signLoc.Z);
            var b = _bm.GetBlock(signLoc.X, groundHeight, signLoc.Z);
            while (b.ID == BlockType.AIR) {
                // GetHeight doesn't always work so well :P
                groundHeight--;
                b = _bm.GetBlock(signLoc.X, groundHeight, signLoc.Z);
            }

            return groundHeight;
        }

        private void DrawSignItems(OsmReader.Building building, int top, int left, int bottom, int right, Dictionary<BlockPosition, int> baseHeights, HashSet<BlockPosition> buildingPoints) {
            if (building.SignItems == null) {
                return;
            }
            Dictionary<BlockPosition, OsmReader.Node> signPoints = new Dictionary<BlockPosition, OsmReader.Node>();
            foreach (var signNode in building.SignItems) {
                var block = _conv.ToBlock(signNode.Lat, signNode.Long);
                // distances, ordered per FullDirection enum.  We're finding which
                // side of the building we're closest too, assuming a square building.
                int[] distances = new[] {
                     Math.Abs(block.X - left),
                     Math.Abs(block.X - right),
                     Math.Abs(block.Z - bottom),
                     Math.Abs(block.Z - top)
                };

                int minDist = Int32.MaxValue;
                for (int i = 0; i < distances.Length; i++) {
                    minDist = Math.Min(distances[i], minDist);
                }
                BlockPosition targetPoint = default(BlockPosition);
                Direction dir = Direction.East;
                int adjX = 0, adjZ = 0;
                // Next find a point that is along the perimeter of the building, or possibly
                // further if the building isn't an exact rectangle
                for (int i = 0; i < 4; i++) {
                    if (distances[i] == minDist) {
                        switch ((FullDirection)i) {
                            case FullDirection.East:
                                dir = Direction.West;
                                adjX = -1;
                                targetPoint = new BlockPosition(left, block.Z);
                                break;
                            case FullDirection.West:
                                dir = Direction.East;
                                adjX = 1;
                                targetPoint = new BlockPosition(right, block.Z);
                                break;
                            case FullDirection.South:
                                dir = Direction.South;
                                adjZ = 1;
                                targetPoint = new BlockPosition(block.X, bottom);
                                break;
                            case FullDirection.North:
                                dir = Direction.North;
                                adjZ = -1;
                                targetPoint = new BlockPosition(block.X, top);
                                break;
                        }
                        break;
                    }
                }

                // And walk that point back into the closest building point
                while (!buildingPoints.Contains(targetPoint) && 
                    targetPoint.X >= left - 1 && 
                    targetPoint.X <= right + 1 && 
                    targetPoint.Z >= top - 1 && 
                    targetPoint.Z <= bottom - 1) {
                    targetPoint = new BlockPosition(targetPoint.X - adjX, targetPoint.Z - adjZ);
                }

                var buildingPoint = targetPoint;
                targetPoint = new BlockPosition(targetPoint.X + adjX, targetPoint.Z + adjZ);

                if (signPoints.ContainsKey(targetPoint)) {
                    continue;
                }
                /*
                while (signPoints.TryGetValue(targetPoint, out existingPoint)) {
                    if (dir == Direction.East || dir == Direction.West) {
                        if (existingPoint.Long > signNode.Long) {
                            targetPoint = new BlockPosition(targetPoint.X, targetPoint.Z + 1);
                        } else {
                            targetPoint = new BlockPosition(targetPoint.X, targetPoint.Z - 1);
                        }
                    } else {
                        if (existingPoint.Lat > signNode.Lat) {
                            targetPoint = new BlockPosition(targetPoint.X + 1, targetPoint.Z);
                        } else {
                            targetPoint = new BlockPosition(targetPoint.X - 1, targetPoint.Z);
                        }
                    }
                } */

                signPoints[targetPoint] = signNode;

                // And finally bump it out to the outside of the building
                if (buildingPoints.Contains(buildingPoint)) {
                    DoorDirection doorDir = 0;
                    switch (dir) {
                        case Direction.East: doorDir = DoorDirection.East; break;
                        case Direction.South: doorDir = DoorDirection.South; break;
                        case Direction.West: doorDir = DoorDirection.West; break;
                        case Direction.North: doorDir = DoorDirection.North; break;
                    }
                    DrawDoor(doorDir, targetPoint.X - adjX, targetPoint.Z - adjZ, baseHeights[buildingPoint]);
                    DrawSign(targetPoint, dir, BlockType.WALL_SIGN, baseHeights[buildingPoint] + 3, signNode.Description);
                } else {
                    DrawSign(targetPoint, dir, signNode.Description);
                }
            }
        }

        private static double GetDistance(OsmReader.Node signPoint, OsmReader.Node pointOnBuilding) {
            return Math.Sqrt(
                (signPoint.Lat - pointOnBuilding.Lat) * (signPoint.Lat - pointOnBuilding.Lat) +
                (signPoint.Long - pointOnBuilding.Long) * (signPoint.Long - pointOnBuilding.Long)
            );
        }

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

        private void DrawBuildingRoof(OsmReader.Building building, int top, int left, int bottom, int right, int buildingHeight, int maxHeight, HashSet<BlockPosition> buildingPositions, HashSet<BlockPosition> roofPoints) {
            // OAKWOAD_STAIRS
            // SPRUCEWOOD_STAIRS
            // BIRCHWOOD_STAIRS
            // DARKOAK_WOOD_STAIRS
            // ACACIA_WOOD_STAIRS
            // RED_SANDSTONE_STAIRS
            // STONE_BRICK_STAIRS
            // JUNGLE_WOOD_STAIRS
            int dataType = (int)0;
            var roofStart = maxHeight + buildingHeight - 1;
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
                    DrawFlatRoof(top, left, bottom, right, buildingPositions, roofPoints, roofStart, roofMapping);
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

        private void DrawGabledRoof(OsmReader.Building building, int top, int left, int bottom, int right, HashSet<BlockPosition> buildingPositions, HashSet<BlockPosition> roofPoints, int dataType, int roofStart, int stairType, int baseType) {
            var height = Math.Abs(top - bottom);
            var width = Math.Abs(left - right);

            if (width > height && !building.Roof.OrientationAcross) {
                for (int x = left; x <= right; x++) {
                    int tStart = -1, bStart = -1;
                    for (int y = top; y <= bottom; y++) {
                        if (buildingPositions.Contains(new BlockPosition(x, y))) {
                            tStart = y;
                            break;
                        }
                    }
                    for (int y = top; y >= bottom; y--) {
                        if (buildingPositions.Contains(new BlockPosition(x, y))) {
                            bStart = y;
                            break;
                        }
                    }
                    var lHeight = Math.Abs(tStart - bStart);

                    for (int i = 0; i < (lHeight + 1) / 2; i++) {
                        if (roofPoints.Contains(new BlockPosition(x, i + tStart))) {
                            continue;
                        }
                        _bm.SetID(x, roofStart + i, i + tStart, stairType);
                        _bm.SetData(x, roofStart + i, i + tStart, (int)FullDirection.East);

                        _bm.SetID(x, roofStart + i, bStart - i, stairType);
                        _bm.SetData(x, roofStart + i, bStart - i, (int)FullDirection.West);

                        roofPoints.Add(new BlockPosition(x, i + tStart));
                        roofPoints.Add(new BlockPosition(x, bStart - i));

                        for (int j = 0; j < i; j++) {
                            _bm.SetID(x, roofStart + j, i + tStart, baseType);
                            _bm.SetData(x, roofStart + j, i + tStart, dataType);

                            _bm.SetID(x, roofStart + j, bStart - i, baseType);
                            _bm.SetData(x, roofStart + j, bStart - i, dataType);
                        }
                    }

                    if ((lHeight & 0x01) == 0) {
                        for (int j = 0; j <= lHeight / 2; j++) {
                            if (roofPoints.Contains(new BlockPosition(x, tStart + lHeight / 2))) {
                                continue;
                            }
                            _bm.SetID(x, roofStart + j, tStart + lHeight / 2, baseType);
                            _bm.SetData(x, roofStart + j, tStart + lHeight / 2, dataType);
                            roofPoints.Add(new BlockPosition(x, tStart + lHeight / 2));
                        }
                    }
                }
            } else {
                for (int y = top; y <= bottom; y++) {
                    int lStart = -1, rStart = -1;
                    for (int x = left; x <= right; x++) {
                        if (buildingPositions.Contains(new BlockPosition(x, y))) {
                            lStart = x;
                            break;
                        }
                    }
                    for (int x = right; x >= left; x--) {
                        if (buildingPositions.Contains(new BlockPosition(x, y))) {
                            rStart = x;
                            break;
                        }
                    }
                    var lWidth = Math.Abs(lStart - rStart);

                    for (int i = 0; i < (lWidth + 1) / 2; i++) {
                        if (roofPoints.Contains(new BlockPosition(i + lStart, y))) {
                            continue;
                        }

                        _bm.SetID(i + lStart, roofStart + i, y, stairType);
                        _bm.SetData(i + lStart, roofStart + i, y, (int)FullDirection.East);

                        _bm.SetID(rStart - i, roofStart + i, y, stairType);
                        _bm.SetData(rStart - i, roofStart + i, y, (int)FullDirection.West);
                        roofPoints.Add(new BlockPosition(i + lStart, y));
                        for (int j = 0; j < i; j++) {
                            _bm.SetID(i + lStart, roofStart + j, y, baseType);
                            _bm.SetData(i + lStart, roofStart + j, y, dataType);

                            _bm.SetID(rStart - i, roofStart + j, y, baseType);
                            _bm.SetData(rStart - i, roofStart + j, y, dataType);
                        }
                    }

                    if ((lWidth & 0x01) == 0) {
                        for (int j = 0; j <= lWidth / 2; j++) {
                            if (roofPoints.Contains(new BlockPosition(lStart + lWidth / 2, y))) {
                                continue;
                            }

                            _bm.SetID(lStart + lWidth / 2, roofStart + j, y, baseType);
                            _bm.SetData(lStart + lWidth / 2, roofStart + j, y, dataType);
                            roofPoints.Add(new BlockPosition(lStart + lWidth / 2, y));

                        }
                    }
                }
            }
        }

        private void DrawHippedRoof(int top, int left, int bottom, int right, HashSet<BlockPosition> buildingPositions, HashSet<BlockPosition> roofPoints, int dataType, int roofStart, int stairType, int baseType) {
            for (int z = top; z <= bottom; z++) {
                //int tStart = -1, bStart = -1;
                int lStart = -1, rStart = -1;
                for (int x = left; x <= right; x++) {
                    if (buildingPositions.Contains(new BlockPosition(x, z))) {
                        lStart = x;
                        break;
                    }
                }
                for (int x = right; x >= left; x--) {
                    if (buildingPositions.Contains(new BlockPosition(x, z))) {
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
                            _bm.SetID(i + lStart, roofStart + y, z, stairType);
                            _bm.SetData(i + lStart, roofStart + y, z, (int)(direction ?? FullDirection.East));

                            _bm.SetID(rStart - i, roofStart + y, z, stairType);
                            _bm.SetData(rStart - i, roofStart + y, z, (int)(direction ?? FullDirection.West));

                        } else {
                            _bm.SetID(i + lStart, roofStart + y, z, baseType);
                            _bm.SetData(i + lStart, roofStart + y, z, dataType);

                            _bm.SetID(rStart - i, roofStart + y, z, baseType);
                            _bm.SetData(rStart - i, roofStart + y, z, dataType);
                        }
                    }
                }
            }
        }

        private void DrawFlatRoof(int top, int left, int bottom, int right, HashSet<BlockPosition> buildingPositions, HashSet<BlockPosition> roofPoints, int roofStart, ColorMapping roofMapping) {
            for (int y = top; y <= bottom; y++) {
                int lStart = -1, rStart = -1;
                for (int x = left; x <= right; x++) {
                    if (buildingPositions.Contains(new BlockPosition(x, y))) {
                        lStart = x;
                        break;
                    }
                }
                for (int x = right; x >= left; x--) {
                    if (buildingPositions.Contains(new BlockPosition(x, y))) {
                        rStart = x;
                        break;
                    }
                }

                for (int x = lStart; x <= rStart; x++) {
                    if (roofPoints.Contains(new BlockPosition(x, y))) {
                        continue;
                    }
                    _bm.SetID(x, roofStart, y, roofMapping.VerticalBlockType);
                    _bm.SetData(x, roofStart, y, 0);
                    roofPoints.Add(new BlockPosition(x, y));
                }
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

        struct RoadSegment : IEquatable<RoadSegment> {
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

        class RoadQueue {
            private readonly Dictionary<OsmReader.Way, LinkedList<OsmReader.Way>> Items;
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
            //var intersections = new Dictionary<RoadIntersection, List<BlockPosition>>();
            //var roads = new[] { reader.Ways[6358491], reader.Ways[6358495], reader.Ways[158718178], reader.Ways[241283900], reader.Ways[6433374] };
            foreach (var roadGroup in roads) {
                foreach (var way in roadGroup) {
                    if ((++cur % 50) == 0) {
                        SaveBlocks();
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

                    WriteLine("{0} ({1}/{2})", way.Name, cur, _reader.Ways.Count);
                    var style = GetRoadStyle(way);
                    if (style != null) {
                        style.Render(roadPoints);
                    }
                }

                List<OsmReader.Node> busStops;
                if (_reader.BusStops.TryGetValue(roadGroup.Key, out busStops)) {
                    WriteLine("Adding {0} bus stops", busStops.Count);
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
            }

            DrawSigns(roadPoints);

            foreach (var roadGroup in roads) {
                foreach (var way in roadGroup) {
                    WriteLine("Zebra {0}", _conv.ToBlock(way.Nodes[0].Lat, way.Nodes[0].Long));
                    if (way.Crossing == OsmReader.CrossingType.Zebra) {
                        BlockPosition[] blockPositions;
                        if (way.Nodes.Length > 1) {
                            blockPositions = way.Nodes.Select(x => _conv.ToBlock(x.Lat, x.Long)).ToArray();
                        } else {
                            WriteLine("Single point zebra");
                            // we have a single point for a zebra, if it's on a road, we'll render it...
                            var pos = _conv.ToBlock(way.Nodes[0].Lat, way.Nodes[0].Long);
                            RoadPoint zebraPoint;
                            if (!roadPoints.TryGetValue(pos, out zebraPoint)) {
                                continue;
                            }

                            var segment = zebraPoint.Segments.First();
                            // we create a line across the road to attempt to draw the zebra on from...
                            var angle = Math.Atan2(
                                segment.Start.Lat - segment.End.Lat,
                                segment.Start.Long - segment.End.Long
                            );

                            int width = GetRoadStyle(segment.Way).Width;
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
                            SaveBlocks();
                        }
                    }
                }
            }
            SaveBlocks();

            return roadPoints;
        }

        struct RoadPoint {
            public readonly List<RoadSegment> Segments;
            public int Height;
            public bool IsSidewalk;

            public RoadPoint(int height, bool isSidewalk) {
                Segments = new List<RoadSegment>();
                Height = height;
                IsSidewalk = isSidewalk;
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

            private void AddRoadPoint(Dictionary<BlockPosition, RoadPoint> roadPoints, BlockPosition position, RoadSegment segment, int height, bool isSidewalk = false) {
                RoadPoint roadPoint;
                if (!roadPoints.TryGetValue(position, out roadPoint)) {
                    roadPoints[position] = roadPoint = new RoadPoint(height, isSidewalk);
                } else {
                    roadPoint.IsSidewalk = isSidewalk;
                }
                roadPoint.Segments.Add(segment);
            }

            public void Render(Dictionary<BlockPosition, RoadPoint> roadPoints) {
                var start = Way.Nodes[0];
                int initialWidth = Width;
                if (_leftEdgeId != null) {
                    initialWidth += SidewalkWidth;
                }
                if (_rightEdgeId != null) {
                    initialWidth += SidewalkWidth;
                }
                for (int i = 1; i < Way.Nodes.Length; i++) {
                    var from = _conv.ToBlock(start.Lat, start.Long);
                    var to = _conv.ToBlock(Way.Nodes[i].Lat, Way.Nodes[i].Long);
                    var curSegment = new RoadSegment(Way, start, Way.Nodes[i]);

                    var xDelta = Math.Abs(from.X - to.X);
                    var yDelta = Math.Abs(from.Z - to.Z);
                    var targetWidth = initialWidth;
                    if (targetWidth != 1) {
                        // adjust width based on angle...
                        var angle = Math.Atan2(xDelta, yDelta);
                        if (xDelta < yDelta) {
                            targetWidth = (int)Math.Round(targetWidth * Math.Cos(angle));
                        } else {
                            targetWidth = (int)Math.Round(targetWidth * Math.Sin(angle));
                        }
                    }

                    foreach (var point in PlotLine(from.X, from.Z, to.X, to.Z, targetWidth)) {
                        if (_conv.IsValidPoint(point.Block) && point.Block.X > 1 && point.Block.Z > 1) {
                            RoadPoint existingRoad;
                            bool isIntersection = roadPoints.TryGetValue(point.Block, out existingRoad);
                            if (isIntersection) {
                                bool skip = false;
                                foreach (var segment in existingRoad.Segments) {
                                    if (segment.Way == Way ||
                                        (segment.Way.Name != null && segment.Way.Name == Way.Name)) {
                                        AddRoadPoint(roadPoints, point.Block, curSegment, existingRoad.Height);
                                        skip = true;
                                        break;
                                    } else if (!existingRoad.IsSidewalk && GetRoadWidth(segment.Way) > Width) {
                                        AddRoadPoint(roadPoints, point.Block, curSegment, existingRoad.Height);
                                        skip = true;
                                        break;
                                    }
                                }

                                if (skip) {
                                    continue;
                                }
                            }

                            int height;
                            if (isIntersection) {
                                // use the existing tracked height, in case we added a raised sidewalk,
                                // rather than the current height.
                                height = existingRoad.Height;
                            } else {
                                height = _bm.GetHeight(point.Block.X, point.Block.Z) + 1;
                            }

                            if (_leftEdgeId != null && point.Column < SidewalkWidth) {
                                if (!isIntersection) {
                                    _bm.SetID(point.Block.X, height, point.Block.Z, _leftEdgeId.Value);
                                    _bm.SetData(point.Block.X, height, point.Block.Z, 0);
                                    AddRoadPoint(roadPoints, point.Block, curSegment, height, true);
                                    ClearPlantLife(_bm, point.Block, height + 1);
                                }
                            } else if (_rightEdgeId != null && point.Column >= targetWidth - SidewalkWidth) {
                                if (!isIntersection) {
                                    _bm.SetID(point.Block.X, height, point.Block.Z, _rightEdgeId.Value);
                                    _bm.SetData(point.Block.X, height, point.Block.Z, 0);
                                    AddRoadPoint(roadPoints, point.Block, curSegment, height, true);
                                    ClearPlantLife(_bm, point.Block, height + 1);
                                }
                            } else {
                                if (DrawRoadPoint(point, height)) {
                                    AddRoadPoint(roadPoints, point.Block, curSegment, height);
                                    ClearPlantLife(_bm, point.Block, height);
                                }
                            }

                        }
                    }

                    start = Way.Nodes[i];
                    //WriteLine("From {0},{1} to {2},{3}", from.X, from.Z, to.X, to.Z);
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
                            width = 2;
                            id = BlockType.GRAVEL;
                            break;
                        case OsmReader.RoadType.Residential:
                        case OsmReader.RoadType.Primary:
                        case OsmReader.RoadType.Secondary:
                        case OsmReader.RoadType.Trunk:
                            width = (way.Lanes ?? 2) * 3;
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
                            width = (way.Lanes ?? 2) * 4;
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

        struct Rect : IEquatable<Rect> {
            public BlockPosition UpperLeft, BottomRight;

            public bool Equals(Rect other) {
                return other.UpperLeft.Equals(UpperLeft) && other.BottomRight.Equals(BottomRight);
            }

            public override bool Equals(object obj) {
                if (obj is Rect) {
                    return Equals((Rect)obj);
                }
                return false;
            }

            public override int GetHashCode() {
                return UpperLeft.GetHashCode() ^ UpperLeft.GetHashCode();
            }
        }

        class IntersectionInfo {
            public readonly HashSet<Rect> Areas;
            public readonly List<SegmentIntersection> Segments;

            public IntersectionInfo() {
                Areas = new HashSet<Rect>();
                Segments = new List<SegmentIntersection>();
            }
        }

        class IntersectionBoundsFinder {
            private readonly Dictionary<BlockPosition, RoadPoint> _roadPoints;
            private readonly Stack<BlockPosition> _todo = new Stack<BlockPosition>();
            private readonly HashSet<BlockPosition> _visited = new HashSet<BlockPosition>();
            private readonly HashSet<RoadSegment> _segments = new HashSet<RoadSegment>();

            public IntersectionBoundsFinder(Dictionary<BlockPosition, RoadPoint> roadPoints) {
                _roadPoints = roadPoints;
            }

            public Rect FindBounds(BlockPosition start) {
                _todo.Push(start);
                int top = start.Z, bottom = start.Z, left = start.X, right = start.X;
                do {
                    var pos = _todo.Pop();
                    if (CheckIntersectionAndQueue(new BlockPosition(pos.X, pos.Z - 1))) {
                        top = Math.Min(top, pos.Z - 1);
                    }
                    if (CheckIntersectionAndQueue(new BlockPosition(pos.X, pos.Z + 1))) {
                        bottom = Math.Max(bottom, pos.Z + 1);
                    }
                    if (CheckIntersectionAndQueue(new BlockPosition(pos.X - 1, pos.Z))) {
                        left = Math.Min(left, pos.X - 1);
                    }
                    if (CheckIntersectionAndQueue(new BlockPosition(pos.X + 1, pos.Z))) {
                        right = Math.Max(right, pos.X + 1);
                    }
                } while (_todo.Count > 0);
                return new Rect() {
                    UpperLeft = new BlockPosition(left, top),
                    BottomRight = new BlockPosition(right, bottom)
                };
            }

            public IEnumerable<RoadSegment> Segments {
                get {
                    return _segments;
                }
            }

            public bool HasSegments { get { return _segments.Count > 0; } }

            private bool CheckIntersectionAndQueue(BlockPosition position) {
                RoadPoint roadPoint;
                if (_roadPoints.TryGetValue(position, out roadPoint) &&
                    roadPoint.Segments.Count > 1 &&
                    !_visited.Contains(position)) {
                    for (int i = 0; i < roadPoint.Segments.Count; i++) {
                        _segments.Add(roadPoint.Segments[i]);
                    }
                    _visited.Add(position);
                    _todo.Push(position);
                    return true;
                }
                return false;
            }

            public void Clear() {
                _todo.Clear();
                _visited.Clear();
                _segments.Clear();
            }
        }

        /// <summary>
        /// Stack allocated frugal string set for things which are frequently set of 1 strings.
        /// </summary>
        struct FrugalStringSet {
            private object _values;

            public void Add(string value) {
                if (_values == null) {
                    _values = value;
                } else if (_values is string) {
                    if (((string)_values) != value) {
                        _values = new HashSet<string>() {
                            (string)_values,
                            value
                        };
                    }
                } else {
                    ((HashSet<string>)_values).Add(value);
                }

            }

            public bool Empty {
                get {
                    return _values == null;
                }
            }

            public string AsString() {
                return _values as string;
            }

            public string[] ToArray() {
                if (_values is string) {
                    return new[] { (string)_values };
                } else if (_values is HashSet<string>) {
                    return ((HashSet<string>)_values).ToArray();
                }
                return Array.Empty<string>();
            }
        }

        private void DrawSigns(Dictionary<BlockPosition, RoadPoint> roadPoints) {
            List<string> names = new List<string>(4);
            IntersectionBoundsFinder boundsFinder = new IntersectionBoundsFinder(roadPoints);
            int cur = 0;
            foreach (var intersection in _reader.RoadsByNode.OrderBy(x => MapOrder(_conv, x.Key))) {
                if ((++cur % 200) == 0) {
                    SaveBlocks();
                }

                if (intersection.Value.Count > 1) { // this node represents an intersection of 2 or more ways
                    // find the bounds of the intersecting roads...
                    var pos = _conv.ToBlock(intersection.Key.Lat, intersection.Key.Long);
                    boundsFinder.Clear();
                    var x = _conv.ToBlock(47.6172547, -122.3025742);
                    var region = boundsFinder.FindBounds(pos);

                    // see if we have a special type of sign...
                    OsmReader.SignType signType;
                    _reader.Signs.TryGetValue(intersection.Key, out signType);
                    if (!boundsFinder.HasSegments) {
                        continue;
                    }
                    // Where can have intersections show up in different ways...  
                    //          |
                    //  ----A---B----A----
                    //          |
                    var segments = boundsFinder.Segments.ToArray();
                    var intersectionPoint = intersection.Key;
                    // We'll draw traffic lights if we have 4 or less roads and one of the roads
                    // is aligned on the grid.  This handles the simple case of:
                    //       |                                        X/
                    //  -----+-----   and the complex case of      ---+---
                    //       |                                       /
                    // Here we can clearly draw the lights in the left case, in the right case we can
                    // also draw them.  We cannot currently mix and match overhead lights and post lights
                    // because it's pretty all or nothing - we'll end up with conflicting locations, because
                    // we put the overhead lights across the street and extendng out, and we put the pole
                    // lights next to the road, so they'd both want to go at location X
                    bool drawLights = ShouldDrawOverheadLights(intersection, signType, segments);
                    for (int i = 0; i < segments.Length; i++) {
                        var segment = segments[i];
                        if (segment.Way.Name == null) {
                            continue;
                        }
                        int? angle = AngleFromIntersection(segment, intersectionPoint);
                        if (angle == null) {
                            continue;
                        }

                        Direction direction = AngleToDirection(angle.Value);
                        names.Clear();
                        for (int j = 0; j < segments.Length; j++) {
                            var otherSegment = segments[j];

                            if (otherSegment.Way.Name == null ||
                                otherSegment.Way == segment.Way ||
                                otherSegment.Way.Name == segment.Way.Name) {
                                continue;
                            }
                            var otherAngle = AngleFromIntersection(otherSegment, intersectionPoint);
                            if (otherAngle == null) {
                                continue;
                            }
                            if (Math.Abs(otherAngle.Value - angle.Value) >= 180 - 45 &&
                                Math.Abs(otherAngle.Value - angle.Value) <= 180 + 45) {
                                // Don't sign different roads acros the street...
                                continue;
                            }
                            if (drawLights &&
                                (direction != Direction.North && direction != Direction.South && direction != Direction.East && direction != Direction.West)) {
                                var dir2 = AngleToDirection(otherAngle.Value);
                                direction = AlignTrafficLightDirection(direction, dir2);
                            }
                            // TODO: Track directions here and add arrows if we have multiple signs...
                            if (!names.Contains(otherSegment.Way.Name)) { 
                                names.Add(otherSegment.Way.Name);
                            }
                        }

                        if (names.Count == 0) {
                            continue;
                        }

                        if (drawLights) {
                            // We want signs facing on intersecting roads, parallel to the intersecting roads...
                            DrawTrafficLight(roadPoints, names, segment.Way, pos, angle.Value, direction);
                        } else {
                            DrawRoadSign(signType, names, roadPoints, pos, angle.Value, segment.Way);
                        }
                    }
                }
            }
        }

        private static bool ShouldDrawOverheadLights(KeyValuePair<OsmReader.Node, List<OsmReader.Way>> intersection, OsmReader.SignType signType, RoadSegment[] segments) {
            bool drawLights = signType == OsmReader.SignType.TrafficSignal && segments.Length <= 4;
            if (drawLights) {
                for (int i = 0; i < segments.Length; i++) {
                    var segment = segments[i];
                    Direction? direction = DirectionFromIntersection(segment, intersection.Key);
                    if (direction == null) {
                        continue;
                    }
                    if (!(direction == Direction.North || direction == Direction.South || direction == Direction.East || direction == Direction.West)) {
                        drawLights = false;
                        break;
                    }
                }
            }

            return drawLights;
        }

        private static Direction AlignTrafficLightDirection(Direction direction, Direction dir2) {
            switch (direction) {
                case Direction.NorthEast:
                    if (dir2 == Direction.North || dir2 == Direction.South) {
                        direction = Direction.East;
                    } else {
                        direction = Direction.North;
                    }
                    break;
                case Direction.SouthEast:
                    if (dir2 == Direction.North || dir2 == Direction.South) {
                        direction = Direction.East;
                    } else {
                        direction = Direction.South;
                    }
                    break;
                case Direction.NorthWest:
                    if (dir2 == Direction.North || dir2 == Direction.South) {
                        direction = Direction.West;
                    } else {
                        direction = Direction.North;
                    }
                    break;
                case Direction.SouthWest:
                    if (dir2 == Direction.North || dir2 == Direction.South) {
                        direction = Direction.West;
                    } else {
                        direction = Direction.South;
                    }
                    break;
                case Direction.EastNorthEast:
                case Direction.EastSouthEast:
                    direction = Direction.East;
                    break;
                case Direction.NorthNorthWest:
                case Direction.NorthNorthEast:
                    direction = Direction.North;
                    break;
                case Direction.SouthSouthWest:
                case Direction.SouthSouthEast:
                    direction = Direction.South;
                    break;
                case Direction.WestNorthWest:
                case Direction.WestSouthWest:
                    direction = Direction.West;
                    break;
            }

            return direction;
        }

        private void DrawTrafficLight(Dictionary<BlockPosition, RoadPoint> roadPoints, List<string> names, OsmReader.Way way, BlockPosition intersection, int angle, Direction direction) {
            Direction lightDir = Direction.East, walkBackDir;
            switch (direction) {
                case Direction.North:
                    lightDir = Direction.East;
                    walkBackDir = Direction.West;
                    break;
                case Direction.South:
                    lightDir = Direction.West;
                    walkBackDir = Direction.East;
                    break;
                case Direction.East:
                    lightDir = Direction.South;
                    walkBackDir = Direction.North;
                    break;
                case Direction.West:
                    lightDir = Direction.North;
                    walkBackDir = Direction.South;
                    break;
                default:
                    throw new InvalidOperationException();
            }
            var location = GetSignNonRoadPoint(roadPoints, intersection, angle + 90, walkBackDir);
            if (location == null) {
                return;
            }
            DrawTrafficLight(
                names,
                location.Value,
                lightDir,
                GetNumberOfTrafficLights(way),
                way.Sidewalk != OsmReader.Sidewalk.None
            );
        }

        private static Direction? DirectionFromIntersection(RoadSegment segment, OsmReader.Node intersectionPoint) {
            var angle = AngleFromIntersection(segment, intersectionPoint);
            if (angle != null) {
                return AngleToDirection(angle.Value);
            }
            return null;
        }

        private static int? AngleFromIntersection(RoadSegment segment, OsmReader.Node intersectionPoint) {
            OsmReader.Node other = GetNonIntersectionPoint(segment, intersectionPoint);
            if (other != null) {
                return GetAngleRelativeToIntersection(intersectionPoint, other);
            }
            return null;
        }

        private static OsmReader.Node GetNonIntersectionPoint(RoadSegment segment, OsmReader.Node intersectionPoint) {
            OsmReader.Node other;
            if (segment.Start == intersectionPoint) {
                other = segment.End;
            } else if (segment.End == intersectionPoint) {
                other = segment.Start;
            } else {
                //WriteLine("Weird intersecting road not at intersection: {0}", segment.Way.Name, segment.Start, segment.End);
                other = null;
            }

            return other;
        }

        private static Direction AngleToDirection(int angle) {
            Direction direction;
            var angleQuadrant = (int)((angle + 360 / 32) / 22.5) % 16; //16 - (((angle + 45 / 4) * 2 / 45 * 4) % 16);
            switch (angleQuadrant) {
                case 0: direction = Direction.East; break;
                case 1: direction = Direction.EastNorthEast; break;
                case 2: direction = Direction.NorthEast; break;
                case 3: direction = Direction.NorthNorthEast; break;
                case 4: direction = Direction.North; break;
                case 5: direction = Direction.NorthNorthWest; break;
                case 6: direction = Direction.NorthWest; break;
                case 7: direction = Direction.WestNorthWest; break;
                case 8: direction = Direction.West; break;
                case 9: direction = Direction.WestSouthWest; break;
                case 10: direction = Direction.SouthWest; break;
                case 11: direction = Direction.SouthSouthWest; break;
                case 12: direction = Direction.South; break;
                case 13: direction = Direction.EastSouthEast; break;
                case 14: direction = Direction.SouthEast; break;
                case 15: direction = Direction.SouthSouthEast; break;
                default: throw new InvalidOperationException();
            }

            return direction;
        }

        private static int GetAngleRelativeToIntersection(KeyValuePair<OsmReader.Node, List<OsmReader.Way>> intersection, OsmReader.Node other) {
            // get the angle of the segment relative to the intersection, the angle will be:
            //      0 if road runs east from the intersection
            //      90 if roads south from intersection
            //      180 if road runs west from the intersection
            //      270 if the road runs north from the intersection
            var angle = (int)(Math.Atan2(
                other.Lat - intersection.Key.Lat,
                other.Long - intersection.Key.Long
            ) * 180 / Math.PI);
            while (angle < 0) {
                angle += 360;
            }
            while (angle >= 360) {
                angle -= 360;
            }

            return angle;
        }

        private static int GetAngleRelativeToIntersection(OsmReader.Node intersection, OsmReader.Node other) {
            // get the angle of the segment relative to the intersection, the angle will be:
            //      0 if road runs east from the intersection
            //      90 if roads south from intersection
            //      180 if road runs west from the intersection
            //      270 if the road runs north from the intersection
            var angle = (int)(Math.Atan2(
                other.Lat - intersection.Lat,
                other.Long - intersection.Long
            ) * 180 / Math.PI);
            while (angle < 0) {
                angle += 360;
            }
            while (angle >= 360) {
                angle -= 360;
            }

            return angle;
        }

        private void DrawRoadSign(OsmReader.SignType signType, List<string> name, Dictionary<BlockPosition, RoadPoint> roadPoints, BlockPosition intersection, int angle, OsmReader.Way way) {
            Direction direction = AngleToDirection(angle);

            var target =  GetSignNonRoadPoint(roadPoints, intersection, angle, direction);
            if (target == null) {
                return;
            }

            switch (signType) {
                case OsmReader.SignType.TrafficSignal:
                case OsmReader.SignType.Stop:
                    DrawTrafficSign(signType, name, direction, target.Value);
                    break;
                default:
                    DrawSimpleSign(name, direction, target.Value);
                    break;
            }
        }

        private static BlockPosition? GetSignNonRoadPoint(Dictionary<BlockPosition, RoadPoint> roadPoints, BlockPosition intersection, int angle, Direction direction) {
            BlockPosition target;
            double adjAngle = ((double)angle + 20) / 180 * Math.PI;
            var destX = (int)(intersection.X + Math.Cos(adjAngle) * 100);
            var destZ = (int)(intersection.Z - Math.Sin(adjAngle) * 100);

            target = intersection;
            foreach (var point in PlotLineOrdered(intersection.X, intersection.Z, destX, destZ)) {
                if (!roadPoints.ContainsKey(point.Block)) {
                    target = point.Block;
                    break;
                }
            }
            if (target.Equals(intersection)) {
                return null;
            }

            switch (direction) {
                case Direction.EastNorthEast:
                case Direction.NorthNorthEast:
                case Direction.NorthEast:
                case Direction.East:
                    target = WalkBackPoint(roadPoints, -1, 1, target);
                    break;
                case Direction.SouthSouthEast:
                case Direction.EastSouthEast:
                case Direction.South:
                case Direction.SouthEast:
                    target = WalkBackPoint(roadPoints, -1, -1, target);
                    break;
                case Direction.WestNorthWest:
                case Direction.NorthNorthWest:
                case Direction.NorthWest:
                case Direction.North:
                    target = WalkBackPoint(roadPoints, 1, 1, target);
                    break;
                case Direction.WestSouthWest:
                case Direction.SouthSouthWest:
                case Direction.West:
                case Direction.SouthWest:
                    target = WalkBackPoint(roadPoints, 1, -1, target);
                    break;
                default:
                    throw new ArgumentException(nameof(direction));
            }
            return target;
        }

        private static void MergeLists(Dictionary<Rect, IntersectionInfo> lists, Rect removing, IntersectionInfo existing, IntersectionInfo other) {
            existing.Areas.UnionWith(other.Areas);
            existing.Segments.AddRange(other.Segments);
            lists[removing] = existing;
            // Now we need to merge other.Areas in as well
            foreach (var area in other.Areas) {
                IntersectionInfo info = lists[area];
                if (info != existing) {
                    MergeLists(lists, area, existing, info);
                }
            }
        }

        private static int GetNumberOfTrafficLights(OsmReader.Way way) {
            if (way.OneWay) {
                return way.Lanes ?? 1;
            }
            return (way.Lanes ?? 2) / 2;
        }

        private void DrawSimpleSign(List<string> name, Direction direction, BlockPosition target) {
            var height = _bm.GetHeight(target.X, target.Z);
            var block = new AlphaBlock(BlockType.SIGN_POST);
            var ent = block.GetTileEntity() as TileEntitySign;
            SetSignName(name, ent);

            if (_conv.IsValidPoint(target)) {
                _bm.SetBlock(target.X, height + 1, target.Z, block);
                _bm.SetData(target.X, height + 1, target.Z, (int)direction);
            }
        }

        private void DrawTrafficSign(OsmReader.SignType signType, List<string> name, Direction direction, BlockPosition target) {
            var height = _bm.GetHeight(target.X, target.Z);
            const int StopSignHeight = 4;
            WriteLine("Sign {0} {1}", target, signType);
            for (int i = 0; i < StopSignHeight; i++) {
                _bm.SetID(target.X, height + i, target.Z, BlockType.COBBLESTONE_WALL);
                _bm.SetData(target.X, height + i, target.Z, 0);
            }

            AlphaBlock block;
            if (direction == Direction.North || direction == Direction.South || direction == Direction.East || direction == Direction.West) {
                block = new AlphaBlock(BlockType.WALL_SIGN);
            } else {
                block = new AlphaBlock(BlockType.SIGN_POST);
            }
            var ent = block.GetTileEntity() as TileEntitySign;
            SetSignName(name, ent);
            int signX = target.X, signZ = target.Z;
            switch (direction) {
                case Direction.NorthWest: signX--; signZ--; break;
                case Direction.WestSouthWest:
                case Direction.WestNorthWest:
                case Direction.West: signX--; break;
                case Direction.NorthEast: signX++; signZ--; break;
                case Direction.EastNorthEast:
                case Direction.EastSouthEast:
                case Direction.East: signX++; break;
                case Direction.SouthEast: signX++; signZ++; break;
                case Direction.SouthSouthWest:
                case Direction.SouthSouthEast:
                case Direction.South: signZ++; break;
                case Direction.SouthWest: signX--; signZ++; break;
                case Direction.NorthNorthWest:
                case Direction.NorthNorthEast:
                case Direction.North: signZ--; break;
            }
            if (_conv.IsValidPoint(target)) {
                if (direction == Direction.North || direction == Direction.South || direction == Direction.East || direction == Direction.West) {
                    int data;
                    switch (direction) {
                        case Direction.North: data = 2; break;
                        case Direction.South: data = 3; break;
                        case Direction.West: data = 4; break;
                        case Direction.East: data = 5; break;
                        default:
                            throw new InvalidOperationException();
                    }
                    _bm.SetBlock(signX, height + 2, signZ, block);
                    _bm.SetData(signX, height + 2, signZ, data);
                } else {
                    var signHeight = _bm.GetHeight(signX, signZ);
                    _bm.SetBlock(signX, signHeight, signZ, block);
                    _bm.SetData(signX, signHeight, signZ, (int)direction);
                }
            }

            AlphaBlock bannerBlock = new AlphaBlock(BlockType.STANDING_BANNER);
            var bannerEnt = bannerBlock.GetTileEntity() as TileEntityBanner;
            if (signType == OsmReader.SignType.Stop) {
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

        private void DrawTrafficLight(List<string> name, BlockPosition target, Direction direction, int lanes, bool sidewalk) {
            var height = _bm.GetHeight(target.X, target.Z);
            const int TrafficLightHeight = 8;
            WriteLine("Sign {0} TrafficLight", target);
            // draw the vertical traffic light pieces...
            // We start with a piece of polished andisite, then an anvil, and have an
            // overlapping armor stand to look like cross walk buttons.
            // Disabled because it currently looks weird when ported to Bedrock addition
            var entities = _bm.GetChunk(target.X, height, target.Z).Entities;
            var armor = new TypedEntity("minecraft:armor_stand");
            armor.Position = new Vector3() { X = target.X + .5, Y = height, Z = target.Z + .5 };
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
                case Direction.West: xDelta = -1; break;
                case Direction.South: zDelta = 1; break;
                case Direction.North: zDelta = -1; break;
                case Direction.East: xDelta = 1; break;
                default: throw new NotImplementedException();
            }

            // draw the 1st three cobble stone horizontal pieces
            int xLoc = target.X + xDelta, zLoc = target.Z + zDelta;
            int initialLength = 1;
            if (sidewalk) {
                initialLength += 3;
            }
            for (int i = 0; i < initialLength; i++) {
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
                int horizontalLength = 3;
                if (lane != lanes - 1 && lane != 0) {
                    horizontalLength = 2;
                }
                for (int i = 0; i < horizontalLength; i++) {
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

        private static BlockPosition WalkBackPoint(Dictionary<BlockPosition, RoadPoint> roadPoints, int inX, int inZ, BlockPosition target) {
            var cur = target;
            int count = 0;

            // Now try and move back close to the road, handling each direction one by one...
            count = 0;
            BlockPosition next = cur;
            bool moved;
            do {
                moved = false;
                if (!roadPoints.ContainsKey(new BlockPosition(next.X + inX, next.Z))) {
                    next = new BlockPosition(next.X + inX, next.Z);
                    moved = true;
                }
                if (!roadPoints.ContainsKey(new BlockPosition(next.X, next.Z + inZ))) {
                    next = new BlockPosition(next.X, next.Z + inZ);
                    moved = true;
                }
            } while (moved && ++count < 100);
            if (count == 100) {
                return cur;
            }
            return next;
        }

        private static int GetRoadWidth(OsmReader.Way way) {
            return (way.Lanes ?? 2) * 4;
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
                }
                
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
            var xDelta = Math.Abs(x0 - x1);
            var yDelta = Math.Abs(y0 - y1);
            if (yDelta < xDelta) {
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

        static IEnumerable<LinePosition> PlotLineOrdered(int x0, int y0, int x1, int y1, int width = 1) {
            var xDelta = Math.Abs(x0 - x1);
            var yDelta = Math.Abs(y0 - y1);
            if (yDelta < xDelta) {
                if (x0 > x1) {
                    return PlotLineLow(x1, y1, x0, y0, width).Reverse();
                } else {
                    return PlotLineLow(x0, y0, x1, y1, width);
                }
            } else if (y0 > y1) {
                return PlotLineHigh(x1, y1, x0, y0, width).Reverse();
            }
            return PlotLineHigh(x0, y0, x1, y1, width);
        }

        static IEnumerable<LinePosition> PlotLineHigh(int x0, int y0, int x1, int y1, int width = 1, bool overlap = false, int index = 0) {
            int dx = x1 - x0;
            int dy = y1 - y0;
            int xi = 1;
            int startFixUp = width / 2;
            if (dx < 0) {
                xi = -1;
                dx = -dx;
            } else {
                startFixUp = (width - 1) - (width / 2);
            }
            int D = 2 * dy - dx;
            // fix up our start point so that we're centering on thickness...
            for (int i = 0; i < width / 2; i++) {
                x0--;
                x1--;
                if (D >= 0) {
                    y0 += xi;
                    y1 += xi;
                    D -= dx * 2;
                }
                D += dy * 2;
            }
            D = 2 * dx - dy;
            int x = x0;

            for (int y = y0; y <= y1; y++) {
                yield return new LinePosition(new BlockPosition(x, y), index, y);
                if (D >= 0) {
                    if (overlap) {
                        if (xi == -1) {
                            yield return new LinePosition(new BlockPosition(x - 1, y), index, y);
                        } else {
                            yield return new LinePosition(new BlockPosition(x, y + 1), index, y);
                        }
                    }
                    x = x + xi;
                    D -= 2 * dy;
                }
                D += 2 * dx;
            }

            // then draw the additional lines...
            D = 2 * dy - dx;
            if (width != 1) {
                for (int i = 1; i < width; i++) {
                    x0++;
                    x1++;
                    bool drawOverlap = false;
                    if (D >= 0) {
                        y0-=xi;
                        y1-=xi;
                        D -= 2 * dx;
                        drawOverlap = true;
                    }
                    D += 2 * dy;
                    foreach (var value in PlotLineHigh(x0, y0, x1, y1, 1, drawOverlap, i)) {
                        yield return value;
                    }
                }
            }
        }

        static IEnumerable<LinePosition> PlotLineLow(int x0, int y0, int x1, int y1, int width = 1, bool overlap = false, int index = 0) {
            int dx = x1 - x0;
            int dy = y1 - y0;
            int yi = 1;
            int startFixUp = width / 2;
            if (dy < 0) {
                yi = -1;
                dy = -dy;
            } else {
                startFixUp = (width - 1) - (width / 2);
            }
            int D = 2 * dx - dy;
            // fix up our start point so that we're centering on thickness...
            for (int i = 0; i < startFixUp; i++) {
                y0--;
                y1--;
                if (D >= 0) {
                    x0 += yi;
                    x1 += yi;
                    D -= dy * 2;
                }
                D += dx * 2;
            }

            // then draw the line...
            int y = y0;
            D = 2 * dy - dx;
            for (int x = x0; x <= x1; x++) {
                yield return new LinePosition(new BlockPosition(x, y), index, x);
                if (D >= 0) {
                    if (overlap) {
                        if (yi == -1) {
                            yield return new LinePosition(new BlockPosition(x, y - 1), index, x);
                        } else {
                            yield return new LinePosition(new BlockPosition(x + 1, y), index, x);
                        }
                    }
                    y = y + yi;
                    D -= 2 * dx;
                }
                D += 2 * dy;
            }

            // then draw the additional lines...
            D = 2 * dx - dy;
            if (width != 1) {
                for (int i = 1; i < width; i++) {
                    y0++;
                    y1++;
                    bool drawOverlap = false;
                    if (D >= 0) {
                        x0 -= yi;
                        x1 -= yi;
                        D -= 2 * dy;
                        drawOverlap = true;
                    }
                    D += 2 * dx;
                    foreach (var value in PlotLineLow(x0, y0, x1, y1, 1, drawOverlap, i)) {
                        yield return value;
                    }
                }
            }
        }
    }
}
