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
        private static readonly string[] FlagBannerBlocks = {
            AcquaticBlocks.WhiteWallBanner, AcquaticBlocks.OrangeWallBanner,
            AcquaticBlocks.MagentaWallBanner, AcquaticBlocks.LightBlueWallBanner,
            AcquaticBlocks.YellowWallBanner, AcquaticBlocks.LimeWallBanner,
            AcquaticBlocks.PinkWallBanner, AcquaticBlocks.GrayWallBanner,
            AcquaticBlocks.LightGrayWallBanner, AcquaticBlocks.CyanWallBanner,
            AcquaticBlocks.PurpleWallBanner, AcquaticBlocks.BlueWallBanner,
            AcquaticBlocks.BrownWallBanner, AcquaticBlocks.GreenWallBanner,
            AcquaticBlocks.RedWallBanner, AcquaticBlocks.BlackWallBanner,
        };

        private static readonly BannerColor[] FlagBannerColors = {
            BannerColor.White, BannerColor.Orange, BannerColor.LightPurple, BannerColor.LightBlue,
            BannerColor.Yellow, BannerColor.LightGreen, BannerColor.Pink, BannerColor.DaryGray,
            BannerColor.LightGray, BannerColor.Cyan, BannerColor.Purple, BannerColor.Blue,
            BannerColor.Brown, BannerColor.Green, BannerColor.Red, BannerColor.Black,
        };

        private static readonly HashSet<string> PointFeatureGroundCoverBlocks =
            new HashSet<string> {
                AcquaticBlocks.ShortGrass, AcquaticBlocks.TallGrass,
                AcquaticBlocks.Fern, AcquaticBlocks.LargeFern,
                AcquaticBlocks.DeadBush, AcquaticBlocks.Bush,
                AcquaticBlocks.FireflyBush, AcquaticBlocks.LeafLitter,
                AcquaticBlocks.Dandelion, AcquaticBlocks.Poppy,
                AcquaticBlocks.BlueOrchid, AcquaticBlocks.Allium,
                AcquaticBlocks.AzureBluet, AcquaticBlocks.RedTulip,
                AcquaticBlocks.OrangeTulip, AcquaticBlocks.WhiteTulip,
                AcquaticBlocks.PinkTulip, AcquaticBlocks.OxeyeDaisy,
                AcquaticBlocks.Cornflower, AcquaticBlocks.LilyOfTheValley,
                AcquaticBlocks.WitherRose, AcquaticBlocks.Torchflower,
                AcquaticBlocks.Sunflower, AcquaticBlocks.Lilac,
                AcquaticBlocks.RoseBush, AcquaticBlocks.Peony,
                AcquaticBlocks.PitcherPlant, AcquaticBlocks.Wildflowers,
                AcquaticBlocks.ClosedEyeblossom, AcquaticBlocks.OpenEyeblossom,
                AcquaticBlocks.CactusFlower,
            };

        private static readonly string[] PublicBookcaseFenceBlocks = {
            AcquaticBlocks.OakFence,
            AcquaticBlocks.SpruceFence,
            AcquaticBlocks.BirchFence,
            AcquaticBlocks.JungleFence,
            AcquaticBlocks.AcaciaFence,
            AcquaticBlocks.DarkOakFence,
            AcquaticBlocks.MangroveFence,
            AcquaticBlocks.CherryFence,
            AcquaticBlocks.BambooFence,
            AcquaticBlocks.PaleOakFence,
        };

        private static readonly string[] ParcelLockerShulkerBoxes = {
            AcquaticBlocks.WhiteShulkerBox,
            AcquaticBlocks.OrangeShulkerBox,
            AcquaticBlocks.MagentaShulkerBox,
            AcquaticBlocks.LightBlueShulkerBox,
            AcquaticBlocks.YellowShulkerBox,
            AcquaticBlocks.LimeShulkerBox,
            AcquaticBlocks.PinkShulkerBox,
            AcquaticBlocks.GrayShulkerBox,
            AcquaticBlocks.LightGrayShulkerBox,
            AcquaticBlocks.CyanShulkerBox,
            AcquaticBlocks.PurpleShulkerBox,
            AcquaticBlocks.BlueShulkerBox,
            AcquaticBlocks.BrownShulkerBox,
            AcquaticBlocks.GreenShulkerBox,
            AcquaticBlocks.RedShulkerBox,
            AcquaticBlocks.BlackShulkerBox,
        };

        private void DrawStreetLamps(Dictionary<BlockPosition, RoadPoint> roadPoints) {
            int cur = 0;
            foreach (var sign in _reader.Signs.OrderBy(x => MapOrder(_conv, x.Key))) {
                if ((++cur % 100) == 0) {
                    SaveBlocks();
                }

                if (sign.Value.Type == OsmReader.SignType.StreetLamp) {
                    var pos = _conv.ToBlock(sign.Key.Lat, sign.Key.Long);
                    Direction? dir = null;
                    if (sign.Value.Direction != null) {
                        switch (sign.Value.Direction) {
                            case 0: dir = Direction.North; break;
                            case 90: dir = Direction.East; break;
                            case 180: dir = Direction.South; break;
                            case 270: dir = Direction.West; break;
                        }
                    }
                    if (dir == null) {
                        dir = Direction.East;
                        for (int i = 0; i < 10; i++) {
                            if (roadPoints.ContainsKey(new BlockPosition(pos.X - 1, pos.Z))) {
                                dir = Direction.East;
                            } else if (roadPoints.ContainsKey(new BlockPosition(pos.X + 1, pos.Z))) {
                                dir = Direction.West;
                            } else if (roadPoints.ContainsKey(new BlockPosition(pos.X, pos.Z - 1))) {
                                dir = Direction.South;
                            } else if (roadPoints.ContainsKey(new BlockPosition(pos.X, pos.Z + 1))) {
                                dir = Direction.North;
                            }
                        }
                    }
                    WriteLine("Lamp at {0} facing {1}", pos, dir);
                    DrawStreetLamp(dir.Value, pos);
                }
            }
        }

        private void DrawBarriers(Dictionary<BlockPosition, int> buildingPositions) {
            int cur = 0;
            foreach (var barrier in _reader.Barriers.OrderBy(x => MapOrder(_conv, x.Nodes.First()))) {
                if ((++cur % 100) == 0) {
                    SaveBlocks();
                }
                int blockKind = 0, height = 1;
                switch (barrier.Kind) {
                    case OsmReader.BarrierKind.Fence: blockKind = BlockType.FENCE; break;
                    case OsmReader.BarrierKind.Gate: blockKind = BlockType.FENCE_GATE; break;
                    case OsmReader.BarrierKind.GuardRail: blockKind = BlockType.NETHER_BRICK_FENCE; break;
                    case OsmReader.BarrierKind.Hedge: blockKind = BlockType.LEAVES; height = 2; break;
                    case OsmReader.BarrierKind.RetainingWall: blockKind = BlockType.STONE_BRICK; height = 2; break;
                    case OsmReader.BarrierKind.Wall:
                        switch (barrier.Wall) {
                            case OsmReader.Wall.Brick:
                                blockKind = BlockType.BRICK_BLOCK; break;
                            default:
                                blockKind = BlockType.STONE;
                                break;
                        }
                        height = 4;
                        break;
                }

                var start = barrier.Nodes[0];
                HashSet<BlockPosition> wallPoints = new HashSet<BlockPosition>();
                for (int i = 1; i < barrier.Nodes.Length; i++) {
                    var from = _conv.ToBlock(start.Lat, start.Long);
                    WriteLine("Barrier {0} {1}", from, barrier.Kind);
                    var to = _conv.ToBlock(barrier.Nodes[i].Lat, barrier.Nodes[i].Long);

                    foreach (var point in PlotLine(from.X, from.Z, to.X, to.Z)) {
                        if (buildingPositions.ContainsKey(point.Block) ||
                            wallPoints.Contains(point.Block)) {
                            continue;
                        }
                        wallPoints.Add(point.Block);

                        var curHeight = _bm.GetHeight(point.Block.X, point.Block.Z);
                        for (int y = 0; y < height; y++) {
                            SetMappedBlock(_bm, point.Block.X, curHeight + y, point.Block.Z, blockKind, 0);
                        }
                    }

                    start = barrier.Nodes[i];
                }
            }
        }

        private void DrawPointFeatures(
                Dictionary<BlockPosition, RoadPoint> roadPoints,
                Dictionary<BlockPosition, int> buildingPositions) {
            int cur = 0;
            var occupiedPointFeatures = new HashSet<BlockPosition>();
            foreach (var feature in _reader.PointFeatures
                    .OrderBy(item => MapOrder(_conv, item.Node))) {
                if ((++cur % 100) == 0) SaveBlocks();
                var point = _conv.ToBlock(feature.Node.Lat, feature.Node.Long);
                point = FindClosestFreePointFeatureLocation(
                    point, roadPoints, buildingPositions, occupiedPointFeatures);
                if (!_conv.IsValidPoint(point) || buildingPositions.ContainsKey(point)) continue;
                int groundHeight = GetPointFeatureGroundHeight(point);
                ClearPointFeatureGroundCover(point, groundHeight);
                int height = groundHeight + 1;
                WriteLine("Point feature {0} {1} ({2})", point, feature.Kind, feature.Id);
                switch (feature.Kind) {
                    case PointFeatureKind.Bollard:
                        DrawWallColumn(point, height, 1, AcquaticBlocks.SandstoneWall);
                        break;
                    case PointFeatureKind.UtilityPole:
                        DrawWallColumn(point, height, 10, AcquaticBlocks.RedSandstoneWall);
                        break;
                    case PointFeatureKind.Flagpole:
                        DrawWallColumn(point, height, 5, AcquaticBlocks.DioriteWall);
                        DrawFlagpoleBanner(feature, point, height + 4);
                        break;
                    case PointFeatureKind.PublicBookcase:
                        DrawPublicBookcase(feature, point, height);
                        break;
                    case PointFeatureKind.ParcelLocker:
                        DrawParcelLocker(feature, point, height, roadPoints, buildingPositions);
                        break;
                    case PointFeatureKind.PostBox:
                        DrawPostBox(point, height, roadPoints, buildingPositions);
                        break;
                    case PointFeatureKind.RecyclingPoint:
                        _bm.SetBlock(point.X, height, point.Z, AcquaticBlocks.WaterCauldron, level: 3);
                        break;
                    case PointFeatureKind.WasteBasket:
                        _bm.SetBlock(point.X, height, point.Z, AcquaticBlocks.LavaCauldron);
                        break;
                }
                occupiedPointFeatures.Add(point);
            }
        }

        private BlockPosition FindClosestFreePointFeatureLocation(
                BlockPosition origin,
                Dictionary<BlockPosition, RoadPoint> roadPoints,
                Dictionary<BlockPosition, int> buildingPositions,
                HashSet<BlockPosition> occupiedPointFeatures) {
            const int SearchRadius = 6;
            var surfaceCounts = new Dictionary<int, int>();
            for (int x = -SearchRadius; x <= SearchRadius; x++) {
                for (int z = -SearchRadius; z <= SearchRadius; z++) {
                    var candidate = new BlockPosition(origin.X + x, origin.Z + z);
                    if (!IsFreePointFeatureColumn(
                            candidate, roadPoints, buildingPositions, occupiedPointFeatures)) {
                        continue;
                    }
                    int surface = GetPointFeatureGroundHeight(candidate);
                    int count;
                    surfaceCounts[surface] = surfaceCounts.TryGetValue(surface, out count)
                        ? count + 1
                        : 1;
                }
            }
            if (surfaceCounts.Count == 0) {
                return origin;
            }

            int terrainHeight = surfaceCounts
                .OrderByDescending(item => item.Value)
                .ThenBy(item => item.Key)
                .First().Key;
            for (int distance = 0; distance <= SearchRadius * 2; distance++) {
                for (int x = -SearchRadius; x <= SearchRadius; x++) {
                    int zDistance = distance - Math.Abs(x);
                    if (zDistance < 0 || zDistance > SearchRadius) {
                        continue;
                    }
                    var first = new BlockPosition(origin.X + x, origin.Z - zDistance);
                    if (IsFreePointFeatureLocationAtHeight(
                            first, terrainHeight, roadPoints, buildingPositions, occupiedPointFeatures)) {
                        return first;
                    }
                    if (zDistance != 0) {
                        var second = new BlockPosition(origin.X + x, origin.Z + zDistance);
                        if (IsFreePointFeatureLocationAtHeight(
                                second, terrainHeight, roadPoints, buildingPositions, occupiedPointFeatures)) {
                            return second;
                        }
                    }
                }
            }
            return origin;
        }

        private bool IsFreePointFeatureLocationAtHeight(
                BlockPosition point,
                int terrainHeight,
                Dictionary<BlockPosition, RoadPoint> roadPoints,
                Dictionary<BlockPosition, int> buildingPositions,
                HashSet<BlockPosition> occupiedPointFeatures) {
            return IsFreePointFeatureColumn(
                    point, roadPoints, buildingPositions, occupiedPointFeatures) &&
                Math.Abs(GetPointFeatureGroundHeight(point) - terrainHeight) <= 1;
        }

        private bool IsFreePointFeatureColumn(
                BlockPosition point,
                Dictionary<BlockPosition, RoadPoint> roadPoints,
                Dictionary<BlockPosition, int> buildingPositions,
                HashSet<BlockPosition> occupiedPointFeatures) {
            return _conv.IsValidPoint(point) &&
                !roadPoints.ContainsKey(point) &&
                !buildingPositions.ContainsKey(point) &&
                !occupiedPointFeatures.Contains(point);
        }

        private int GetPointFeatureGroundHeight(BlockPosition point) {
            int height = GetHeight(point);
            while (height > 0 && PointFeatureGroundCoverBlocks.Contains(
                    _bm.GetStringID(point.X, height, point.Z))) {
                height--;
            }
            return height;
        }

        private void ClearPointFeatureGroundCover(BlockPosition point, int groundHeight) {
            for (int y = groundHeight + 1; y <= groundHeight + 3; y++) {
                string block = _bm.GetStringID(point.X, y, point.Z);
                if (PointFeatureGroundCoverBlocks.Contains(block)) {
                    _bm.SetBlock(point.X, y, point.Z, AcquaticBlocks.Air);
                } else if (block != AcquaticBlocks.Air) {
                    break;
                }
            }
        }

        private void DrawPublicBookcase(PointFeature feature, BlockPosition point, int height) {
            int fence = (int)(unchecked((ulong)feature.Id) % (ulong)PublicBookcaseFenceBlocks.Length);
            _bm.SetBlock(
                point.X,
                height,
                point.Z,
                PublicBookcaseFenceBlocks[fence],
                north: false,
                east: false,
                south: false,
                west: false);
            _bm.SetBlock(point.X, height + 1, point.Z, AcquaticBlocks.Bookshelf);
        }

        private void DrawParcelLocker(
                PointFeature feature,
                BlockPosition point,
                int height,
                Dictionary<BlockPosition, RoadPoint> roadPoints,
                Dictionary<BlockPosition, int> buildingPositions) {
            string block = String.Equals(feature.Brand, "Amazon Hub", StringComparison.OrdinalIgnoreCase)
                ? AcquaticBlocks.OrangeShulkerBox
                : ParcelLockerShulkerBoxes[(int)(unchecked((ulong)feature.Id) % (ulong)ParcelLockerShulkerBoxes.Length)];
            BlockFacing facing = FindPointFeatureFacing(point, roadPoints, buildingPositions);
            int widthX = facing == BlockFacing.North || facing == BlockFacing.South ? 1 : 0;
            int widthZ = widthX == 0 ? 1 : 0;

            for (int width = 0; width < 2; width++) {
                for (int y = 0; y < 2; y++) {
                    SetShulkerBox(
                        point.X + width * widthX,
                        height + y,
                        point.Z + width * widthZ,
                        block,
                        facing);
                }
            }
        }

        private void DrawPostBox(
                BlockPosition point,
                int height,
                Dictionary<BlockPosition, RoadPoint> roadPoints,
                Dictionary<BlockPosition, int> buildingPositions) {
            BlockFacing facing = FindPointFeatureFacing(point, roadPoints, buildingPositions);
            SetShulkerBox(
                point.X,
                height,
                point.Z,
                AcquaticBlocks.BlueShulkerBox,
                facing);

            var sign = new TileEntitySign(TileEntitySign.HangingTypeId);
            SetSignName(new List<string> { "USPS" }, sign);
            BlockPosition signPoint = FindPostBoxSignPosition(
                point, facing, roadPoints, buildingPositions);
            _bm.SetBlock(
                signPoint.X,
                height,
                signPoint.Z,
                AcquaticBlocks.PaleOakWallHangingSign,
                facing: facing,
                waterlogged: false);
            _bm.SetTileEntity(signPoint.X, height, signPoint.Z, sign);
        }

        private void SetShulkerBox(
                int x, int y, int z, string block, BlockFacing facing) {
            _bm.SetBlock(x, y, z, block, facing: facing);
            _bm.SetTileEntity(x, y, z, new TileEntity("minecraft:shulker_box"));
        }

        private BlockPosition FindPostBoxSignPosition(
                BlockPosition point,
                BlockFacing facing,
                Dictionary<BlockPosition, RoadPoint> roadPoints,
                Dictionary<BlockPosition, int> buildingPositions) {
            BlockPosition[] candidates = facing == BlockFacing.East || facing == BlockFacing.West
                ? new[] {
                    new BlockPosition(point.X, point.Z - 1),
                    new BlockPosition(point.X, point.Z + 1),
                }
                : new[] {
                    new BlockPosition(point.X - 1, point.Z),
                    new BlockPosition(point.X + 1, point.Z),
                };

            foreach (BlockPosition candidate in candidates) {
                if (_conv.IsValidPoint(candidate) &&
                        !roadPoints.ContainsKey(candidate) &&
                        !buildingPositions.ContainsKey(candidate)) {
                    return candidate;
                }
            }
            return candidates[0];
        }

        private static BlockFacing FindPointFeatureFacing(
                BlockPosition point,
                Dictionary<BlockPosition, RoadPoint> roadPoints,
                Dictionary<BlockPosition, int> buildingPositions) {
            const int SearchRadius = 20;
            BlockPosition? nearest = null;
            int nearestDistance = Int32.MaxValue;
            for (int x = -SearchRadius; x <= SearchRadius; x++) {
                for (int z = -SearchRadius; z <= SearchRadius; z++) {
                    if (x == 0 && z == 0) continue;
                    BlockPosition target;
                    if (IsRenderedFeatureAt(point.X + x, point.Z + z, roadPoints, buildingPositions, out target)) {
                        int distance = x * x + z * z;
                        if (distance < nearestDistance) {
                            nearest = target;
                            nearestDistance = distance;
                        }
                    }
                }
            }
            return nearest == null ? BlockFacing.North : FacingToward(point, nearest.Value);
        }

        private static bool IsRenderedFeatureAt(
                int x,
                int z,
                Dictionary<BlockPosition, RoadPoint> roadPoints,
                Dictionary<BlockPosition, int> buildingPositions,
                out BlockPosition target) {
            target = new BlockPosition(x, z);
            return roadPoints.ContainsKey(target) || buildingPositions.ContainsKey(target);
        }

        private static BlockFacing FacingToward(BlockPosition from, BlockPosition to) {
            int xDelta = to.X - from.X;
            int zDelta = to.Z - from.Z;
            if (Math.Abs(xDelta) > Math.Abs(zDelta)) {
                return xDelta < 0 ? BlockFacing.West : BlockFacing.East;
            }
            return zDelta < 0 ? BlockFacing.North : BlockFacing.South;
        }

        private void DrawWallColumn(BlockPosition point, int height, int blocks, string block) {
            for (int y = 0; y < blocks; y++) {
                _bm.SetBlock(point.X, height + y, point.Z, block,
                    north: false, east: false, south: false, west: false, up: false);
            }
        }

        private void DrawFlagpoleBanner(PointFeature feature, BlockPosition point, int height) {
            int color = (int)(unchecked((ulong)feature.Id) % (ulong)FlagBannerBlocks.Length);
            var bannerBlock = new AlphaBlock(BlockType.WALL_BANNER);
            var banner = bannerBlock.GetTileEntity() as TileEntityBanner;
            banner.BaseColor = FlagBannerColors[color];
            banner.Patterns = new BannerPattern[0];
            int bannerX = point.X + 1;
            _bm.SetBlock(bannerX, height, point.Z, FlagBannerBlocks[color],
                facing: BlockFacing.East);
            _bm.SetTileEntity(bannerX, height, point.Z, banner);
        }

        private void DrawStreetLamp(Direction direction, BlockPosition center) {
            var height = GetHeight(center.X, center.Z);
            const int LampHeight = 8, Length = 3;
            for (int i = 0; i < LampHeight; i++) {
                _bm.SetBlock(center.X, height + i, center.Z, AcquaticBlocks.CobblestoneWall, north: false, east: false, south: false, west: false, up: false);
            }

            for (int i = 0; i < Length; i++) {
                int x = center.X, z = center.Z;
                switch (direction) {
                    case Direction.East: x += i; break;
                    case Direction.West: x -= i; break;
                    case Direction.North: z -= i; break;
                    case Direction.South: z += i; break;
                }

                _bm.SetBlock(x, height + LampHeight, z, AcquaticBlocks.StoneSlab, type: BlockSlabType.Bottom, waterlogged: false);
                if (i == Length - 1) {
                    _bm.SetBlock(x, height + LampHeight - 1, z, AcquaticBlocks.SeaLantern);
                }
            }
        }


        private void DrawBusStop(Direction direction, BlockPosition center) {
            if (center.X < 5 || center.Z < 5) {
                return;
            }
            int height;
            if (!TryPrepareBusStopFootprint(direction, center, out height)) {
                return;
            }
            // draw sides
            if (direction == Direction.South || direction == Direction.North) {
                // draw sides
                for (int z = 0; z < 3; z++) {
                    var zLoc = direction == Direction.North ? center.Z - z : center.Z + z;
                    for (int y = 0; y < 3; y++) {
                        if (y == 0) {
                            _bm.SetBlock(center.X - 2, height + y, zLoc, AcquaticBlocks.BlueStainedGlassPane,
                                north: false, east: false, south: false, west: false);
                            _bm.SetBlock(center.X + 2, height + y, zLoc, AcquaticBlocks.BlueStainedGlassPane,
                                north: false, east: false, south: false, west: false);
                        } else {
                            _bm.SetBlock(center.X - 2, height + y, zLoc, AcquaticBlocks.GlassPane, north: false, east: false, south: false, west: false);
                            _bm.SetBlock(center.X + 2, height + y, zLoc, AcquaticBlocks.GlassPane, north: false, east: false, south: false, west: false);
                        }
                    }
                    // draw top
                    for (int x = -2; x <= 2; x++) {
                        _bm.SetBlock(center.X + x, height + 3, zLoc, AcquaticBlocks.AcaciaSlab,
                            type: BlockSlabType.Bottom, waterlogged: false);
                    }
                }
                // draw back
                for (int x = -2; x < 2; x++) {
                    for (int y = 0; y < 3; y++) {
                        if (y == 0) {
                            _bm.SetBlock(center.X + x, height + y, center.Z, AcquaticBlocks.BlueStainedGlassPane,
                                north: false, east: false, south: false, west: false);
                        } else {
                            _bm.SetBlock(center.X + x, height + y, center.Z, AcquaticBlocks.GlassPane, north: false, east: false, south: false, west: false);
                        }
                    }
                }
            } else {
                // draw sides
                for (int x = 0; x < 3; x++) {
                    var xLoc = direction == Direction.West ? center.X - x : center.X + x;
                    for (int y = 0; y < 3; y++) {
                        if (y == 0) {
                            _bm.SetBlock(xLoc, height + y, center.Z - 2, AcquaticBlocks.BlueStainedGlassPane,
                                north: false, east: false, south: false, west: false);
                            _bm.SetBlock(xLoc, height + y, center.Z + 2, AcquaticBlocks.BlueStainedGlassPane,
                                north: false, east: false, south: false, west: false);
                        } else {
                            _bm.SetBlock(xLoc, height + y, center.Z - 2, AcquaticBlocks.GlassPane, north: false, east: false, south: false, west: false);
                            _bm.SetBlock(xLoc, height + y, center.Z + 2, AcquaticBlocks.GlassPane, north: false, east: false, south: false, west: false);
                        }
                    }
                    // draw top
                    for (int z = -2; z <= 2; z++) {
                        _bm.SetBlock(xLoc, height + 3, center.Z + z, AcquaticBlocks.AcaciaSlab,
                            type: BlockSlabType.Bottom, waterlogged: false);
                    }
                }
                // draw back
                for (int z = -2; z < 2; z++) {
                    for (int y = 0; y < 3; y++) {
                        if (y == 0) {
                            _bm.SetBlock(center.X, height + y, center.Z + z, AcquaticBlocks.BlueStainedGlassPane,
                                north: false, east: false, south: false, west: false);
                        } else {
                            _bm.SetBlock(center.X, height + y, center.Z + z, AcquaticBlocks.GlassPane, north: false, east: false, south: false, west: false);
                        }
                    }
                }
            }
        }

        private bool TryPrepareBusStopFootprint(Direction direction,
                BlockPosition center, out int shelterBaseHeight) {
            var footprint = new List<BlockPosition>();
            if (direction == Direction.South || direction == Direction.North) {
                for (int z = 0; z < 3; z++) {
                    int zLoc = direction == Direction.North ? center.Z - z : center.Z + z;
                    for (int x = -2; x <= 2; x++) {
                        footprint.Add(new BlockPosition(center.X + x, zLoc));
                    }
                }
            } else {
                for (int x = 0; x < 3; x++) {
                    int xLoc = direction == Direction.West ? center.X - x : center.X + x;
                    for (int z = -2; z <= 2; z++) {
                        footprint.Add(new BlockPosition(xLoc, center.Z + z));
                    }
                }
            }

            if (footprint.Any(point => !_conv.IsValidPoint(point))) {
                shelterBaseHeight = 0;
                return false;
            }

            var groundHeights = footprint.ToDictionary(
                point => point,
                point => GetTerrainHeight(point));
            int maxGroundHeight = groundHeights.Values.Max();

            foreach (var point in footprint) {
                int groundHeight = groundHeights[point];
                ClearNonSolidBlocks(point, groundHeight + 1, maxGroundHeight + 4);
                for (int y = groundHeight + 1; y <= maxGroundHeight; y++) {
                    _bm.SetBlock(point.X, y, point.Z,
                        AcquaticBlocks.GrassBlock, snowy: false);
                }
            }

            shelterBaseHeight = maxGroundHeight + 1;
            return true;
        }

        private int GetTerrainHeight(BlockPosition point) {
            int height = GetHeight(point);
            while (height > 0) {
                int id = _bm.GetID(point.X, height, point.Z);
                if (BlockInfo.BlockTable[id].State != BlockState.NONSOLID) {
                    break;
                }
                height--;
            }
            return height;
        }

        private void ClearNonSolidBlocks(BlockPosition point, int firstY, int lastY) {
            for (int y = firstY; y <= lastY; y++) {
                int id = _bm.GetID(point.X, y, point.Z);
                if (id != BlockType.AIR &&
                        BlockInfo.BlockTable[id].State == BlockState.NONSOLID) {
                    _bm.SetBlock(point.X, y, point.Z, AcquaticBlocks.Air);
                }
            }
        }

    }
}
