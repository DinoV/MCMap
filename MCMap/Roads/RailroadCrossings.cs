using Substrate;
using Substrate.TileEntities;
using System;
using System.Collections.Generic;
using System.Linq;

namespace MinecraftMapper {
    partial class Mapper {
        private const int MaximumRailroadCrossingRoadWidth = 48;

        private void DrawRailroadCrossings(Dictionary<BlockPosition, RoadPoint> roadPoints) {
            foreach (var crossing in _reader.Signs
                    .Where(item => item.Value.Type == OsmReader.SignType.RailroadCrossing)
                    .OrderBy(item => MapOrder(_conv, item.Key))) {
                List<OsmReader.Way> roads;
                if (!_reader.RoadsByNode.TryGetValue(crossing.Key, out roads) || roads.Count == 0) {
                    WriteLine("Railroad crossing {0} is not part of a rendered road", crossing.Value.Id);
                    continue;
                }

                var center = _conv.ToBlock(crossing.Key.Lat, crossing.Key.Long);
                if (!_conv.IsValidPoint(center)) {
                    continue;
                }

                var road = roads.OrderByDescending(item => item.Lanes ?? 0).First();
                Direction armDirection;
                if (!TryGetRailroadCrossingArmDirection(road, crossing.Key, out armDirection)) {
                    WriteLine("Unable to determine road direction for railroad crossing {0}", crossing.Value.Id);
                    continue;
                }

                DrawRailroadCrossing(roadPoints, center, armDirection, crossing.Value.Id);
            }
        }

        private bool TryGetRailroadCrossingArmDirection(
                OsmReader.Way road,
                OsmReader.Node crossing,
                out Direction armDirection) {
            int index = Array.IndexOf(road.Nodes, crossing);
            if (index < 0) {
                armDirection = Direction.East;
                return false;
            }

            var center = _conv.ToBlock(crossing.Lat, crossing.Long);
            BlockPosition adjacent;
            if (index + 1 < road.Nodes.Length) {
                adjacent = _conv.ToBlock(road.Nodes[index + 1].Lat, road.Nodes[index + 1].Long);
            } else if (index > 0) {
                adjacent = _conv.ToBlock(road.Nodes[index - 1].Lat, road.Nodes[index - 1].Long);
            } else {
                armDirection = Direction.East;
                return false;
            }

            int roadX = adjacent.X - center.X;
            int roadZ = adjacent.Z - center.Z;
            if (roadX == 0 && roadZ == 0) {
                armDirection = Direction.East;
                return false;
            }

            // The overhead arm runs across the road, perpendicular to its direction.
            armDirection = Math.Abs(roadX) > Math.Abs(roadZ) ? Direction.South : Direction.East;
            return true;
        }

        private void DrawRailroadCrossing(
                Dictionary<BlockPosition, RoadPoint> roadPoints,
                BlockPosition center,
                Direction armDirection,
                long osmId) {
            int xDelta = armDirection == Direction.East ? 1 : 0;
            int zDelta = armDirection == Direction.South ? 1 : 0;
            int negativeWidth = MeasureRoadWidth(roadPoints, center, -xDelta, -zDelta);
            int positiveWidth = MeasureRoadWidth(roadPoints, center, xDelta, zDelta);

            // Start one block beyond the near edge and finish one block beyond the far edge.
            var pole = new BlockPosition(
                center.X - (negativeWidth + 1) * xDelta,
                center.Z - (negativeWidth + 1) * zDelta);
            int spanLength = negativeWidth + positiveWidth + 3;
            int height = _bm.GetHeight(pole.X, pole.Z);
            int top = height + TrafficLightHeight - 1;

            WriteLine("Railroad crossing {0} at {1}, span {2}", osmId, center, spanLength);
            _bm.SetBlock(pole.X, height, pole.Z, AcquaticBlocks.PolishedAndesite);
            _bm.SetBlock(pole.X, height + 1, pole.Z, AcquaticBlocks.Anvil, facing: BlockFacing.West);
            for (int y = height + 2; y <= top; y++) {
                SetRailroadCrossingPost(pole.X, y, pole.Z);
            }

            for (int offset = 1; offset < spanLength; offset++) {
                SetRailroadCrossingPost(pole.X + offset * xDelta, top, pole.Z + offset * zDelta);
            }

            // Keep two signal heads spread across the measured span as the road gets wider.
            int firstSignal = Math.Max(2, spanLength / 3);
            int secondSignal = Math.Min(spanLength - 2, (spanLength * 2) / 3);
            DrawRailroadCrossingSignal(pole, height, armDirection, firstSignal);
            if (secondSignal != firstSignal) {
                DrawRailroadCrossingSignal(pole, height, armDirection, secondSignal);
            }
        }

        private static int MeasureRoadWidth(
                Dictionary<BlockPosition, RoadPoint> roadPoints,
                BlockPosition center,
                int xDelta,
                int zDelta) {
            int width = 0;
            for (int offset = 1; offset <= MaximumRailroadCrossingRoadWidth; offset++) {
                var point = new BlockPosition(center.X + offset * xDelta, center.Z + offset * zDelta);
                if (!roadPoints.ContainsKey(point)) {
                    break;
                }
                width++;
            }
            return width;
        }

        private void DrawRailroadCrossingSignal(
                BlockPosition pole,
                int height,
                Direction armDirection,
                int offset) {
            int xDelta = armDirection == Direction.East ? 1 : 0;
            int zDelta = armDirection == Direction.South ? 1 : 0;
            int x = pole.X + offset * xDelta;
            int z = pole.Z + offset * zDelta;
            int top = height + TrafficLightHeight - 1;

            _bm.SetBlock(x, top, z, AcquaticBlocks.BlackWool);
            _bm.SetBlock(x, top - 1, z, AcquaticBlocks.BlackWool);

            int bannerX;
            int bannerZ;
            int bannerData;
            GetTrafficSignalSignInfo(armDirection, x, z, out bannerX, out bannerZ, out bannerData);

            AlphaBlock bannerBlock = new AlphaBlock(BlockType.WALL_BANNER);
            var banner = bannerBlock.GetTileEntity() as TileEntityBanner;
            banner.BaseColor = BannerColor.Black;
            banner.Patterns = new[] {
                new BannerPattern(BannerColor.White, "minecraft:diagonal_cross")
            };

            _bm.SetBlock(
                bannerX,
                top,
                bannerZ,
                AcquaticBlocks.BlackWallBanner,
                facing: WallFacingFromLegacyData(bannerData));
            _bm.SetTileEntity(bannerX, top, bannerZ, banner);
        }

        private void SetRailroadCrossingPost(int x, int y, int z) {
            _bm.SetBlock(
                x,
                y,
                z,
                AcquaticBlocks.CobblestoneWall,
                north: false,
                east: false,
                south: false,
                west: false,
                up: false);
        }
    }
}
