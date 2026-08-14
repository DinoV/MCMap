using Substrate;
using System.Collections.Generic;

namespace MinecraftMapper {
    partial class Mapper {
        partial class RoadRenderer {
            private const int SidewalkWidth = 3;

            private void DrawSidewalkPoint(
                    Dictionary<BlockPosition, RoadPoint> roadPoints,
                    LinePosition point, RoadSegment segment, int height,
                    string edgeBlockName) {
                if (_bm.GetHeight(point.Block.X, point.Block.Z) == height - 2 ||
                        _bm.GetHeight(point.Block.X - 1, point.Block.Z) == height - 2 ||
                        _bm.GetHeight(point.Block.X + 1, point.Block.Z) == height - 2 ||
                        _bm.GetHeight(point.Block.X, point.Block.Z - 1) == height - 2 ||
                        _bm.GetHeight(point.Block.X, point.Block.Z + 1) == height - 2) {
                    _bm.SetBlock(point.Block.X, height - 1, point.Block.Z,
                        AcquaticBlocks.StoneSlab,
                        type: BlockSlabType.Double, waterlogged: false);
                }

                _bm.SetBlock(point.Block.X, height - 1, point.Block.Z,
                    edgeBlockName, _edgeProperties);
                if (_bm.GetID(point.Block.X, height - 2, point.Block.Z) == BlockType.AIR) {
                    if (Way.Bridge) {
                        _bm.SetBlock(point.Block.X, height - 2, point.Block.Z,
                            GetFullBlockName(edgeBlockName));
                    } else {
                        _bm.SetBlock(point.Block.X, height - 2, point.Block.Z,
                            AcquaticBlocks.StoneSlab,
                            type: BlockSlabType.Double, waterlogged: false);
                    }
                }
                AddRoadPoint(roadPoints, point.Block, segment, height, true,
                    edgeBlockName, _edgeProperties);
                ClearPlantLife(_bm, point.Block, height);
            }
        }
    }
}
