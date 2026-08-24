using static MinecraftMapper.OsmReader;

namespace MinecraftMapper {
    partial class Mapper {
        private static ShopDefinition CreateCopyShopDefinition() {
            return new ShopDefinition(
                Shop.CopyShop,
                "Copy Shop",
                "minecraft:librarian",
                new[] {
                    new ShopItemGroup("Paper",
                        new ShopItem("minecraft:paper", 64, fillContainer: true)),
                });
        }
    }
}
