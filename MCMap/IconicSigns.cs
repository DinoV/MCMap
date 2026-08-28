using Substrate;
using Substrate.TileEntities;
using System;

namespace MinecraftMapper {
    partial class Mapper {
        private const double KensMarketAnchorLatitude = 47.6818771;
        private const double KensMarketAnchorLongitude = -122.3554662;
        private const BannerColor KensMarketLetterColor = BannerColor.Black;

        private static readonly BannerPattern[][] KensMarketLetters = {
            new[] {
                new BannerPattern(KensMarketLetterColor, "minecraft:stripe_left"),
                new BannerPattern(KensMarketLetterColor, "minecraft:stripe_downright"),
                new BannerPattern(KensMarketLetterColor, "minecraft:stripe_downleft"),
            },
            new[] {
                new BannerPattern(KensMarketLetterColor, "minecraft:stripe_left"),
                new BannerPattern(KensMarketLetterColor, "minecraft:stripe_top"),
                new BannerPattern(KensMarketLetterColor, "minecraft:stripe_middle"),
                new BannerPattern(KensMarketLetterColor, "minecraft:stripe_bottom"),
            },
            new[] {
                new BannerPattern(KensMarketLetterColor, "minecraft:stripe_left"),
                new BannerPattern(KensMarketLetterColor, "minecraft:stripe_downright"),
                new BannerPattern(KensMarketLetterColor, "minecraft:stripe_right"),
            },
            new[] {
                new BannerPattern(KensMarketLetterColor, "minecraft:square_top_right"),
            },
            new[] {
                new BannerPattern(KensMarketLetterColor, "minecraft:stripe_left"),
                new BannerPattern(BannerColor.White, "minecraft:square_bottom_left"),
                new BannerPattern(KensMarketLetterColor, "minecraft:stripe_right"),
                new BannerPattern(BannerColor.White, "minecraft:square_top_right"),
                new BannerPattern(KensMarketLetterColor, "minecraft:stripe_top"),
                new BannerPattern(KensMarketLetterColor, "minecraft:stripe_middle"),
                new BannerPattern(KensMarketLetterColor, "minecraft:stripe_bottom"),
            },
        };

        private void DrawIconicSigns() {
            DrawKensMarketSign();
        }

        private void DrawKensMarketSign() {
            BlockPosition anchor = _conv.ToBlock(
                KensMarketAnchorLatitude, KensMarketAnchorLongitude);
            if (!_conv.IsValidPoint(anchor)) {
                return;
            }

            // The landmark sits northwest of the building corner represented by
            // OSM node 8211371052. Keep this offset in block space so the sign
            // follows the same latitude/longitude anchor on regenerated maps.
            var leftPost = new BlockPosition(anchor.X - 4, anchor.Z - 2);
            var rightPost = new BlockPosition(leftPost.X + 1, leftPost.Z);
            int leftGround = GetPointFeatureGroundHeight(leftPost);
            int rightGround = GetPointFeatureGroundHeight(rightPost);
            int baseY = Math.Max(leftGround, rightGround) + 1;

            DrawKensMarketPost(leftPost, leftGround + 1, baseY + 3, true);
            DrawKensMarketPost(rightPost, rightGround + 1, baseY + 3, false);

            int beamStartX = leftPost.X - 1;
            int beamEndX = beamStartX + KensMarketLetters.Length - 1;
            for (int y = baseY + 4; y <= baseY + 5; y++) {
                for (int x = beamStartX; x <= beamEndX; x++) {
                    _bm.SetBlock(
                        x, y, leftPost.Z, AcquaticBlocks.WarpedFence,
                        north: false,
                        east: x < beamEndX,
                        south: false,
                        west: x > beamStartX);
                }
            }

            int bannerY = baseY + 5;
            for (int index = 0; index < KensMarketLetters.Length; index++) {
                int x = beamStartX + index;
                DrawKensMarketBanner(
                    x, bannerY, leftPost.Z + 1, BlockFacing.South,
                    KensMarketLetters[index]);
                DrawKensMarketBanner(
                    x, bannerY, leftPost.Z - 1, BlockFacing.North,
                    KensMarketLetters[KensMarketLetters.Length - 1 - index]);
            }

            WriteLine("Ken's Market sign at {0},{1},{2}",
                leftPost.X, baseY, leftPost.Z);
        }

        private void DrawKensMarketPost(
                BlockPosition post, int bottomY, int topY, bool connectsEast) {
            for (int y = bottomY; y <= topY; y++) {
                _bm.SetBlock(
                    post.X, y, post.Z, AcquaticBlocks.WarpedFence,
                    north: false,
                    east: connectsEast,
                    south: false,
                    west: !connectsEast);
            }
        }

        private void DrawKensMarketBanner(
                int x,
                int y,
                int z,
                BlockFacing facing,
                BannerPattern[] patterns) {
            var banner = new TileEntityBanner {
                BaseColor = BannerColor.White,
                CustomName = "Ken's Market",
                Patterns = patterns,
            };
            _bm.SetBlock(
                x, y, z, AcquaticBlocks.WhiteWallBanner,
                facing: facing);
            _bm.SetTileEntity(x, y, z, banner);
        }
    }
}
