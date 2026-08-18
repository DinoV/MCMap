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
        static int Main(string[] args) {
            string map = null, osmData = null, log = null, outputName = null;
            HashSet<long> ways = new HashSet<long>();
            List<PositionBase> positionBases = new List<PositionBase>();
            double? upperLeftEastingUsFt = null, upperLeftNorthingUsFt = null;
            int mcX = 0, mcZ = 0;
            bool dryrun = false, copy = false;
            foreach (var arg in args) {
                if (arg == "/?" || arg == "/help") {
                    Help();
                    return 0;
                } else if (arg == "/dryrun") {
                    dryrun = true;
                } else if (arg == "/copy") {
                    copy = true;
                } else if (arg.StartsWith("/name:")) {
                    outputName = arg.Substring("/name:".Length);
                } else if (arg.StartsWith("/log:")) {
                    log = arg.Substring("/log:".Length);
                } else if (arg.StartsWith("/map:")) {
                    map = arg.Substring("/map:".Length);
                } else if (arg.StartsWith("/osm:")) {
                    osmData = arg.Substring("/osm:".Length);
                } else if (arg.StartsWith("/way:")) {
                    long way;
                    if (!long.TryParse(arg.Substring("/way:".Length), out way)) {
                        Console.WriteLine("Bad way ID: " + arg.Substring("/way:".Length));
                        return 1;
                    }
                    ways.Add(way);
                } else if (arg.StartsWith("/pos:")) {
                    var coords = arg.Substring("/pos:".Length).Split(',');
                    double parsedNorth, parsedWest;
                    if (coords.Length != 2 ||
                        !double.TryParse(coords[0], out parsedWest) ||
                        !double.TryParse(coords[1], out parsedNorth)) {
                        Console.WriteLine("Expected /pos:<west>,<north>");
                        return 1;
                    }
                    upperLeftEastingUsFt = parsedWest;
                    upperLeftNorthingUsFt = parsedNorth;
                } else if (arg.StartsWith("/offset:")) {
                    var coords = arg.Substring("/offset:".Length).Split(',');
                    if (coords.Length != 2 ||
                        !int.TryParse(coords[0], out mcX) ||
                        !int.TryParse(coords[1], out mcZ)) {
                        Console.WriteLine("Expected /offset:X,Z");
                        return 1;
                    }

                }
            }
            if (map == null) {
                Console.WriteLine("map required");
                return 1;
            } else if (osmData == null) {
                Console.WriteLine("osm data required");
                return 1;
            } else if (!upperLeftNorthingUsFt.HasValue || !upperLeftNorthingUsFt.HasValue) {
                Console.WriteLine("Bounds need to be provided");
                return 1;
            }

            try {
                map = Path.GetFullPath(map);
            } catch (Exception e) {
                Console.WriteLine("Invalid map path: {0}", e.Message);
                return 1;
            }
            if (!Directory.Exists(map)) {
                Console.WriteLine("Map does not exist: {0}", map);
                return 1;
            }

            if (copy || outputName != null) {
                string outputMap;
                try {
                    outputMap = GetOutputMapPath(map, outputName);
                } catch (ArgumentException e) {
                    Console.WriteLine(e.Message);
                    return 1;
                }

                if (Directory.Exists(outputMap) || File.Exists(outputMap)) {
                    Console.WriteLine("Output map already exists: {0}", outputMap);
                    return 1;
                }

                Console.WriteLine("Copying map to {0}", outputMap);
                try {
                    CopyDirectory(map, outputMap);
                } catch (Exception e) {
                    Console.WriteLine("Failed to copy map to {0}: {1}", outputMap, e.Message);
                    return 1;
                }
                map = outputMap;
            }

            NbtWorld world;
            try {
                world = NbtWorld.Open(map);
            } catch (Exception e) {
                Console.WriteLine("Failed to open world {0}: {1}", map, e.Message);
                return 1;
            }

            var mapper = new Mapper(log, map, osmData, upperLeftEastingUsFt.Value, upperLeftNorthingUsFt.Value,
                mcX, mcZ, ways, dryrun);
            mapper.MapIt();
            return 0;
        }

        private static void Help() {
            Console.WriteLine("MCMap: ");
            Console.WriteLine(" /help                         - This help");
            Console.WriteLine(" /map:<map>                    - Specifies path to Minecraft Java Edition save directory");
            Console.WriteLine(" /copy                         - Copy the map to a new sibling world before rendering");
            Console.WriteLine(" /name:<worldName>             - Copy to this exact sibling world name; fails if it exists");
            Console.WriteLine(" /osm:<osm.xml>                - Specifies path to Open Street Map .xml file");
            Console.WriteLine(" /pos:<west>,<north>           - Specifies the projected bounds (west, north) outputted from lidar.py");
            Console.WriteLine(" /offset:X,Z                   - Provides an extra offset in Minecraft blocks that's applied from the lat,long");
            Console.WriteLine(" /way:<osmWay>                 - Limit rendering to these OSM ways; may be repeated");
        }

        private static string GetOutputMapPath(string sourceMap, string outputName) {
            var source = new DirectoryInfo(sourceMap);
            if (source.Parent == null) {
                throw new ArgumentException("The map path must have a parent directory.");
            }

            if (outputName != null) {
                if (String.IsNullOrWhiteSpace(outputName) ||
                        outputName == "." || outputName == ".." ||
                        Path.IsPathRooted(outputName) ||
                        Path.GetFileName(outputName) != outputName ||
                        outputName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) {
                    throw new ArgumentException(
                        "/name must contain one valid world folder name, not a path.");
                }
                return Path.Combine(source.Parent.FullName, outputName);
            }

            string baseName = source.Name + "-copy";
            string candidate = Path.Combine(source.Parent.FullName, baseName);
            for (int suffix = 2;
                    Directory.Exists(candidate) || File.Exists(candidate);
                    suffix++) {
                candidate = Path.Combine(
                    source.Parent.FullName, baseName + suffix);
            }
            return candidate;
        }

        private static void CopyDirectory(string sourcePath, string destinationPath) {
            bool destinationCreated = false;
            try {
                Directory.CreateDirectory(destinationPath);
                destinationCreated = true;

                foreach (string file in Directory.EnumerateFiles(sourcePath)) {
                    File.Copy(file, Path.Combine(
                        destinationPath, Path.GetFileName(file)));
                }
                foreach (string directory in Directory.EnumerateDirectories(sourcePath)) {
                    CopyDirectory(directory, Path.Combine(
                        destinationPath, Path.GetFileName(directory)));
                }
            } catch {
                if (destinationCreated && Directory.Exists(destinationPath)) {
                    Directory.Delete(destinationPath, true);
                }
                throw;
            }
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

        private static BlockFacing ToBlockFacing(FullDirection direction) {
            switch (direction) {
                case FullDirection.East: return BlockFacing.East;
                case FullDirection.West: return BlockFacing.West;
                case FullDirection.South: return BlockFacing.South;
                case FullDirection.North: return BlockFacing.North;
                default: throw new ArgumentOutOfRangeException("direction");
            }
        }

        private static BlockFacing ToBlockFacing(DoorDirection direction) {
            switch (direction) {
                case DoorDirection.East: return BlockFacing.East;
                case DoorDirection.South: return BlockFacing.South;
                case DoorDirection.West: return BlockFacing.West;
                case DoorDirection.North: return BlockFacing.North;
                default: throw new ArgumentOutOfRangeException("direction");
            }
        }

        private static BlockFacing WallFacingFromLegacyData(int data) {
            switch (data) {
                case 2: return BlockFacing.North;
                case 3: return BlockFacing.South;
                case 4: return BlockFacing.West;
                case 5: return BlockFacing.East;
                default: throw new ArgumentOutOfRangeException("data");
            }
        }

    }
}
