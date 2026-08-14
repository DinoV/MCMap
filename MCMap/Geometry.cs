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

        private static int MapOrder(PositionConverter conv, OsmReader.Node node) {
            var position = conv.ToBlock(node.Lat, node.Long);
            return (position.Z >> 5) * 100 + position.X >> 5;
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
            int D = 2 * dx - dy;
            // fix up our start point so that we're centering on thickness...
            for (int i = 0; i < startFixUp; i++) {
                x0--;
                x1--;
                if (D >= 0) {
                    y0 += xi;
                    y1 += xi;
                    D -= dy * 2;
                }
                D += dx * 2;
            }
            D = 2 * dx - dy;
            int x = x0;

            for (int y = y0; y <= y1; y++) {
                yield return new LinePosition(new BlockPosition(x, y), index, y - y0);
                if (D >= 0) {
                    if (overlap) {
                        if (xi == -1) {
                            yield return new LinePosition(new BlockPosition(x - 1, y), index, y - y0);
                        } else {
                            yield return new LinePosition(new BlockPosition(x, y + 1), index, y - y0);
                        }
                    }
                    x = x + xi;
                    D -= 2 * dy;
                }
                D += 2 * dx;
            }

            // then draw the additional lines...
            D = 2 * dx - dy;
            if (width != 1) {
                for (int i = 1; i < width; i++) {
                    x0++;
                    x1++;
                    bool drawOverlap = false;
                    if (D >= 0) {
                        y0 -= xi;
                        y1 -= xi;
                        D -= 2 * dy;
                        drawOverlap = true;
                    }
                    D += 2 * dx;
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
            int D = 2 * dy - dx;
            // fix up our start point so that we're centering on thickness...
            for (int i = 0; i < startFixUp; i++) {
                y0--;
                y1--;
                if (D >= 0) {
                    x0 += yi;
                    x1 += yi;
                    D -= dx * 2;
                }
                D += dy * 2;
            }

            // then draw the line...
            int y = y0;
            D = 2 * dy - dx;
            for (int x = x0; x <= x1; x++) {
                yield return new LinePosition(new BlockPosition(x, y), index, x - x0);
                if (D >= 0) {
                    if (overlap) {
                        if (yi == -1) {
                            yield return new LinePosition(new BlockPosition(x, y - 1), index, x - x0);
                        } else {
                            yield return new LinePosition(new BlockPosition(x + 1, y), index, x - x0);
                        }
                    }
                    y = y + yi;
                    D -= 2 * dx;
                }
                D += 2 * dy;
            }

            // then draw the additional lines...
            D = 2 * dy - dx;
            if (width != 1) {
                for (int i = 1; i < width; i++) {
                    y0++;
                    y1++;
                    bool drawOverlap = false;
                    if (D >= 0) {
                        x0 -= yi;
                        x1 -= yi;
                        D -= 2 * dx;
                        drawOverlap = true;
                    }
                    D += 2 * dy;
                    foreach (var value in PlotLineLow(x0, y0, x1, y1, 1, drawOverlap, i)) {
                        yield return value;
                    }
                }
            }
        }
    }
}
