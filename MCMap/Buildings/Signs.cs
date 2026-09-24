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
        private HashSet<BlockPosition> DrawBuildingSign(OsmReader.Building building, int top, int left, int bottom, int right, Dictionary<BlockPosition, int> baseHeights, Dictionary<BlockPosition, int> buildingPoints) {
            var doors = DrawSignItems(building, top, left, bottom, right, baseHeights, buildingPoints);

            if (building.Street == null || building.HouseNumber == null) {
                return doors;
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
                                facing = DoorDirection.South;
                            } else {
                                facing = DoorDirection.North;
                            }
                        } else {
                            if (building1.Long < closestRoadPoint.Long) {
                                facing = DoorDirection.West;
                            } else {
                                facing = DoorDirection.East;
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

                // Put the address sign immediately outside the portion of this building nearest
                // the named road.  Measure against the complete road segments rather than just
                // their OSM nodes; the nearest node can be past a corner and select the wrong wall.
                BlockPosition buildingLoc;
                double frontageRoadX, frontageRoadZ;
                if (!TryFindClosestRoadFrontage(baseHeights.Keys, roads,
                        out buildingLoc, out frontageRoadX, out frontageRoadZ)) {
                    buildingLoc = _conv.ToBlock(buildingWallPoint.Lat, buildingWallPoint.Long);
                    frontageRoadX = roadLoc.X;
                    frontageRoadZ = roadLoc.Z;
                }

                var candidates = new[] {
                    new { Point = new BlockPosition(buildingLoc.X + 1, buildingLoc.Z), Direction = Direction.East },
                    new { Point = new BlockPosition(buildingLoc.X - 1, buildingLoc.Z), Direction = Direction.West },
                    new { Point = new BlockPosition(buildingLoc.X, buildingLoc.Z + 1), Direction = Direction.South },
                    new { Point = new BlockPosition(buildingLoc.X, buildingLoc.Z - 1), Direction = Direction.North },
                };
                var placement = candidates
                    .OrderBy(candidate => baseHeights.ContainsKey(candidate.Point) ? 1 : 0)
                    .ThenBy(candidate => {
                        double dx = candidate.Point.X - frontageRoadX;
                        double dz = candidate.Point.Z - frontageRoadZ;
                        return dx * dx + dz * dz;
                    })
                    .First();
                signLoc = placement.Point;
                direction = placement.Direction;

                Console.WriteLine("Building sign at {0}", signLoc);
                DrawSign(signLoc, direction, building.Name, building.HouseNumber);
            } else {
                Console.WriteLine("No road for finding sign loc: {0} {1}", building.HouseNumber, building.Street, building);
            }
            return doors;
        }

        private bool TryFindClosestRoadFrontage(IEnumerable<BlockPosition> buildingWalls,
                IEnumerable<OsmReader.Way> roads, out BlockPosition buildingLoc,
                out double roadX, out double roadZ) {
            buildingLoc = default(BlockPosition);
            roadX = 0;
            roadZ = 0;
            double closestDistance = double.MaxValue;
            bool found = false;

            foreach (BlockPosition wall in buildingWalls) {
                foreach (OsmReader.Way road in roads) {
                    for (int i = 1; i < road.Nodes.Length; i++) {
                        BlockPosition from = _conv.ToBlock(road.Nodes[i - 1].Lat, road.Nodes[i - 1].Long);
                        BlockPosition to = _conv.ToBlock(road.Nodes[i].Lat, road.Nodes[i].Long);
                        double segmentX = to.X - from.X;
                        double segmentZ = to.Z - from.Z;
                        double lengthSquared = segmentX * segmentX + segmentZ * segmentZ;
                        double amount = lengthSquared == 0
                            ? 0
                            : ((wall.X - from.X) * segmentX + (wall.Z - from.Z) * segmentZ) /
                                lengthSquared;
                        amount = Math.Max(0, Math.Min(1, amount));
                        double projectedX = from.X + amount * segmentX;
                        double projectedZ = from.Z + amount * segmentZ;
                        double dx = wall.X - projectedX;
                        double dz = wall.Z - projectedZ;
                        double distance = dx * dx + dz * dz;
                        if (distance < closestDistance) {
                            closestDistance = distance;
                            buildingLoc = wall;
                            roadX = projectedX;
                            roadZ = projectedZ;
                            found = true;
                        }
                    }
                }
            }
            return found;
        }

        enum DoorDirection {
            East = 0,
            South = 1,
            West = 2,
            North = 3
        }

        private void DrawDoor(DoorDirection facing, int doorX, int doorZ, int doorHeight) {
            BlockFacing blockFacing = ToBlockFacing(facing);
            BlockHinge hinge = BlockHinge.Right;
            _bm.SetBlock(
                doorX, doorHeight + 1, doorZ, AcquaticBlocks.OakDoor,
                facing: blockFacing,
                half: BlockHalf.Lower,
                hinge: hinge,
                open: false,
                powered: false);

            _bm.SetBlock(
                doorX, doorHeight + 2, doorZ, AcquaticBlocks.OakDoor,
                facing: blockFacing,
                half: BlockHalf.Upper,
                hinge: hinge,
                open: false,
                powered: false);
        }

        private void DrawSign(BlockPosition signLoc, Direction direction, params string[] text) {
            DrawSign(signLoc, direction, BlockType.SIGN_POST, null, text);
        }

        static string[] _sign_types = new[] {
            AcquaticBlocks.OakSign,
            AcquaticBlocks.JungleSign,
            AcquaticBlocks.MangroveSign,
            AcquaticBlocks.PaleOakSign,
            AcquaticBlocks.SpruceSign,
            AcquaticBlocks.DarkOakSign,
            AcquaticBlocks.CherrySign,
        };

        private void DrawSign(BlockPosition signLoc, Direction direction, int signType = BlockType.SIGN_POST, int? height = null, params string[] text) {
            if (!_conv.IsValidPoint(signLoc)) {
                return;
            }
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

            if (signType == BlockType.WALL_SIGN) {
                _bm.SetBlock(
                    signLoc.X, height.Value, signLoc.Z,
                    AcquaticBlocks.OakWallSign,
                    facing: ToBlockFacing(direction));
            } else {
                _bm.SetBlock(
                    signLoc.X, height.Value, signLoc.Z,
                    _sign_types[Math.Abs((signLoc.X + signLoc.Z) % _sign_types.Length)],
                    rotation: (int)direction);
            }
            _bm.SetTileEntity(signLoc.X, height.Value, signLoc.Z, ent);
        }

        private static BlockFacing ToBlockFacing(Direction direction) {
            switch (direction) {
                case Direction.East: return BlockFacing.East;
                case Direction.West: return BlockFacing.West;
                case Direction.South: return BlockFacing.South;
                case Direction.North: return BlockFacing.North;
                default: throw new ArgumentOutOfRangeException("direction");
            }
        }

        private HashSet<BlockPosition> DrawSignItems(OsmReader.Building building, int top, int left, int bottom, int right, Dictionary<BlockPosition, int> baseHeights, Dictionary<BlockPosition, int> buildingPoints) {
            HashSet<BlockPosition> doors = new HashSet<BlockPosition>();
            if (building.SignItems == null) {
                return doors;
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
                while (!baseHeights.ContainsKey(targetPoint) &&
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
                if (baseHeights.ContainsKey(buildingPoint)) {
                    DoorDirection doorDir = 0;
                    switch (dir) {
                        case Direction.East: doorDir = DoorDirection.West; break;
                        case Direction.South: doorDir = DoorDirection.North; break;
                        case Direction.West: doorDir = DoorDirection.East; break;
                        case Direction.North: doorDir = DoorDirection.South; break;
                    }
                    int baseHeight;
                    if (!baseHeights.TryGetValue(buildingPoint, out baseHeight)) {
                        continue;
                    }
                    DrawDoor(doorDir, targetPoint.X - adjX, targetPoint.Z - adjZ, baseHeight);
                    doors.Add(new BlockPosition(targetPoint.X - adjX, targetPoint.Z - adjZ));
                    DrawSign(targetPoint, dir, BlockType.WALL_SIGN, baseHeight + 3, signNode.Description);
                } else {
                    DrawSign(targetPoint, dir, signNode.Description);
                }
            }
            return doors;
        }

        private static double GetDistance(OsmReader.Node signPoint, OsmReader.Node pointOnBuilding) {
            return Math.Sqrt(
                (signPoint.Lat - pointOnBuilding.Lat) * (signPoint.Lat - pointOnBuilding.Lat) +
                (signPoint.Long - pointOnBuilding.Long) * (signPoint.Long - pointOnBuilding.Long)
            );
        }

    }
}
