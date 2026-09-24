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
        private readonly NbtWorld _world;
        private readonly PositionConverter _conv;
        private readonly OsmReader _reader;
        private readonly AnvilBlockManager _bm;
        private readonly ModernEntityManager _modernEntities;
        private readonly StreamWriter _log;
        private readonly bool _dryRun = false;

        public Mapper(string log, string world, string osmXml,
                double upperLeftEastingUsFt, double upperLeftNorthingUsFt,
                int mcX, int mcZ,
                HashSet<long> ways, bool dryrun) {
            _world = NbtWorld.Open(world);
            _bm = _world.GetBlockManager() as AnvilBlockManager;
            _bm.AutoLight = true;
            if (log != null) {
                _log = new StreamWriter(new FileStream(log, FileMode.Create, FileAccess.ReadWrite));
            }
            var region = ((AnvilWorld)_world).GetRegionManager().GetRegionPath();
            var entityRegion = Path.Combine(Directory.GetParent(region).FullName, "entities");
            if (Directory.Exists(entityRegion)) {
                _modernEntities = new ModernEntityManager(entityRegion);
            }

            int maxChunkX = Int32.MinValue, maxChunkZ = Int32.MinValue;
            int minChunkX = Int32.MaxValue, minChunkZ = Int32.MaxValue;
            foreach (var file in Directory.GetFiles(region)) {
                var filenameInfo = Path.GetFileName(file).Split('.');
                int regionX = 0, regionZ = 0;
                if (filenameInfo.Length == 4 &&
                    filenameInfo[0] == "r" &&
                    int.TryParse(filenameInfo[1], out regionX) &&
                    int.TryParse(filenameInfo[2], out regionZ) &&
                    filenameInfo[3] == "mca") {
                    using (var regionFile = new Substrate.Core.RegionFile(file)) {
                        for (int localZ = 0; localZ < 32; localZ++) {
                            for (int localX = 0; localX < 32; localX++) {
                                if (!regionFile.HasChunk(localX, localZ)) {
                                    continue;
                                }

                                int chunkX = (regionX << RegionChunkManager.REGION_XLOG) + localX;
                                int chunkZ = (regionZ << RegionChunkManager.REGION_ZLOG) + localZ;
                                maxChunkX = Math.Max(chunkX, maxChunkX);
                                maxChunkZ = Math.Max(chunkZ, maxChunkZ);
                                minChunkX = Math.Min(chunkX, minChunkX);
                                minChunkZ = Math.Min(chunkZ, minChunkZ);
                            }
                        }
                    }
                }
            }

            if (minChunkX == Int32.MaxValue) {
                throw new InvalidDataException("The world contains no populated chunks.");
            }

            int worldX = minChunkX << _bm.ChunkXLog;
            int worldZ = minChunkZ << _bm.ChunkZLog;
            int worldMaxX = (maxChunkX + 1) << _bm.ChunkXLog;
            int worldMaxZ = (maxChunkZ + 1) << _bm.ChunkZLog;

            _conv = PositionConverter.FromProjectedRasterOrigin(
                    worldMaxX, worldMaxZ, upperLeftEastingUsFt, upperLeftNorthingUsFt,
                    mcX, mcZ);
            _reader = new OsmReader(osmXml, ways);
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

        private static void SetMappedBlock(
            BlockManager blocks, int x, int y, int z, int id, int data) {
            string name;
            Substrate.Nbt.TagNodeCompound properties;
            BlockInfo.GetModernBlockState(
                id, data, out name, out properties);
            blocks.SetBlock(x, y, z, name, properties);
        }

        public void MapIt() {
            DateTime startTime = DateTime.Now;
            _reader.ReadData();
            GC.Collect();
            DateTime dataProcessing = DateTime.Now;
            if (File.Exists("residential.csv")) {
                var residential = new CsvReader(new StreamReader("residential.csv"));
                ReadStories(residential, "Stories");
            }
            if (File.Exists("commercial.csv")) {
                var commercial = new CsvReader(new StreamReader("commercial.csv"));
                ReadStories(commercial, "NbrStories");
            }
            _reader.BuildingsByAddress.Clear();
            try {
                WriteLine("{0} roads", _reader.Ways.Count);

                DrawCoastlines();

                DrawPiers();

                DrawLandUses();

                DrawLeisures();

                DrawParkingLots();

                var roadPoints = DrawRoads();

                DrawRails();

                DrawRailroadCrossings(roadPoints);

                DrawStreetLamps(roadPoints);
                var buildingPoints = DrawBuildings();

                DrawWoodlands(roadPoints, buildingPoints);

                Save();

                DrawBarriers(buildingPoints);

                DrawPointFeatures(roadPoints, buildingPoints);

                DrawIconicSigns();

                foreach (var node in _reader.Amenities) {
                    switch(node.Amenity) {
                        case Amenity.BicycleParking:
                            var point = _conv.ToBlock(node.Lat, node.Long);
                            Console.WriteLine("Bike rack {0} {1}", point, node.Id);
                            DrawBikeRack(point, roadPoints);
                            break;
                    }
                }
                Save();
            } catch (Exception e) {
                Save();
                WriteLine(e);
            }
            DateTime done = DateTime.Now;
            WriteLine("All done, data processing {0}, total {1}", dataProcessing - startTime, done - startTime);
            Console.ReadLine();
        }

        private void DrawBikeRack(
                BlockPosition point, Dictionary<BlockPosition, RoadPoint> roadPoints) {
            const int RoadSearchRadius = 16;
            RoadSegment? nearestSegment = null;
            int nearestDistanceSquared = int.MaxValue;

            for (int x = point.X - RoadSearchRadius; x <= point.X + RoadSearchRadius; x++) {
                for (int z = point.Z - RoadSearchRadius; z <= point.Z + RoadSearchRadius; z++) {
                    RoadPoint roadPoint;
                    if (!roadPoints.TryGetValue(new BlockPosition(x, z), out roadPoint) ||
                            roadPoint.Segments.Count == 0) {
                        continue;
                    }

                    int xDistance = x - point.X;
                    int zDistance = z - point.Z;
                    int distanceSquared = xDistance * xDistance + zDistance * zDistance;
                    if (distanceSquared < nearestDistanceSquared) {
                        nearestDistanceSquared = distanceSquared;
                        nearestSegment = roadPoint.Segments[0];
                    }
                }
            }

            int xStep = 1;
            int zStep = 0;
            if (nearestSegment.HasValue) {
                var start = _conv.ToBlock(
                    nearestSegment.Value.Start.Lat, nearestSegment.Value.Start.Long);
                var end = _conv.ToBlock(
                    nearestSegment.Value.End.Lat, nearestSegment.Value.End.Long);
                if (Math.Abs(end.Z - start.Z) > Math.Abs(end.X - start.X)) {
                    xStep = 0;
                    zStep = 1;
                }
            }

            for (int i = 0; i < 3; i++) {
                int x = point.X + i * xStep;
                int z = point.Z + i * zStep;
                _bm.SetBlock(
                    x,
                    _bm.GetHeight(x, z),
                    z,
                    AcquaticBlocks.PaleOakFence,
                    north: zStep != 0 && i > 0,
                    east: xStep != 0 && i < 2,
                    south: zStep != 0 && i < 2,
                    west: xStep != 0 && i > 0
                );
            }
        }

        private void Save() {
            if (!_dryRun) {
                _world.Save();
            }
        }

        private int GetHeight(int x, int z) {
            return GetHeight(new BlockPosition(x, z));
        }

        private int GetHeight(BlockPosition signLoc) {
            var chunk = _bm.GetChunk(signLoc.X, 0, signLoc.Z);
            if (chunk == null) {
                return 0;
            }

            int minimumY = chunk.MinimumY;
            int maximumY = minimumY + chunk.Blocks.YDim - 1;
            var groundHeight = _bm.GetHeight(signLoc.X, signLoc.Z);
            groundHeight = Math.Min(groundHeight, maximumY);
            while (groundHeight >= minimumY) {
                var block = _bm.GetBlock(signLoc.X, groundHeight, signLoc.Z);
                if (block != null && block.ID != BlockType.AIR) {
                    return groundHeight;
                }

                // GetHeight doesn't always work so well :P
                groundHeight--;
            }

            return 0;
        }

    }
}
