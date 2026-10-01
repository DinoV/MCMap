using Substrate;
using System;
using System.Collections.Generic;
using System.Linq;

namespace MinecraftMapper {
    partial class Mapper {
        private readonly Dictionary<BlockPosition, int> _pierDeckHeights =
            new Dictionary<BlockPosition, int>();

        private void DrawPiers() {
            foreach (var pier in _reader.Piers) {
                var polygon = pier.Nodes
                    .Select(node => _conv.ToBlock(node.Lat, node.Long))
                    .Where(point => _conv.IsValidPoint(point))
                    .ToList();
                if (polygon.Count < 3) {
                    continue;
                }

                int left = polygon.Min(point => point.X);
                int right = polygon.Max(point => point.X);
                int top = polygon.Min(point => point.Z);
                int bottom = polygon.Max(point => point.Z);
                var deckPoints = GetPierDeckPoints(polygon, left, top, right, bottom);
                PierLandConnection landConnection = null;
                int deckHeight;
                if (pier.Floating) {
                    // The sampled height is the water block itself. Put the
                    // deck above it rather than replacing the water surface.
                    deckHeight = FindPierWaterHeight(
                        polygon, left, top, right, bottom) + 1;
                } else {
                    landConnection = FindPierLandConnection(
                        polygon, deckPoints, left, top, right, bottom);
                    deckHeight = landConnection.Height;
                    ExtendPierToLand(deckPoints, landConnection);
                }

                int rendered = 0;
                int supports = 0;
                foreach (var point in deckPoints) {
                    if (!pier.Floating &&
                            PositiveModulo(point.X - left, 6) == 0 &&
                            PositiveModulo(point.Z - top, 6) == 0) {
                        int groundHeight = GetTerrainHeight(point);
                        for (int y = groundHeight + 1; y < deckHeight; y++) {
                            _bm.SetBlock(point.X, y, point.Z,
                                AcquaticBlocks.OakLog, axis: BlockAxis.Y);
                        }
                        supports++;
                    }

                    ClearNonSolidBlocks(point, deckHeight + 1, deckHeight + 3);
                    _bm.SetBlock(point.X, deckHeight, point.Z, AcquaticBlocks.OakPlanks);
                    _pierDeckHeights[point] = deckHeight;
                    rendered++;
                }

                int fences = pier.Floating
                    ? 0
                    : DrawPierEdgeFences(deckPoints, deckHeight);

                WriteLine($"{pier.WayId:10} Pier {(pier.Floating ? "floating" : "fixed")}: {rendered} blocks at height {deckHeight}, " +
                    "{supports} supports, {fences} edge fences.");
            }
        }

        private int DrawPierEdgeFences(HashSet<BlockPosition> deckPoints,
                int deckHeight) {
            var fencePoints = new HashSet<BlockPosition>();
            foreach (var point in deckPoints) {
                bool exposedEdge = false;
                bool connectsToLand = false;
                foreach (var direction in _queueDirs) {
                    var neighbor = new BlockPosition(
                        point.X + direction.X, point.Z + direction.Z);
                    if (deckPoints.Contains(neighbor)) {
                        continue;
                    }

                    exposedEdge = true;
                    if (IsLandAtPierHeight(neighbor, deckHeight)) {
                        connectsToLand = true;
                        break;
                    }
                }

                if (exposedEdge && !connectsToLand) {
                    fencePoints.Add(point);
                }
            }

            foreach (var point in fencePoints) {
                _bm.SetBlock(point.X, deckHeight + 1, point.Z,
                    AcquaticBlocks.OakFence,
                    north: fencePoints.Contains(new BlockPosition(point.X, point.Z - 1)),
                    east: fencePoints.Contains(new BlockPosition(point.X + 1, point.Z)),
                    south: fencePoints.Contains(new BlockPosition(point.X, point.Z + 1)),
                    west: fencePoints.Contains(new BlockPosition(point.X - 1, point.Z)));
            }
            return fencePoints.Count;
        }

        private bool IsLandAtPierHeight(BlockPosition point, int deckHeight) {
            if (!_conv.IsValidPoint(point)) {
                return false;
            }

            int surfaceHeight = GetHeight(point);
            if (surfaceHeight < deckHeight - 1) {
                return false;
            }

            int id = _bm.GetID(point.X, surfaceHeight, point.Z);
            return id != BlockType.WATER && id != BlockType.STATIONARY_WATER &&
                id != BlockType.AIR;
        }

        private HashSet<BlockPosition> GetPierDeckPoints(
                IList<BlockPosition> polygon, int left, int top, int right, int bottom) {
            var points = new HashSet<BlockPosition>();
            for (int z = top; z <= bottom; z++) {
                for (int x = left; x <= right; x++) {
                    var point = new BlockPosition(x, z);
                    if (_conv.IsValidPoint(point) &&
                            IsInsidePolygon(x + 0.5, z + 0.5, polygon)) {
                        points.Add(point);
                    }
                }
            }

            for (int i = 1; i < polygon.Count; i++) {
                foreach (var linePoint in PlotLine(
                        polygon[i - 1].X, polygon[i - 1].Z,
                        polygon[i].X, polygon[i].Z)) {
                    if (_conv.IsValidPoint(linePoint.Block)) {
                        points.Add(linePoint.Block);
                    }
                }
            }
            return points;
        }

        private void ExtendPierToLand(HashSet<BlockPosition> deckPoints,
                PierLandConnection connection) {
            int deltaX = connection.LandPoint.X - connection.PierPoint.X;
            int deltaZ = connection.LandPoint.Z - connection.PierPoint.Z;
            double distance = Math.Sqrt(deltaX * deltaX + deltaZ * deltaZ);
            if (distance == 0) {
                return;
            }
            double directionX;
            double directionZ;
            if (!TryGetPierAxis(
                    deckPoints, connection.LandPoint,
                    out directionX, out directionZ)) {
                return;
            }

            double terminalProjection = deckPoints.Max(point =>
                point.X * directionX + point.Z * directionZ);
            var terminalEdge = deckPoints
                .Where(point => terminalProjection -
                    (point.X * directionX + point.Z * directionZ) <= 1.25)
                .ToList();
            var originalDeck = new HashSet<BlockPosition>(deckPoints);
            int maximumDistance = Math.Max(15,
                (int)Math.Ceiling(distance)) + 15;

            foreach (var edgePoint in terminalEdge) {
                int targetX = edgePoint.X +
                    (int)Math.Round(directionX * maximumDistance);
                int targetZ = edgePoint.Z +
                    (int)Math.Round(directionZ * maximumDistance);
                foreach (var linePoint in PlotLine(
                        edgePoint.X, edgePoint.Z, targetX, targetZ)) {
                    var point = linePoint.Block;
                    if (!_conv.IsValidPoint(point)) {
                        break;
                    }

                    deckPoints.Add(point);
                    if (!originalDeck.Contains(point) &&
                            IsLandAtPierHeight(point, connection.Height)) {
                        break;
                    }
                }
            }
        }

        private static bool TryGetPierAxis(
                HashSet<BlockPosition> deckPoints, BlockPosition landPoint,
                out double directionX, out double directionZ) {
            directionX = 0;
            directionZ = 0;
            if (deckPoints.Count < 2) {
                return false;
            }

            // The land search identifies which end of the pier faces shore,
            // but an arbitrary land candidate must not determine the heading.
            // Use the footprint's principal axis so the extension continues
            // in the direction in which the pier is already travelling.
            double centerX = deckPoints.Average(point => (double)point.X);
            double centerZ = deckPoints.Average(point => (double)point.Z);
            double xx = 0;
            double xz = 0;
            double zz = 0;
            foreach (var point in deckPoints) {
                double x = point.X - centerX;
                double z = point.Z - centerZ;
                xx += x * x;
                xz += x * z;
                zz += z * z;
            }

            double axisAngle = 0.5 * Math.Atan2(2 * xz, xx - zz);
            directionX = Math.Cos(axisAngle);
            directionZ = Math.Sin(axisAngle);
            double towardLand =
                (landPoint.X - centerX) * directionX +
                (landPoint.Z - centerZ) * directionZ;
            if (towardLand < 0) {
                directionX = -directionX;
                directionZ = -directionZ;
            }

            // Refine the global axis from the final portion of the footprint.
            // This follows the pier's last segment when the overall pier is
            // slightly curved instead of extending along a shore candidate.
            double axisX = directionX;
            double axisZ = directionZ;
            double minimumProjection = deckPoints.Min(point =>
                point.X * axisX + point.Z * axisZ);
            double maximumProjection = deckPoints.Max(point =>
                point.X * axisX + point.Z * axisZ);
            double axisLength = maximumProjection - minimumProjection;
            double innerStart = Math.Min(4, axisLength * 0.15);
            double innerEnd = Math.Min(12, axisLength * 0.4);
            var terminalPoints = deckPoints.Where(point =>
                maximumProjection -
                    (point.X * axisX + point.Z * axisZ) <= 2).ToList();
            var innerPoints = deckPoints.Where(point => {
                double fromEnd = maximumProjection -
                    (point.X * axisX + point.Z * axisZ);
                return fromEnd >= innerStart && fromEnd <= innerEnd;
            }).ToList();
            if (terminalPoints.Count != 0 && innerPoints.Count != 0) {
                double terminalX = terminalPoints.Average(point => (double)point.X);
                double terminalZ = terminalPoints.Average(point => (double)point.Z);
                double innerX = innerPoints.Average(point => (double)point.X);
                double innerZ = innerPoints.Average(point => (double)point.Z);
                double localX = terminalX - innerX;
                double localZ = terminalZ - innerZ;
                double localLength = Math.Sqrt(localX * localX + localZ * localZ);
                if (localLength > 0.5) {
                    directionX = localX / localLength;
                    directionZ = localZ / localLength;
                }
            }
            return true;
        }

        private int GetBuildingBaseHeight(BlockPosition point) {
            int deckHeight;
            return _pierDeckHeights.TryGetValue(point, out deckHeight)
                ? deckHeight
                : GetHeight(point);
        }

        private static int PositiveModulo(int value, int divisor) {
            int result = value % divisor;
            return result < 0 ? result + divisor : result;
        }

        private int FindPierWaterHeight(IList<BlockPosition> polygon,
                int left, int top, int right, int bottom) {
            var waterHeights = new List<int>();
            for (int z = top - 1; z <= bottom + 1; z++) {
                for (int x = left - 1; x <= right + 1; x++) {
                    var point = new BlockPosition(x, z);
                    if (!_conv.IsValidPoint(point)) {
                        continue;
                    }

                    int height = GetHeight(point);
                    int id = _bm.GetID(x, height, z);
                    if (id == BlockType.WATER || id == BlockType.STATIONARY_WATER) {
                        waterHeights.Add(height);
                    }
                }
            }

            if (waterHeights.Count != 0) {
                waterHeights.Sort();
                return waterHeights[waterHeights.Count / 2];
            }

            return GetTerrainHeight(polygon[0]);
        }

        private PierLandConnection FindPierLandConnection(
                IList<BlockPosition> polygon, HashSet<BlockPosition> deckPoints,
                int left, int top, int right, int bottom) {
            const int MaximumLandSearchDistance = 10;
            var candidates = new List<PierLandCandidate>();
            int searchLeft = left - MaximumLandSearchDistance;
            int searchRight = right + MaximumLandSearchDistance;
            int searchTop = top - MaximumLandSearchDistance;
            int searchBottom = bottom + MaximumLandSearchDistance;
            for (int z = searchTop; z <= searchBottom; z++) {
                for (int x = searchLeft; x <= searchRight; x++) {
                    var landPoint = new BlockPosition(x, z);
                    if (deckPoints.Contains(landPoint) ||
                            !_conv.IsValidPoint(landPoint)) {
                        continue;
                    }

                    int surfaceHeight = GetHeight(landPoint);
                    int id = _bm.GetID(x, surfaceHeight, z);
                    if (id == BlockType.WATER || id == BlockType.STATIONARY_WATER ||
                            id == BlockType.AIR) {
                        continue;
                    }

                    BlockPosition pierPoint;
                    int distanceSquared;
                    FindNearestPierPoint(
                        deckPoints, landPoint, out pierPoint, out distanceSquared);
                    candidates.Add(new PierLandCandidate(
                        landPoint,
                        pierPoint,
                        GetTerrainHeight(landPoint),
                        distanceSquared));
                }
            }

            if (candidates.Count != 0) {
                // Use the representative shoreline elevation rather than the
                // single highest solid block in the search area. A tree,
                // building, or other elevated block must not lift the entire
                // pier into the air.
                var landHeights = candidates
                    .Select(candidate => candidate.Height)
                    .OrderBy(height => height)
                    .ToList();
                int landHeight = landHeights[landHeights.Count - 1];
                var anchor = candidates
                    .OrderBy(candidate =>
                        Math.Abs(candidate.Height - landHeight))
                    .ThenBy(candidate => candidate.DistanceSquared)
                    .First();
                return new PierLandConnection(
                    anchor.PierPoint, anchor.LandPoint, anchor.Height);
            }

            var fallback = polygon[0];
            return new PierLandConnection(
                fallback, fallback, GetTerrainHeight(fallback));
        }

        private static void FindNearestPierPoint(
                HashSet<BlockPosition> deckPoints, BlockPosition landPoint,
                out BlockPosition nearest, out int nearestDistanceSquared) {
            nearest = default(BlockPosition);
            nearestDistanceSquared = Int32.MaxValue;
            foreach (var pierPoint in deckPoints) {
                int deltaX = pierPoint.X - landPoint.X;
                int deltaZ = pierPoint.Z - landPoint.Z;
                int distanceSquared = deltaX * deltaX + deltaZ * deltaZ;
                if (distanceSquared < nearestDistanceSquared) {
                    nearest = pierPoint;
                    nearestDistanceSquared = distanceSquared;
                }
            }
        }

        private sealed class PierLandConnection {
            public readonly BlockPosition PierPoint;
            public readonly BlockPosition LandPoint;
            public readonly int Height;

            public PierLandConnection(BlockPosition pierPoint,
                    BlockPosition landPoint, int height) {
                PierPoint = pierPoint;
                LandPoint = landPoint;
                Height = height;
            }
        }

        private sealed class PierLandCandidate {
            public readonly BlockPosition LandPoint;
            public readonly BlockPosition PierPoint;
            public readonly int Height;
            public readonly int DistanceSquared;

            public PierLandCandidate(BlockPosition landPoint,
                    BlockPosition pierPoint, int height, int distanceSquared) {
                LandPoint = landPoint;
                PierPoint = pierPoint;
                Height = height;
                DistanceSquared = distanceSquared;
            }
        }
    }
}
