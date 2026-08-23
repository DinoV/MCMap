using Substrate;
using static MinecraftMapper.OsmReader;

namespace MinecraftMapper {
    partial class Mapper {
        private static ShopDefinition CreateBookStoreDefinition() {
            return new ShopDefinition(
                Shop.Books,
                "Book Store",
                "minecraft:librarian",
                new[] {
                    new ShopItemGroup("Paper",
                        new ShopItem("minecraft:paper", 64, fillContainer: true)),
                },
                displayOverride: DrawBookStoreDisplay);
        }

        private static ShopItem[] DrawBookStoreDisplay(Mapper mapper, ShopPlacement shop,
                BlockPosition point, int height, BlockFacing facing, int offset, int lineX, int lineZ,
                ShopItem[] sectionItems, bool againstWall) {
            if (againstWall && ShouldUseBookStoreBin(shop, point)) return sectionItems;

            mapper._bm.SetBlock(point.X, height - (againstWall ? 0 : 1), point.Z,
                AcquaticBlocks.Bookshelf);
            mapper._bm.SetBlock(point.X, height + (againstWall ? 1 : 0), point.Z,
                AcquaticBlocks.Bookshelf);
            return null;
        }

        private static bool ShouldUseBookStoreBin(ShopPlacement shop, BlockPosition point) {
            uint value = unchecked((uint)(shop.Id ^ (shop.Id >> 32) ^
                ((long)point.X * 719) ^ ((long)point.Z * 233)));
            return value % 5 == 0;
        }
    }
}
