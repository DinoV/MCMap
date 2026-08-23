using static MinecraftMapper.OsmReader;

namespace MinecraftMapper {
    partial class Mapper {
        private static ShopDefinition CreateSupermarketDefinition() {
            return new ShopDefinition(
                Shop.Supermarket,
                "Supermarket",
                "minecraft:farmer",
                new[] {
                    new ShopItemGroup("Fresh Fish",
                    new ShopItem("minecraft:cod"),
                    new ShopItem("minecraft:salmon"),
                    new ShopItem("minecraft:tropical_fish"),
                    new ShopItem("minecraft:pufferfish")),
                    new ShopItemGroup("Produce",
                    new ShopItem("minecraft:pumpkin"),
                    new ShopItem("minecraft:sweet_berries"),
                    new ShopItem("minecraft:glow_berries"),
                    new ShopItem("minecraft:carrot")),
                    new ShopItemGroup("Pantry Staples",
                    new ShopItem("minecraft:egg", 16),
                    new ShopItem("minecraft:kelp"),
                    new ShopItem("minecraft:dried_kelp"),
                    new ShopItem("minecraft:bread")),
                });
        }
    }
}
