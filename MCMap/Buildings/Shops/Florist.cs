using Substrate;
using static MinecraftMapper.OsmReader;

namespace MinecraftMapper {
    partial class Mapper {
        private static readonly ShopItem[] FloristBinItems = {
            new ShopItem("minecraft:torchflower_seeds", 64, fillContainer: true),
        };

        private static ShopDefinition CreateFloristDefinition() {
            return new ShopDefinition(
                Shop.Florist,
                "Florist",
                "minecraft:farmer",
                new[] {
                    new ShopItemGroup("Potted Flowers",
                        new ShopItem(AcquaticBlocks.Dandelion, 1),
                        new ShopItem(AcquaticBlocks.Poppy, 1),
                        new ShopItem(AcquaticBlocks.BlueOrchid, 1),
                        new ShopItem(AcquaticBlocks.Allium, 1),
                        new ShopItem(AcquaticBlocks.AzureBluet, 1),
                        new ShopItem(AcquaticBlocks.RedTulip, 1),
                        new ShopItem(AcquaticBlocks.OrangeTulip, 1),
                        new ShopItem(AcquaticBlocks.WhiteTulip, 1),
                        new ShopItem(AcquaticBlocks.PinkTulip, 1),
                        new ShopItem(AcquaticBlocks.OxeyeDaisy, 1),
                        new ShopItem(AcquaticBlocks.Cornflower, 1),
                        new ShopItem(AcquaticBlocks.LilyOfTheValley, 1),
                        new ShopItem(AcquaticBlocks.Torchflower, 1)),
                    new ShopItemGroup("Tall Flowers",
                        new ShopItem(AcquaticBlocks.Sunflower, 1),
                        new ShopItem(AcquaticBlocks.Lilac, 1),
                        new ShopItem(AcquaticBlocks.RoseBush, 1),
                        new ShopItem(AcquaticBlocks.Peony, 1),
                        new ShopItem(AcquaticBlocks.PitcherPlant, 1)),
                },
                displayOverride: DrawFloristDisplay);
        }

        private static ShopItem[] DrawFloristDisplay(Mapper mapper, ShopPlacement shop,
                BlockPosition point, int height, BlockFacing facing, int offset, int lineX, int lineZ,
                ShopItem[] sectionItems, bool againstWall) {
            if (againstWall && ShouldUseFloristBin(shop, point)) return FloristBinItems;

            ShopItem flower = sectionItems[offset % sectionItems.Length];
            string potted = PottedFlower(flower.Id);
            if (potted != null) {
                if (!againstWall) {
                    mapper._bm.SetBlock(point.X, height - 1, point.Z, AcquaticBlocks.OakPlanks);
                }
                mapper._bm.SetBlock(point.X, height, point.Z, potted);
                return null;
            }

            int dirtHeight = againstWall ? height : height - 1;
            mapper._bm.SetBlock(point.X, dirtHeight, point.Z, AcquaticBlocks.Dirt);
            mapper._bm.SetBlock(point.X, dirtHeight + 1, point.Z, flower.Id,
                half: BlockHalf.Lower);
            mapper._bm.SetBlock(point.X, dirtHeight + 2, point.Z, flower.Id,
                half: BlockHalf.Upper);
            return null;
        }

        private static string PottedFlower(string flower) {
            switch (flower) {
                case AcquaticBlocks.Dandelion: return AcquaticBlocks.PottedDandelion;
                case AcquaticBlocks.Poppy: return AcquaticBlocks.PottedPoppy;
                case AcquaticBlocks.BlueOrchid: return AcquaticBlocks.PottedBlueOrchid;
                case AcquaticBlocks.Allium: return AcquaticBlocks.PottedAllium;
                case AcquaticBlocks.AzureBluet: return AcquaticBlocks.PottedAzureBluet;
                case AcquaticBlocks.RedTulip: return AcquaticBlocks.PottedRedTulip;
                case AcquaticBlocks.OrangeTulip: return AcquaticBlocks.PottedOrangeTulip;
                case AcquaticBlocks.WhiteTulip: return AcquaticBlocks.PottedWhiteTulip;
                case AcquaticBlocks.PinkTulip: return AcquaticBlocks.PottedPinkTulip;
                case AcquaticBlocks.OxeyeDaisy: return AcquaticBlocks.PottedOxeyeDaisy;
                case AcquaticBlocks.Cornflower: return AcquaticBlocks.PottedCornflower;
                case AcquaticBlocks.LilyOfTheValley: return AcquaticBlocks.PottedLilyOfTheValley;
                case AcquaticBlocks.Torchflower: return AcquaticBlocks.PottedTorchflower;
                default: return null;
            }
        }

        private static bool ShouldUseFloristBin(ShopPlacement shop, BlockPosition point) {
            uint value = unchecked((uint)(shop.Id ^ (shop.Id >> 32) ^
                ((long)point.X * 811) ^ ((long)point.Z * 277)));
            return value % 10 == 0;
        }
    }
}
