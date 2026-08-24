using static MinecraftMapper.OsmReader;

namespace MinecraftMapper {
    partial class Mapper {
        private static ShopDefinition CreateStationeryStoreDefinition() {
            return new ShopDefinition(
                Shop.Stationary,
                "Stationery Store",
                "minecraft:librarian",
                new[] {
                    new ShopItemGroup("Paper",
                        new ShopItem("minecraft:paper", 64, fillContainer: true)),
                });
        }
    }
}
