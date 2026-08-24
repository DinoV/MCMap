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
        private const int TrafficLightHeight = 8;

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
                    OsmReader.SignInfo signType;
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
                    bool drawLights = ShouldDrawOverheadLights(intersection, signType?.Type ?? OsmReader.SignType.None, segments);
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
                            DrawRoadSign(signType?.Type ?? OsmReader.SignType.None, names, roadPoints, pos, angle.Value, segment.Way);
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

            var target = GetSignNonRoadPoint(roadPoints, intersection, angle, direction);
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
                _bm.SetBlock(
                    target.X, height, target.Z, AcquaticBlocks.OakSign,
                    rotation: (int)direction);
                _bm.SetTileEntity(target.X, height, target.Z, ent);
            }
        }

        private void DrawTrafficSign(OsmReader.SignType signType, List<string> name, Direction direction, BlockPosition target) {
            var height = _bm.GetHeight(target.X, target.Z);
            const int StopSignHeight = 4;
            WriteLine("Sign {0} {1}", target, signType);
            for (int i = 0; i < StopSignHeight; i++) {
                _bm.SetBlock(target.X, height + i, target.Z, AcquaticBlocks.CobblestoneWall, north: false, east: false, south: false, west: false, up: false);
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
                    _bm.SetBlock(
                        signX, height + 2, signZ,
                        AcquaticBlocks.OakWallSign,
                        facing: WallFacingFromLegacyData(data));
                    _bm.SetTileEntity(signX, height + 2, signZ, ent);
                } else {
                    var signHeight = _bm.GetHeight(signX, signZ);
                    _bm.SetBlock(
                        signX, signHeight, signZ,
                        AcquaticBlocks.OakSign,
                        rotation: (int)direction);
                    _bm.SetTileEntity(signX, signHeight, signZ, ent);
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

            _bm.SetBlock(
                target.X, height + StopSignHeight, target.Z,
                AcquaticBlocks.WhiteBanner,
                rotation: (int)direction);
            _bm.SetTileEntity(
                target.X, height + StopSignHeight, target.Z, bannerEnt);
        }

        private void DrawTrafficLight(List<string> name, BlockPosition target, Direction direction, int lanes, bool sidewalk) {
            var height = _bm.GetHeight(target.X, target.Z);
            WriteLine("Sign {0} TrafficLight", target);
            // draw the vertical traffic light pieces...
            // We start with a piece of polished andisite, then an anvil, and have an
            // overlapping armor stand to look like cross walk buttons.
            // Disabled because it currently looks weird when ported to Bedrock addition
            var entities = _bm.GetChunk(target.X, height, target.Z).Entities;
            var armor = new TypedEntity("minecraft:armor_stand");
            armor.Position = new Vector3() { X = target.X + .5, Y = height, Z = target.Z + .5 };
            armor.IsOnGround = true;
            _bm.SetBlock(target.X, height, target.Z, AcquaticBlocks.PolishedAndesite);
            _bm.SetBlock(target.X, height + 1, target.Z, AcquaticBlocks.Anvil, facing: BlockFacing.West);
            if (direction == Direction.North || direction == Direction.South) {
                armor.Rotation = new Orientation() { Yaw = Math.PI / 2 };
            }
            entities.Add(armor);

            for (int i = 2; i < TrafficLightHeight; i++) {
                _bm.SetBlock(target.X, height + i, target.Z, AcquaticBlocks.CobblestoneWall, north: false, east: false, south: false, west: false, up: false);
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
                _bm.SetBlock(xLoc, height + TrafficLightHeight - 1, zLoc, AcquaticBlocks.CobblestoneWall, north: false, east: false, south: false, west: false, up: false);
                xLoc += xDelta;
                zLoc += zDelta;
            }

            for (int lane = 0; lane < lanes; lane++) {
                // Draw the traffic light...
                for (int i = 0; i < 2; i++) {
                    _bm.SetBlock(xLoc, height + TrafficLightHeight - 1 - i, zLoc, AcquaticBlocks.BlackWool);
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

                _bm.SetBlock(
                    lightX, height + TrafficLightHeight - 1, lightZ,
                    AcquaticBlocks.WhiteWallBanner,
                    facing: WallFacingFromLegacyData(lightDir));
                _bm.SetTileEntity(
                    lightX, height + TrafficLightHeight - 1, lightZ,
                    bannerEnt);

                xLoc += xDelta;
                zLoc += zDelta;

                // draw three more horizontal pieces
                int horizontalLength = 3;
                if (lane != lanes - 1 && lane != 0) {
                    horizontalLength = 2;
                }
                for (int i = 0; i < horizontalLength; i++) {
                    _bm.SetBlock(xLoc, height + TrafficLightHeight - 1, zLoc, AcquaticBlocks.CobblestoneWall, north: false, east: false, south: false, west: false, up: false);
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
                        _bm.SetBlock(
                            signX, height + TrafficLightHeight - 1, signZ,
                            AcquaticBlocks.OakWallSign,
                            facing: WallFacingFromLegacyData(signDir));
                        _bm.SetTileEntity(
                            signX, height + TrafficLightHeight - 1, signZ,
                            ent);
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

    }
}
