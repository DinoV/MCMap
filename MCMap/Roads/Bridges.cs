using Substrate;
using System;
using System.Collections.Generic;

namespace MinecraftMapper {
    partial class Mapper {
        partial class RoadRenderer {
            private double PrepareBridgePath(List<BlockPosition> path,
                    out int startHeight, out int endHeight) {
                startHeight = endHeight = 0;
                if (!Way.Bridge) return 0;

                BlockPosition start = FindBridgeAnchor(path[0], path[1], out startHeight);
                BlockPosition end = FindBridgeAnchor(
                    path[path.Count - 1], path[path.Count - 2], out endHeight);
                path.Insert(0, start);
                path.Add(end);

                double length = 0;
                for (int index = 1; index < path.Count; index++)
                    length += Distance(path[index - 1], path[index]);
                return length;
            }

            private static int CalculateBridgeHeight(BlockPosition point,
                    BlockPosition from, BlockPosition to, double segmentLength,
                    double completedLength, double bridgeLength,
                    int startHeight, int endHeight) {
                double x = point.X - from.X;
                double z = point.Z - from.Z;
                double segmentProgress = 0;
                if (segmentLength != 0) {
                    segmentProgress =
                        (x * (to.X - from.X) + z * (to.Z - from.Z)) /
                        (segmentLength * segmentLength);
                    segmentProgress = Math.Max(0, Math.Min(1, segmentProgress));
                }
                double progress = bridgeLength == 0
                    ? 0
                    : (completedLength + segmentProgress * segmentLength) / bridgeLength;
                return (int)Math.Round(
                    startHeight + (endHeight - startHeight) * progress,
                    MidpointRounding.AwayFromZero);
            }

            private void DrawBridgeEdgePoint(
                    Dictionary<BlockPosition, RoadPoint> roadPoints,
                    LinePosition point, RoadSegment segment, int height) {
                string edgeBlockName = GetFullBlockName(BlockName);
                _bm.SetBlock(point.Block.X, height - 1, point.Block.Z, edgeBlockName);
                if (_bm.GetID(point.Block.X, height - 2, point.Block.Z) == BlockType.AIR)
                    _bm.SetBlock(point.Block.X, height - 2, point.Block.Z, edgeBlockName);
                AddRoadPoint(roadPoints, point.Block, segment, height,
                    false, edgeBlockName);
                ClearPlantLife(_bm, point.Block, height);
            }

            private void EnsureBridgeSupport(BlockPosition point, int height,
                    string blockName) {
                if (Way.Bridge &&
                        _bm.GetID(point.X, height - 2, point.Z) == BlockType.AIR) {
                    _bm.SetBlock(point.X, height - 2, point.Z,
                        GetFullBlockName(blockName));
                }
            }

            private static double Distance(BlockPosition first, BlockPosition second) {
                double x = second.X - first.X;
                double z = second.Z - first.Z;
                return Math.Sqrt(x * x + z * z);
            }

            private BlockPosition FindBridgeAnchor(
                    BlockPosition endpoint, BlockPosition interior, out int height) {
                const int ApproachSearchLength = 10;
                double xDirection = endpoint.X - interior.X;
                double zDirection = endpoint.Z - interior.Z;
                double directionLength = Math.Sqrt(
                    xDirection * xDirection + zDirection * zDirection);
                if (directionLength == 0) {
                    height = _bm.GetHeight(endpoint.X, endpoint.Z);
                    return endpoint;
                }

                xDirection /= directionLength;
                zDirection /= directionLength;
                BlockPosition anchor = endpoint;
                height = _bm.GetHeight(endpoint.X, endpoint.Z);
                for (int distance = 1; distance <= ApproachSearchLength; distance++) {
                    var candidate = new BlockPosition(
                        (int)Math.Round(endpoint.X + xDirection * distance,
                            MidpointRounding.AwayFromZero),
                        (int)Math.Round(endpoint.Z + zDirection * distance,
                            MidpointRounding.AwayFromZero));
                    if (!_conv.IsValidPoint(candidate)) break;
                    int candidateHeight = _bm.GetHeight(candidate.X, candidate.Z);
                    if (candidateHeight >= height) {
                        height = candidateHeight;
                        anchor = candidate;
                    }
                }
                return anchor;
            }
        }
    }
}
