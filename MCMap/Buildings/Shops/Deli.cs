using static MinecraftMapper.OsmReader;

namespace MinecraftMapper {
    partial class Mapper {
        private static ShopDefinition CreateDeliDefinition() {
            return new ShopDefinition(
                Shop.Deli,
                "Deli",
                "minecraft:butcher",
                new[] {
                    new ShopItemGroup("Cooked Meats",
                        new ShopItem("minecraft:cooked_beef"),
                        new ShopItem("minecraft:cooked_porkchop"),
                        new ShopItem("minecraft:cooked_chicken"),
                        new ShopItem("minecraft:cooked_mutton"),
                        new ShopItem("minecraft:cooked_rabbit")),
                });
        }
    }
}
