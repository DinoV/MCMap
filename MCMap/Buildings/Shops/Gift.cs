using static MinecraftMapper.OsmReader;

namespace MinecraftMapper {
    partial class Mapper {
        private static ShopDefinition CreateGiftStoreDefinition() {
            return new ShopDefinition(
                Shop.Gift,
                "Gift Store",
                "minecraft:librarian",
                new[] {
                    new ShopItemGroup("Valuables",
                    new ShopItem("minecraft:gold_ingot"),
                    new ShopItem("minecraft:diamond")),
                    new ShopItemGroup("Craft Gifts",
                    new ShopItem("minecraft:paper"),
                    new ShopItem("minecraft:leather")),
                });
        }
    }
}
