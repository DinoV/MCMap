using Substrate;
using System.Linq;
using static MinecraftMapper.OsmReader;

namespace MinecraftMapper {
    partial class Mapper {
        private static readonly string[][] ClothingArmorSets = {
            new[] { "minecraft:leather_helmet", "minecraft:leather_chestplate", "minecraft:leather_leggings", "minecraft:leather_boots" },
            new[] { "minecraft:chainmail_helmet", "minecraft:chainmail_chestplate", "minecraft:chainmail_leggings", "minecraft:chainmail_boots" },
            new[] { "minecraft:iron_helmet", "minecraft:iron_chestplate", "minecraft:iron_leggings", "minecraft:iron_boots" },
            new[] { "minecraft:golden_helmet", "minecraft:golden_chestplate", "minecraft:golden_leggings", "minecraft:golden_boots" },
            new[] { "minecraft:diamond_helmet", "minecraft:diamond_chestplate", "minecraft:diamond_leggings", "minecraft:diamond_boots" },
            new[] { "minecraft:netherite_helmet", "minecraft:netherite_chestplate", "minecraft:netherite_leggings", "minecraft:netherite_boots" },
            new[] { "minecraft:turtle_helmet", "minecraft:leather_chestplate", "minecraft:leather_leggings", "minecraft:leather_boots" },
        };

        private static readonly ShopItem[] ClothingContainerItems = {
            new ShopItem("minecraft:wolf_armor", 1),
            new ShopItem("minecraft:leather_horse_armor", 1),
            new ShopItem("minecraft:iron_horse_armor", 1),
            new ShopItem("minecraft:golden_horse_armor", 1),
            new ShopItem("minecraft:diamond_horse_armor", 1),
        };

        private static ShopItem[] DrawClothingStoreDisplay(Mapper mapper, ShopPlacement shop,
                BlockPosition point, int height, BlockFacing facing, int offset, int lineX, int lineZ,
                ShopItem[] sectionItems) {
            if (sectionItems.Any(item => item.Id.Contains("horse_armor") || item.Id == "minecraft:wolf_armor")) {
                mapper.RemoveClothingArmorStand(shop, point, height);
                return sectionItems;
            }
            if (ShouldUseArmorStand(shop, point)) {
                mapper.PlaceClothingArmorStand(shop, point, height, facing, offset, lineX, lineZ,
                    ArmorSetForSection(sectionItems, offset));
                return null;
            }
            mapper.RemoveClothingArmorStand(shop, point, height);
            return sectionItems;
        }

        private static string[] ArmorSetForSection(ShopItem[] items, int offset) {
            string id = items[0].Id;
            for (int i = 0; i < ClothingArmorSets.Length; i++) {
                if (ClothingArmorSets[i].Contains(id)) return ClothingArmorSets[i];
            }
            return ClothingArmorSets[offset % ClothingArmorSets.Length];
        }

        private static ShopDefinition CreateClothingStoreDefinition() {
            return new ShopDefinition(
                Shop.Clothes,
                "Clothing Store",
                "minecraft:leatherworker",
                new[] {
                    new ShopItemGroup("Leather Armor",
                    new ShopItem("minecraft:leather_helmet", 1),
                    new ShopItem("minecraft:leather_chestplate", 1),
                    new ShopItem("minecraft:leather_leggings", 1),
                    new ShopItem("minecraft:leather_boots", 1)),
                    new ShopItemGroup("Chainmail Armor",
                    new ShopItem("minecraft:chainmail_helmet", 1),
                    new ShopItem("minecraft:chainmail_chestplate", 1),
                    new ShopItem("minecraft:chainmail_leggings", 1),
                    new ShopItem("minecraft:chainmail_boots", 1)),
                    new ShopItemGroup("Iron Armor",
                    new ShopItem("minecraft:iron_helmet", 1),
                    new ShopItem("minecraft:iron_chestplate", 1),
                    new ShopItem("minecraft:iron_leggings", 1),
                    new ShopItem("minecraft:iron_boots", 1)),
                    new ShopItemGroup("Golden Armor",
                    new ShopItem("minecraft:golden_helmet", 1),
                    new ShopItem("minecraft:golden_chestplate", 1),
                    new ShopItem("minecraft:golden_leggings", 1),
                    new ShopItem("minecraft:golden_boots", 1)),
                    new ShopItemGroup("Diamond Armor",
                    new ShopItem("minecraft:diamond_helmet", 1),
                    new ShopItem("minecraft:diamond_chestplate", 1),
                    new ShopItem("minecraft:diamond_leggings", 1),
                    new ShopItem("minecraft:diamond_boots", 1)),
                    new ShopItemGroup("Netherite Armor",
                    new ShopItem("minecraft:netherite_helmet", 1),
                    new ShopItem("minecraft:netherite_chestplate", 1),
                    new ShopItem("minecraft:netherite_leggings", 1),
                    new ShopItem("minecraft:netherite_boots", 1)),
                    new ShopItemGroup("Specialty Armor",
                    new ShopItem("minecraft:turtle_helmet", 1),
                    new ShopItem("minecraft:wolf_armor", 1)),
                    new ShopItemGroup("Horse Armor",
                    new ShopItem("minecraft:leather_horse_armor", 1),
                    new ShopItem("minecraft:iron_horse_armor", 1),
                    new ShopItem("minecraft:golden_horse_armor", 1),
                    new ShopItem("minecraft:diamond_horse_armor", 1)),
                },
                displayOverride: DrawClothingStoreDisplay);
        }
    }
}
