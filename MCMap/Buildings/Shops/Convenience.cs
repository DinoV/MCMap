using System.Collections.Generic;
using static MinecraftMapper.OsmReader;

namespace MinecraftMapper {
    partial class Mapper {
        private static ShopDefinition CreateConvenienceStoreDefinition() {
            return new ShopDefinition(
                Shop.Convenience,
                "Convenience Store",
                "minecraft:farmer",
                new[] {
                    new ShopItemGroup("Cooked Meats",
                    new ShopItem("minecraft:cooked_beef"),
                    new ShopItem("minecraft:cooked_porkchop"),
                    new ShopItem("minecraft:cooked_chicken"),
                    new ShopItem("minecraft:cooked_mutton"),
                    new ShopItem("minecraft:cooked_rabbit")),
                    new ShopItemGroup("Breakfast",
                    new ShopItem("minecraft:egg", 16),
                    new ShopItem("minecraft:bread"),
                    new ShopItem("minecraft:milk_bucket", 1),
                    new ShopItem("minecraft:apple")),
                    new ShopItemGroup("Prepared Stews",
                    new ShopItem("minecraft:mushroom_stew", 1),
                    new ShopItem("minecraft:rabbit_stew", 1),
                    new ShopItem("minecraft:beetroot_soup", 1)),
                    new ShopItemGroup("Sweets",
                    new ShopItem("minecraft:cookie"),
                    new ShopItem("minecraft:pumpkin_pie"),
                    new ShopItem("minecraft:cake"),
                    new ShopItem("minecraft:sugar")),
                });
        }
    }
}
