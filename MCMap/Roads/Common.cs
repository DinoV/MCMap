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

        private Dictionary<BlockPosition, RoadPoint> DrawRoads() {
            int cur = 0;
            // We group the roads by their name here, and then order them by a position in the road.  The grouping
            // by name ensures that we process all of the road segments, which may be split out as multiple ways,
            // at once.  Once a road is entirely processed we'll process it's intersections and draw them on.
            // We order by a position on the map to handle very large maps - this allows us to be reading/writing
            // in approximately the same area and to avoid lots of additional reads/writes.
            IEnumerable<IGrouping<string, OsmReader.Way>> roads =
                _reader.Ways.Values
                    .GroupBy(x => x.Name ?? "")
                    .OrderBy(x => MapOrder(_conv, x.First().Nodes.First()));
            var roadPoints = new Dictionary<BlockPosition, RoadPoint>();
            //var intersections = new Dictionary<RoadIntersection, List<BlockPosition>>();
            //var roads = new[] { reader.Ways[6358491], reader.Ways[6358495], reader.Ways[158718178], reader.Ways[241283900], reader.Ways[6433374] };
            foreach (var roadGroup in roads) {
                foreach (var way in roadGroup) {
                    if ((++cur % 50) == 0) {
                        SaveBlocks();
                    }

                    if (way.RoadType != OsmReader.RoadType.Service &&
                        way.RoadType != OsmReader.RoadType.FootWay &&
                        way.RoadType != OsmReader.RoadType.Motorway &&
                        way.RoadType != OsmReader.RoadType.MotorwayLink) {
                        if ((way.Name == null && !way.Roundabout) ||
                            (way.RoadType != OsmReader.RoadType.Residential &&
                            way.RoadType != OsmReader.RoadType.FootWay &&
                            way.RoadType != OsmReader.RoadType.Primary &&
                            way.RoadType != OsmReader.RoadType.Secondary &&
                            way.RoadType != OsmReader.RoadType.Path &&
                            way.RoadType != OsmReader.RoadType.Trunk) ||
                            (way.Layer != null && !way.Bridge)) {
                            continue;
                        }
                    }

                    WriteLine("{0} {1} {2} ({3}/{4})", way.Id, way.Name, way.RoadType, cur, _reader.Ways.Count);
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
                                        _bm.SetBlock(point.Block.X, height - 1, point.Block.Z, AcquaticBlocks.QuartzBlock);
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
            public string BlockName;
            public Substrate.Nbt.TagNodeCompound Properties;

            public RoadPoint(int height, bool isSidewalk, string blockName,
                    Substrate.Nbt.TagNodeCompound properties) {
                Segments = new List<RoadSegment>();
                Height = height;
                IsSidewalk = isSidewalk;
                BlockName = blockName;
                Properties = properties;
            }
        }

        partial class RoadRenderer {
            public readonly string BlockName;
            public readonly Substrate.Nbt.TagNodeCompound Properties;
            public readonly int Width;
            private readonly string _leftEdgeName, _rightEdgeName;
            private readonly Substrate.Nbt.TagNodeCompound _edgeProperties;
            private readonly Mapper _mapper;
            protected readonly PositionConverter _conv;
            protected readonly AnvilBlockManager _bm;
            public readonly OsmReader.Way Way;
            private bool IsSidewalk;

            public RoadRenderer(Mapper mapper, PositionConverter conv, AnvilBlockManager bm, OsmReader.Way way, int width,
                    string blockName, Substrate.Nbt.TagNodeCompound properties, bool isSidealk,
                    string leftEdgeName = null, string rightEdgeName = null,
                    Substrate.Nbt.TagNodeCompound edgeProperties = null) {
                _mapper = mapper;
                _conv = conv;
                _bm = bm;
                Way = way;
                Width = width;
                BlockName = blockName;
                Properties = properties;
                _leftEdgeName = leftEdgeName;
                _rightEdgeName = rightEdgeName;
                _edgeProperties = edgeProperties;
                IsSidewalk = isSidealk;
            }

            private void AddRoadPoint(Dictionary<BlockPosition, RoadPoint> roadPoints, BlockPosition position,
                    RoadSegment segment, int height, bool isSidewalk = false,
                    string blockName = null, Substrate.Nbt.TagNodeCompound properties = null) {
                bool pointIsSidewalk = IsSidewalk || isSidewalk;
                if (blockName == null) {
                    blockName = BlockName;
                    properties = Properties;
                }
                RoadPoint roadPoint;
                if (!roadPoints.TryGetValue(position, out roadPoint)) {
                    roadPoints[position] = roadPoint = new RoadPoint(
                        height, pointIsSidewalk, blockName, properties);
                } else {
                    if (Way.Bridge) {
                        roadPoint.Height = height;
                    }
                    // Once a point contains a road, a sidewalk must never take ownership of it.
                    if (roadPoint.IsSidewalk && !pointIsSidewalk) {
                        roadPoint.BlockName = blockName;
                        roadPoint.Properties = properties;
                    }
                    roadPoint.IsSidewalk = roadPoint.IsSidewalk && pointIsSidewalk;
                    roadPoints[position] = roadPoint;
                }
                roadPoint.Segments.Add(segment);
            }

            public void Render(Dictionary<BlockPosition, RoadPoint> roadPoints) {
                var path = Way.Nodes.Select(node =>
                    _conv.ToBlock(node.Lat, node.Long)).ToList();
                int bridgeStartHeight, bridgeEndHeight;
                double bridgeLength = PrepareBridgePath(
                    path, out bridgeStartHeight, out bridgeEndHeight);
                double completedBridgeLength = 0;
                int initialWidth = Width;
                if (_leftEdgeName != null) {
                    initialWidth += SidewalkWidth;
                }
                if (_rightEdgeName != null) {
                    initialWidth += SidewalkWidth;
                }
                for (int i = 1; i < path.Count; i++) {
                    var from = path[i - 1];
                    var to = path[i];
                    int wayEndIndex = Way.Bridge
                        ? Math.Max(1, Math.Min(Way.Nodes.Length - 1, i - 1))
                        : i;
                    var curSegment = new RoadSegment(
                        Way, Way.Nodes[wayEndIndex - 1], Way.Nodes[wayEndIndex]);
                    double segmentLength = Distance(from, to);
                    var previousRoadPoints = new Dictionary<int, RoadStepPoint>();

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
                        if (_mapper._pierDeckHeights.ContainsKey(point.Block)) {
                            return;
                        }
                        if (_conv.IsValidPoint(point.Block) && point.Block.X > 1 && point.Block.Z > 1) {
                            bool isSidewalkPoint = IsSidewalk ||
                                (_leftEdgeName != null && point.Column < SidewalkWidth) ||
                                (_rightEdgeName != null && point.Column >= targetWidth - SidewalkWidth);
                            RoadPoint existingRoad;
                            bool isIntersection = roadPoints.TryGetValue(point.Block, out existingRoad);
                            if (isIntersection && !Way.Bridge) {
                                bool skip = false;
                                foreach (var segment in existingRoad.Segments) {
                                    if (segment.Way == Way ||
                                        (segment.Way.Name != null && segment.Way.Name == Way.Name)) {
                                        AddRoadPoint(roadPoints, point.Block, curSegment, existingRoad.Height,
                                            isSidewalkPoint);
                                        skip = true;
                                        break;
                                    } else if (existingRoad.IsSidewalk && !IsSidewalk) {
                                        // Two road-edge sidewalks can overlap across the entire center of
                                        // an intersection. Promote that shared area to this road's lane
                                        // material instead of leaving the original terrain untouched.
                                        AddRoadPoint(roadPoints, point.Block, curSegment,
                                            existingRoad.Height, false, BlockName, Properties);
                                        skip = true;
                                        break;
                                    } else if (!existingRoad.IsSidewalk && isSidewalkPoint) {
                                        // Roads have priority, regardless of whether the sidewalk was
                                        // rendered as a footway or as the edge of another road.
                                        AddRoadPoint(roadPoints, point.Block, curSegment, existingRoad.Height, true);
                                        skip = true;
                                        break;
                                    } else if (!existingRoad.IsSidewalk && GetRoadWidth(segment.Way) > Width) {
                                        AddRoadPoint(roadPoints, point.Block, curSegment, existingRoad.Height);
                                        skip = true;
                                        break;
                                    }
                                }

                                if (skip) {
                                    RoadPoint winningPoint = roadPoints[point.Block];
                                    // Repaint the block state that owns the point. This fills lowered-road
                                    // intersection gaps without allowing a sidewalk or narrower road to
                                    // replace the winning roadway.
                                    _bm.SetBlock(point.Block.X, winningPoint.Height - 1,
                                        point.Block.Z, winningPoint.BlockName, winningPoint.Properties);
                                    ClearPlantLife(_bm, point.Block, winningPoint.Height);
                                    continue;
                                }
                            }

                            int height;
                            if (Way.Bridge) {
                                height = CalculateBridgeHeight(point.Block, from, to,
                                    segmentLength, completedBridgeLength, bridgeLength,
                                    bridgeStartHeight, bridgeEndHeight);
                            } else if (isIntersection) {
                                // Roads and sidewalks share a base height.  A road slab replaces a
                                // sidewalk block at that height when the two overlap.
                                height = existingRoad.Height;
                            } else {
                                height = _bm.GetHeight(point.Block.X, point.Block.Z);
                            }

                            if (_leftEdgeName != null && point.Column < SidewalkWidth) {
                                if (!isIntersection || Way.Bridge) {
                                    DrawSidewalkPoint(roadPoints, point, curSegment, height,
                                        _leftEdgeName);
                                }
                            } else if (_rightEdgeName != null && point.Column >= targetWidth - SidewalkWidth) {
                                if (!isIntersection || Way.Bridge) {
                                    DrawSidewalkPoint(roadPoints, point, curSegment, height,
                                        _rightEdgeName);
                                }
                            } else if (Way.Bridge &&
                                    (point.Column == 0 || point.Column == targetWidth - 1)) {
                                DrawBridgeEdgePoint(roadPoints, point, curSegment, height);
                            } else {
                                string roadBlockName = BlockName;
                                Substrate.Nbt.TagNodeCompound roadProperties = Properties;
                                MakeRoadStepGradual(roadPoints, previousRoadPoints, point,
                                    height, ref roadBlockName, ref roadProperties);
                                if (DrawRoadPoint(point, height, roadBlockName, roadProperties)) {
                                    AddRoadPoint(roadPoints, point.Block, curSegment, height,
                                        false, roadBlockName, roadProperties);
                                    ClearPlantLife(_bm, point.Block, height);
                                }
                            }

                        }
                    }

                    completedBridgeLength += segmentLength;
                    //WriteLine("From {0},{1} to {2},{3}", from.X, from.Z, to.X, to.Z);
                }

                if (Way.Roundabout) {
                    FillSmallRoundabout(path, roadPoints);
                }
            }

            private void FillSmallRoundabout(List<BlockPosition> path,
                    Dictionary<BlockPosition, RoadPoint> roadPoints) {
                const int MaxPavedRoundaboutDiameter = 32;
                int minX = path.Min(point => point.X);
                int maxX = path.Max(point => point.X);
                int minZ = path.Min(point => point.Z);
                int maxZ = path.Max(point => point.Z);
                if (maxX - minX > MaxPavedRoundaboutDiameter ||
                        maxZ - minZ > MaxPavedRoundaboutDiameter) {
                    return;
                }

                var segment = new RoadSegment(Way, Way.Nodes[0], Way.Nodes[1]);
                for (int z = minZ; z <= maxZ; z++) {
                    for (int x = minX; x <= maxX; x++) {
                        var position = new BlockPosition(x, z);
                        if (!_conv.IsValidPoint(position) ||
                                !IsInsidePolygon(x + 0.5, z + 0.5, path)) {
                            continue;
                        }

                        RoadPoint existingRoad;
                        bool hasExistingRoad = roadPoints.TryGetValue(position, out existingRoad);
                        int height = hasExistingRoad
                            ? existingRoad.Height
                            : _bm.GetHeight(x, z);
                        string blockName = hasExistingRoad && !existingRoad.IsSidewalk
                            ? existingRoad.BlockName
                            : BlockName;
                        Substrate.Nbt.TagNodeCompound properties =
                            hasExistingRoad && !existingRoad.IsSidewalk
                                ? existingRoad.Properties
                                : Properties;
                        _bm.SetBlock(x, height - 1, z, blockName, properties);
                        AddRoadPoint(roadPoints, position, segment, height,
                            false, blockName, properties);
                        ClearPlantLife(_bm, position, height);
                    }
                }
            }

            private static bool IsInsidePolygon(double x, double z,
                    IList<BlockPosition> polygon) {
                bool inside = false;
                for (int current = 0, previous = polygon.Count - 1;
                        current < polygon.Count; previous = current++) {
                    double currentZ = polygon[current].Z;
                    double previousZ = polygon[previous].Z;
                    if ((currentZ > z) == (previousZ > z)) continue;
                    double edgeX = (polygon[previous].X - polygon[current].X) *
                        (z - currentZ) / (previousZ - currentZ) + polygon[current].X;
                    if (x < edgeX) inside = !inside;
                }
                return inside;
            }

            private struct RoadStepPoint {
                public readonly BlockPosition Position;
                public readonly int Height;

                public RoadStepPoint(BlockPosition position, int height) {
                    Position = position;
                    Height = height;
                }
            }

            private void MakeRoadStepGradual(
                    Dictionary<BlockPosition, RoadPoint> roadPoints,
                    Dictionary<int, RoadStepPoint> previousRoadPoints,
                    LinePosition point, int height, ref string blockName,
                    ref Substrate.Nbt.TagNodeCompound properties) {
                RoadStepPoint previous;
                if (!Way.Bridge &&
                        previousRoadPoints.TryGetValue(point.Column, out previous) &&
                        Math.Abs(previous.Height - height) == 1) {
                    string fullBlockName = GetFullBlockName(BlockName);
                    if (fullBlockName == BlockName) {
                        // Full-block roads ease onto the higher terrain with a bottom slab.
                        string slabBlockName = GetSlabBlockName(BlockName);
                        var slabProperties = CreateBlockProperties(BlockProperties.Type, "bottom");
                        if (height > previous.Height) {
                            blockName = slabBlockName;
                            properties = slabProperties;
                        } else {
                            _bm.SetBlock(previous.Position.X, previous.Height - 1,
                                previous.Position.Z, slabBlockName, slabProperties);
                            RoadPoint previousRoadPoint;
                            if (roadPoints.TryGetValue(previous.Position, out previousRoadPoint)) {
                                previousRoadPoint.BlockName = slabBlockName;
                                previousRoadPoint.Properties = slabProperties;
                                roadPoints[previous.Position] = previousRoadPoint;
                            }
                        }
                    } else {
                        // Slab roads use a top slab on the lower terrain, halfway between
                        // the two normal bottom-slab surface levels.
                        var topProperties = CreateBlockProperties(BlockProperties.Type, "top");
                        if (height < previous.Height) {
                            properties = topProperties;
                        } else {
                            _bm.SetBlock(previous.Position.X, previous.Height - 1,
                                previous.Position.Z, BlockName, topProperties);
                            RoadPoint previousRoadPoint;
                            if (roadPoints.TryGetValue(previous.Position, out previousRoadPoint)) {
                                previousRoadPoint.Properties = topProperties;
                                roadPoints[previous.Position] = previousRoadPoint;
                            }
                        }
                    }
                }
                previousRoadPoints[point.Column] = new RoadStepPoint(point.Block, height);
            }

            public bool DrawRoadPoint(LinePosition point, int height, string blockName,
                    Substrate.Nbt.TagNodeCompound properties) {
                _bm.SetBlock(point.Block.X, height - 1, point.Block.Z, blockName, properties);
                EnsureBridgeSupport(point.Block, height, blockName);
                return true;
            }
        }

        private static void ClearPlantLife(AnvilBlockManager bm, BlockPosition position, int height) {
            for (int i = 0; i < 3; i++) {
                var block = bm.GetID(position.X, height + i, position.Z);
                if (block != BlockType.AIR) {
                    bm.SetBlock(position.X, height + i, position.Z, AcquaticBlocks.Air);
                }
            }
        }

        private static string GetFullBlockName(string blockName) {
            if (blockName == AcquaticBlocks.CobblestoneSlab) return AcquaticBlocks.Cobblestone;
            if (blockName == AcquaticBlocks.DeepslateTileSlab) return AcquaticBlocks.DeepslateTiles;
            if (blockName == AcquaticBlocks.PolishedTuffSlab) return AcquaticBlocks.PolishedTuff;
            if (blockName == AcquaticBlocks.MudBrickSlab) return AcquaticBlocks.MudBricks;
            if (blockName == AcquaticBlocks.TuffSlab) return AcquaticBlocks.Tuff;
            if (blockName == AcquaticBlocks.PolishedAndesiteSlab) return AcquaticBlocks.PolishedAndesite;
            if (blockName == AcquaticBlocks.StoneSlab) return AcquaticBlocks.Stone;
            if (blockName == AcquaticBlocks.BrickSlab) return AcquaticBlocks.Bricks;
            if (blockName == AcquaticBlocks.SandstoneSlab) return AcquaticBlocks.Sandstone;
            if (blockName == AcquaticBlocks.OakSlab) return AcquaticBlocks.OakPlanks;
            if (blockName == AcquaticBlocks.MossyCobblestoneSlab) return AcquaticBlocks.MossyCobblestone;
            if (blockName == AcquaticBlocks.WarpedSlab) return AcquaticBlocks.WarpedPlanks;
            if (blockName == AcquaticBlocks.SpruceSlab) return AcquaticBlocks.SprucePlanks;
            if (blockName == AcquaticBlocks.SmoothStoneSlab) return AcquaticBlocks.SmoothStone;
            return blockName;
        }

        private static string GetSlabBlockName(string blockName) {
            if (blockName == AcquaticBlocks.Cobblestone) return AcquaticBlocks.CobblestoneSlab;
            if (blockName == AcquaticBlocks.DeepslateTiles) return AcquaticBlocks.DeepslateTileSlab;
            if (blockName == AcquaticBlocks.PolishedTuff) return AcquaticBlocks.PolishedTuffSlab;
            if (blockName == AcquaticBlocks.MudBricks) return AcquaticBlocks.MudBrickSlab;
            if (blockName == AcquaticBlocks.Tuff) return AcquaticBlocks.TuffSlab;
            if (blockName == AcquaticBlocks.PolishedAndesite) return AcquaticBlocks.PolishedAndesiteSlab;
            if (blockName == AcquaticBlocks.Stone) return AcquaticBlocks.StoneSlab;
            if (blockName == AcquaticBlocks.Bricks) return AcquaticBlocks.BrickSlab;
            if (blockName == AcquaticBlocks.Sandstone) return AcquaticBlocks.SandstoneSlab;
            if (blockName == AcquaticBlocks.OakPlanks) return AcquaticBlocks.OakSlab;
            if (blockName == AcquaticBlocks.MossyCobblestone) return AcquaticBlocks.MossyCobblestoneSlab;
            if (blockName == AcquaticBlocks.WarpedPlanks) return AcquaticBlocks.WarpedSlab;
            if (blockName == AcquaticBlocks.SprucePlanks) return AcquaticBlocks.SpruceSlab;
            if (blockName == AcquaticBlocks.SmoothStone) return AcquaticBlocks.SmoothStoneSlab;
            return blockName;
        }

        private static string GetRoadBlock(OsmReader.Way way) {
            string blockName = AcquaticBlocks.SandstoneSlab;
            switch (way.Surface) {
                case OsmReader.Surface.Cobblestone: blockName = AcquaticBlocks.CobblestoneSlab; break;
                case OsmReader.Surface.Asphalt: blockName = AcquaticBlocks.DeepslateTileSlab; break;
                case OsmReader.Surface.Clay: blockName = AcquaticBlocks.PolishedTuffSlab; break;
                case OsmReader.Surface.Dirt: blockName = AcquaticBlocks.MudBrickSlab; break;
                case OsmReader.Surface.Gravel: blockName = AcquaticBlocks.TuffSlab; break;
                case OsmReader.Surface.Metal: blockName = AcquaticBlocks.PolishedAndesiteSlab; break;
                case OsmReader.Surface.Stone: blockName = AcquaticBlocks.StoneSlab; break;
                case OsmReader.Surface.Brick: blockName = AcquaticBlocks.BrickSlab; break;
                case OsmReader.Surface.Sand: blockName = AcquaticBlocks.SandstoneSlab; break;
                case OsmReader.Surface.Wood: blockName = AcquaticBlocks.OakSlab; break;
                case OsmReader.Surface.GravelGrass: blockName = AcquaticBlocks.MossyCobblestoneSlab; break;
                case OsmReader.Surface.Compacted: blockName = AcquaticBlocks.WarpedSlab; break;
                case OsmReader.Surface.FineGravel: blockName = AcquaticBlocks.TuffSlab; break;
                case OsmReader.Surface.Ground: blockName = AcquaticBlocks.SpruceSlab; break;
                case OsmReader.Surface.RailroadTies: blockName = AcquaticBlocks.OakSlab; break;
                case OsmReader.Surface.Concrete: blockName = AcquaticBlocks.SmoothStoneSlab; break;
            }
            return blockName;
        }

        private static string GetSidewalkBlock(OsmReader.Way way) {
            string blockName = AcquaticBlocks.Sandstone;
            switch (way.Surface) {
                case OsmReader.Surface.Cobblestone: blockName = AcquaticBlocks.Cobblestone; break;
                case OsmReader.Surface.Asphalt: blockName = AcquaticBlocks.DeepslateTiles; break;
                case OsmReader.Surface.Clay: blockName = AcquaticBlocks.PolishedTuff; break;
                case OsmReader.Surface.Dirt: blockName = AcquaticBlocks.MudBricks; break;
                case OsmReader.Surface.Gravel: blockName = AcquaticBlocks.Tuff; break;
                case OsmReader.Surface.Metal: blockName = AcquaticBlocks.PolishedAndesite; break;
                case OsmReader.Surface.Stone: blockName = AcquaticBlocks.Stone; break;
                case OsmReader.Surface.Brick: blockName = AcquaticBlocks.Bricks; break;
                case OsmReader.Surface.Sand: blockName = AcquaticBlocks.Sandstone; break;
                case OsmReader.Surface.Wood: blockName = AcquaticBlocks.OakPlanks; break;
                case OsmReader.Surface.GravelGrass: blockName = AcquaticBlocks.MossyCobblestone; break;
                case OsmReader.Surface.Compacted: blockName = AcquaticBlocks.WarpedPlanks; break;
                case OsmReader.Surface.FineGravel: blockName = AcquaticBlocks.Tuff; break;
                case OsmReader.Surface.Ground: blockName = AcquaticBlocks.SprucePlanks; break;
                case OsmReader.Surface.RailroadTies: blockName = AcquaticBlocks.OakPlanks; break;
                case OsmReader.Surface.Concrete: blockName = AcquaticBlocks.SmoothStone; break;
            }
            return blockName;
        }

        private RoadRenderer GetRoadStyle(OsmReader.Way way) {
            int width = GetRoadWidth(way);
            string blockName;
            Substrate.Nbt.TagNodeCompound properties = null;
            bool isSidewalk = way.RoadType == OsmReader.RoadType.FootWay;
            if (isSidewalk) {
                if (way.Crossing != OsmReader.CrossingType.None) {
                    if (way.Crossing == OsmReader.CrossingType.Zebra) {
                        return null;
                    }
                    Console.WriteLine("Crossing {0}", way.Crossing);
                }

                width = 3;
                blockName = GetSidewalkBlock(way);
            } else if (way.RoadType == OsmReader.RoadType.Service) {
                // Service roads stay level with the surrounding terrain.  Use the
                // full-block equivalent of an explicit surface, or smooth stone
                // for the default surface used by service roads.
                blockName = way.Surface == OsmReader.Surface.None
                    ? AcquaticBlocks.SmoothStone
                    : GetSidewalkBlock(way);
                if (way.Surface == OsmReader.Surface.None) {
                    width = 4;
                }
            } else {
                blockName = GetRoadBlock(way);
                properties = CreateBlockProperties(BlockProperties.Type, "bottom");
                if (way.Surface == OsmReader.Surface.None) {
                    switch (way.RoadType) {
                        case OsmReader.RoadType.Path:
                            width = 3;
                            blockName = AcquaticBlocks.TuffSlab;
                            break;
                        case OsmReader.RoadType.Secondary:
                            width = (way.Lanes ?? 3) * 3;
                            blockName = AcquaticBlocks.SmoothStoneSlab;
                            break;
                        case OsmReader.RoadType.Residential:
                        case OsmReader.RoadType.Primary:
                        case OsmReader.RoadType.Trunk:
                            width = (way.Lanes ?? 2) * 4;
                            blockName = AcquaticBlocks.SmoothStoneSlab;
                            break;
                        case OsmReader.RoadType.MotorwayLink:
                        case OsmReader.RoadType.Motorway:
                            width = (way.Lanes ?? 2) * 7;
                            blockName = AcquaticBlocks.SmoothStoneSlab;
                            break;
                        default:
                            blockName = AcquaticBlocks.SmoothStoneSlab;
                            break;
                    }
                }
            }

            string leftEdge = null, rightEdge = null;
            switch (way.Sidewalk) {
                case OsmReader.Sidewalk.Left:
                    leftEdge = AcquaticBlocks.StoneBricks;
                    break;
                case OsmReader.Sidewalk.Right:
                    rightEdge = AcquaticBlocks.StoneBricks;
                    break;
                case OsmReader.Sidewalk.Both:
                    leftEdge = rightEdge = AcquaticBlocks.StoneBricks;
                    break;
            }
            return new RoadRenderer(this, _conv, _bm, way, width, blockName, properties, isSidewalk,
                leftEdge, rightEdge);
        }

        private static Substrate.Nbt.TagNodeCompound CreateBlockProperties(string name, string value) {
            var properties = new Substrate.Nbt.TagNodeCompound();
            properties[name] = new Substrate.Nbt.TagNodeString(value);
            return properties;
        }
    }
}
