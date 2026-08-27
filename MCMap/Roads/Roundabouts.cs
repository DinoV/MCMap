using Substrate;
using System;
using System.Collections.Generic;
using System.Linq;

namespace MinecraftMapper {
    partial class Mapper {
        private static readonly string[] RoundaboutPlants = {
            AcquaticBlocks.ShortGrass,
            AcquaticBlocks.ShortGrass,
            AcquaticBlocks.ShortGrass,
            AcquaticBlocks.ShortGrass,
            AcquaticBlocks.Fern,
            AcquaticBlocks.Dandelion,
            AcquaticBlocks.Poppy,
            AcquaticBlocks.AzureBluet,
            AcquaticBlocks.Cornflower,
            AcquaticBlocks.OxeyeDaisy,
            AcquaticBlocks.Allium,
        };

        private void DrawRoundaboutCenters(Dictionary<BlockPosition, RoadPoint> roadPoints) {
            foreach (OsmReader.Way way in _reader.Ways.Values
                    .Where(item => item.Roundabout && item.Nodes.Length >= 3)
                    .OrderBy(item => MapOrder(_conv, item.Nodes[0]))) {
                RoadRenderer style = GetRoadStyle(way);
                if (style == null) {
                    continue;
                }

                var path = way.Nodes
                    .Select(node => _conv.ToBlock(node.Lat, node.Long))
                    .ToArray();
                int minX = path.Min(point => point.X);
                int maxX = path.Max(point => point.X);
                int minZ = path.Min(point => point.Z);
                int maxZ = path.Max(point => point.Z);
                double laneClearance = Math.Min(3.0, style.Width / 2.0 + 1.0);
                double minimumDistanceSquared = laneClearance * laneClearance;
                var innerFootprint = new HashSet<BlockPosition>();

                for (int z = minZ; z <= maxZ; z++) {
                    for (int x = minX; x <= maxX; x++) {
                        if (IsInsideRoundabout(x + 0.5, z + 0.5, path) &&
                                DistanceSquaredToRoundabout(x + 0.5, z + 0.5, path) >=
                                    minimumDistanceSquared) {
                            innerFootprint.Add(new BlockPosition(x, z));
                        }
                    }
                }

                HashSet<BlockPosition> island = CreateRegularRoundaboutIsland(
                    innerFootprint, path);
                if (island.Count == 0) {
                    continue;
                }

                double centerX = island.Average(point => point.X);
                double centerZ = island.Average(point => point.Z);
                int planted = 0;

                foreach (BlockPosition point in island) {
                    if (!_conv.IsValidPoint(point)) {
                        continue;
                    }

                    RoadPoint roadPoint;
                    int plantHeight;
                    if (roadPoints.TryGetValue(point, out roadPoint)) {
                        plantHeight = roadPoint.Height;
                        roadPoints.Remove(point);
                    } else {
                        plantHeight = GetHeight(point) + 1;
                    }
                    ClearPlantLife(_bm, point, plantHeight);

                    if (IsRoundaboutIslandEdge(point, island)) {
                        BlockFacing facing;
                        BlockStairShape shape;
                        GetRoundaboutStairState(
                            point, centerX, centerZ, out facing, out shape);
                        _bm.SetBlock(
                            point.X, plantHeight - 1, point.Z,
                            AcquaticBlocks.StoneStairs,
                            facing: facing,
                            half: BlockHalf.Lower,
                            shape: shape,
                            waterlogged: false);
                    } else {
                        _bm.SetBlock(point.X, plantHeight - 1, point.Z,
                            AcquaticBlocks.Dirt);
                        _bm.SetBlock(point.X, plantHeight, point.Z,
                            RoundaboutPlants[RoundaboutPlantIndex(way.Id, point)]);
                        planted++;
                    }
                }

                if (planted > 0) {
                    WriteLine("Roundabout {0}: planted {1} center blocks", way.Id, planted);
                }
            }
        }

        private static HashSet<BlockPosition> CreateRegularRoundaboutIsland(
                HashSet<BlockPosition> innerFootprint,
                IList<BlockPosition> polygon) {
            if (innerFootprint.Count == 0) {
                return innerFootprint;
            }

            int width = innerFootprint.Max(point => point.X) -
                innerFootprint.Min(point => point.X) + 1;
            int depth = innerFootprint.Max(point => point.Z) -
                innerFootprint.Min(point => point.Z) + 1;
            int size = Math.Min(width, depth);
            if ((size & 1) == 0) {
                size--;
            }
            if (size < 3) {
                return new HashSet<BlockPosition>();
            }

            int centerX = (int)Math.Round(
                innerFootprint.Average(point => point.X),
                MidpointRounding.AwayFromZero);
            int centerZ = (int)Math.Round(
                innerFootprint.Average(point => point.Z),
                MidpointRounding.AwayFromZero);

            // A square ring is visually stable in Minecraft. Shrink only when a
            // corner would actually leave the OSM roundabout polygon.
            while (size >= 3) {
                int radius = size / 2;
                bool fits = true;
                for (int z = centerZ - radius; z <= centerZ + radius && fits; z++) {
                    for (int x = centerX - radius; x <= centerX + radius; x++) {
                        if (!IsInsideRoundabout(x + 0.5, z + 0.5, polygon)) {
                            fits = false;
                            break;
                        }
                    }
                }
                if (fits) {
                    var island = new HashSet<BlockPosition>();
                    for (int z = centerZ - radius; z <= centerZ + radius; z++) {
                        for (int x = centerX - radius; x <= centerX + radius; x++) {
                            island.Add(new BlockPosition(x, z));
                        }
                    }
                    return island;
                }
                size -= 2;
            }

            return new HashSet<BlockPosition>();
        }

        private static bool IsRoundaboutIslandEdge(
                BlockPosition point, HashSet<BlockPosition> island) {
            return !island.Contains(new BlockPosition(point.X - 1, point.Z)) ||
                !island.Contains(new BlockPosition(point.X + 1, point.Z)) ||
                !island.Contains(new BlockPosition(point.X, point.Z - 1)) ||
                !island.Contains(new BlockPosition(point.X, point.Z + 1));
        }

        private static void GetRoundaboutStairState(
                BlockPosition point,
                double centerX,
                double centerZ,
                out BlockFacing facing,
                out BlockStairShape shape) {
            double xDistance = point.X - centerX;
            double zDistance = point.Z - centerZ;
            bool corner = Math.Abs(Math.Abs(xDistance) - Math.Abs(zDistance)) < 0.75;
            shape = corner ? BlockStairShape.OuterRight : BlockStairShape.Straight;

            if (corner) {
                if (xDistance < 0 && zDistance < 0) facing = BlockFacing.East;
                else if (xDistance > 0 && zDistance < 0) facing = BlockFacing.South;
                else if (xDistance < 0 && zDistance > 0) facing = BlockFacing.North;
                else facing = BlockFacing.West;
            } else if (Math.Abs(xDistance) > Math.Abs(zDistance)) {
                facing = xDistance < 0 ? BlockFacing.East : BlockFacing.West;
            } else {
                facing = zDistance < 0 ? BlockFacing.South : BlockFacing.North;
            }
        }

        private static int RoundaboutPlantIndex(long wayId, BlockPosition point) {
            ulong hash = unchecked((ulong)wayId);
            hash ^= unchecked((uint)point.X) * 0x9E3779B1UL;
            hash ^= unchecked((uint)point.Z) * 0x85EBCA77UL;
            hash ^= hash >> 33;
            hash *= 0xFF51AFD7ED558CCDUL;
            hash ^= hash >> 33;
            return (int)(hash % (ulong)RoundaboutPlants.Length);
        }

        private static bool IsInsideRoundabout(
                double x,
                double z,
                IList<BlockPosition> polygon) {
            bool inside = false;
            for (int current = 0, previous = polygon.Count - 1;
                    current < polygon.Count; previous = current++) {
                double currentZ = polygon[current].Z;
                double previousZ = polygon[previous].Z;
                if ((currentZ > z) == (previousZ > z)) {
                    continue;
                }
                double edgeX = (polygon[previous].X - polygon[current].X) *
                    (z - currentZ) / (previousZ - currentZ) + polygon[current].X;
                if (x < edgeX) {
                    inside = !inside;
                }
            }
            return inside;
        }

        private static double DistanceSquaredToRoundabout(
                double x,
                double z,
                IList<BlockPosition> polygon) {
            double minimum = Double.MaxValue;
            for (int current = 0, previous = polygon.Count - 1;
                    current < polygon.Count; previous = current++) {
                double distance = DistanceSquaredToSegment(
                    x, z,
                    polygon[previous].X, polygon[previous].Z,
                    polygon[current].X, polygon[current].Z);
                minimum = Math.Min(minimum, distance);
            }
            return minimum;
        }

        private static double DistanceSquaredToSegment(
                double x,
                double z,
                double startX,
                double startZ,
                double endX,
                double endZ) {
            double segmentX = endX - startX;
            double segmentZ = endZ - startZ;
            double lengthSquared = segmentX * segmentX + segmentZ * segmentZ;
            if (lengthSquared == 0) {
                double pointX = x - startX;
                double pointZ = z - startZ;
                return pointX * pointX + pointZ * pointZ;
            }

            double amount = ((x - startX) * segmentX + (z - startZ) * segmentZ) /
                lengthSquared;
            amount = Math.Max(0, Math.Min(1, amount));
            double nearestX = startX + amount * segmentX;
            double nearestZ = startZ + amount * segmentZ;
            double deltaX = x - nearestX;
            double deltaZ = z - nearestZ;
            return deltaX * deltaX + deltaZ * deltaZ;
        }
    }
}
