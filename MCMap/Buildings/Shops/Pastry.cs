using static MinecraftMapper.OsmReader;

namespace MinecraftMapper {
    partial class Mapper {
        private static ShopDefinition CreatePastryShopDefinition() {
            return new ShopDefinition(
                Shop.Pastry,
                "Pastry Shop",
                "minecraft:farmer",
                new[] {
                    new ShopItemGroup("Sweets",
                        new ShopItem("minecraft:cookie"),
                        new ShopItem("minecraft:cake", 1)),
                });
        }
    }
}
