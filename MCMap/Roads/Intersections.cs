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

    }
}
