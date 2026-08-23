using static MinecraftMapper.OsmReader;

namespace MinecraftMapper {
    partial class Mapper {
        private static ShopDefinition CreateBakeryDefinition() {
            return new ShopDefinition(
                Shop.Bakery,
                "Bakery",
                "minecraft:farmer",
                new[] { new ShopItemGroup("Baked Goods",
                    new ShopItem("minecraft:bread"),
                    new ShopItem("minecraft:cookie")
                ) });
        }
    }
}
