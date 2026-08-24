using static MinecraftMapper.OsmReader;

namespace MinecraftMapper {
    partial class Mapper {
        private static ShopDefinition CreateButcherDefinition() {
            return new ShopDefinition(
                Shop.Butcher,
                "Butcher Shop",
                "minecraft:butcher",
                new[] {
                    new ShopItemGroup("Raw Meats",
                        new ShopItem("minecraft:rabbit"),
                        new ShopItem("minecraft:mutton"),
                        new ShopItem("minecraft:porkchop"),
                        new ShopItem("minecraft:beef"),
                        new ShopItem("minecraft:chicken")),
                });
        }
    }
}
