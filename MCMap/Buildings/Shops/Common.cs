using Substrate;
using Substrate.Nbt;
using Substrate.TileEntities;
using System;
using System.Collections.Generic;
using System.Linq;
using static MinecraftMapper.OsmReader;

namespace MinecraftMapper {
    partial class Mapper {
        private delegate ShopItem[] ShopDisplayOverride(Mapper mapper, ShopPlacement shop,
            BlockPosition point, int height, BlockFacing facing, int offset, int lineX, int lineZ,
            ShopItem[] sectionItems, bool againstWall);

        private sealed class ShopDefinition {
            public readonly Shop Type;
            public readonly string DisplayName;
            public readonly string VillagerProfession;
            public readonly ShopItemGroup[] ItemGroups;
            public readonly ShopDisplayOverride DisplayOverride;

            public ShopDefinition(Shop type, string displayName, string villagerProfession,
                    ShopItemGroup[] itemGroups, ShopDisplayOverride displayOverride = null) {
                Type = type;
                DisplayName = displayName;
                VillagerProfession = villagerProfession;
                ItemGroups = itemGroups;
                DisplayOverride = displayOverride;
            }
        }

        private sealed class ShopItemGroup {
            public readonly string Description;
            public readonly ShopItem[] Items;

            public ShopItemGroup(string description, params ShopItem[] items) {
                Description = description;
                Items = items;
            }
        }

        private sealed class ShopSection {
            public readonly string Description;
            public readonly ShopItem[] Items;

            public ShopSection(string description, ShopItem[] items) {
                Description = description;
                Items = items;
            }
        }

        private sealed class ShopItem {
            public readonly string Id;
            public readonly int Count;
            public readonly bool FillContainer;

            public ShopItem(string id, int count = 8, bool fillContainer = false) {
                Id = id;
                Count = count;
                FillContainer = fillContainer;
            }
        }

        private sealed class ShopPlacement {
            public long Id;
            public string Name;
            public ShopDefinition Definition;
            public BlockPosition? Anchor;
            public HashSet<BlockPosition> FloorPoints = new HashSet<BlockPosition>();
        }

        private sealed class ShopDisplayPlan {
            public readonly Random Random;
            public readonly int ContainerType;
            public readonly string ShulkerBox;
            private readonly ShopSection[] _sections;
            private int _nextSection;

            public ShopDisplayPlan(long id, ShopDefinition definition) {
                Random = new Random(unchecked((int)(id ^ (id >> 32))));
                ContainerType = Random.Next(3);
                ShulkerBox = ShopShulkerBoxes[Random.Next(ShopShulkerBoxes.Length)];
                _sections = definition.ItemGroups
                    .SelectMany(group => group.Items
                        .Select((item, index) => new { item, index })
                        .GroupBy(value => value.index / 2)
                        .Select(section => new ShopSection(group.Description,
                            section.Select(value => value.item).ToArray())))
                    .OrderBy(section => Random.Next())
                    .ToArray();
            }

            public ShopSection NextSection() {
                if (_sections.Length == 0) return null;
                return _sections[_nextSection++ % _sections.Length];
            }
        }

        private static readonly string[] ShopShulkerBoxes = {
            AcquaticBlocks.WhiteShulkerBox, AcquaticBlocks.OrangeShulkerBox,
            AcquaticBlocks.MagentaShulkerBox, AcquaticBlocks.LightBlueShulkerBox,
            AcquaticBlocks.YellowShulkerBox, AcquaticBlocks.LimeShulkerBox,
            AcquaticBlocks.PinkShulkerBox, AcquaticBlocks.GrayShulkerBox,
            AcquaticBlocks.LightGrayShulkerBox, AcquaticBlocks.CyanShulkerBox,
            AcquaticBlocks.PurpleShulkerBox, AcquaticBlocks.BlueShulkerBox,
            AcquaticBlocks.BrownShulkerBox, AcquaticBlocks.GreenShulkerBox,
            AcquaticBlocks.RedShulkerBox, AcquaticBlocks.BlackShulkerBox,
        };

        private static readonly Dictionary<Shop, ShopDefinition> ShopDefinitions =
            new[] {
                CreateConvenienceStoreDefinition(), CreateClothingStoreDefinition(),
                CreateArtStoreDefinition(), CreateBakeryDefinition(),
                CreateSupermarketDefinition(), CreateGiftStoreDefinition(),
                CreateBookStoreDefinition(),
                CreateFloristDefinition(),
                CreateDeliDefinition(), CreateCopyShopDefinition(),
                CreatePastryShopDefinition(), CreateButcherDefinition(),
                CreateStationeryStoreDefinition(),
                CreatePetStoreDefinition(),
            }
                .ToDictionary(definition => definition.Type);

        private void DrawBuildingShops(Building building, HashSet<BlockPosition> floorPoints,
                int floorHeight, HashSet<BlockPosition> doors) {
            if (floorPoints == null || floorPoints.Count < 2) {
                return;
            }

            var shops = FindBuildingShops(building);
            if (shops.Count == 0) {
                return;
            }

            if (shops.Count == 1 && shops[0].Anchor == null) {
                shops[0].FloorPoints.UnionWith(floorPoints);
            } else {
                foreach (var point in floorPoints) {
                    var nearest = shops
                        .Where(shop => shop.Anchor != null)
                        .OrderBy(shop => DistanceSquared(point, shop.Anchor.Value))
                        .FirstOrDefault();
                    if (nearest != null) {
                        nearest.FloorPoints.Add(point);
                    }
                }
            }

            foreach (var shop in shops.Where(candidate => candidate.FloorPoints.Count >= 2)) {
                DrawShop(shop, floorHeight, doors ?? new HashSet<BlockPosition>());
            }
        }

        private List<ShopPlacement> FindBuildingShops(Building building) {
            ShopDefinition definition;
            if (building.Shop != Shop.None && ShopDefinitions.TryGetValue(building.Shop, out definition)) {
                return new List<ShopPlacement> {
                    new ShopPlacement {
                        Id = building.Id,
                        Name = building.Name,
                        Definition = definition,
                    }
                };
            }

            return (building.SignItems ?? new List<Node>())
                .OfType<ShopNode>()
                .Where(node => ShopDefinitions.ContainsKey(node.Shop))
                .GroupBy(node => node.Id)
                .Select(group => group.First())
                .Select(node => new ShopPlacement {
                    Id = node.Id,
                    Name = node.Name,
                    Definition = ShopDefinitions[node.Shop],
                    Anchor = _conv.ToBlock(node.Lat, node.Long),
                })
                .ToList();
        }

        private void DrawShop(ShopPlacement shop, int floorHeight, HashSet<BlockPosition> doors) {
            int width = shop.FloorPoints.Max(point => point.X) - shop.FloorPoints.Min(point => point.X) + 1;
            int depth = shop.FloorPoints.Max(point => point.Z) - shop.FloorPoints.Min(point => point.Z) + 1;
            var occupied = new HashSet<BlockPosition>();
            var plan = new ShopDisplayPlan(shop.Id, shop.Definition);
            Console.WriteLine("Shop {0} {1} at {2} {3} ({4})", shop.Name, shop.Definition.Type, shop.FloorPoints.FirstOrDefault(), shop.FloorPoints.Count, shop.Id);
            if (shop.Definition.Type == Shop.Pet) {
                DrawPetStore(shop, floorHeight, doors, occupied);
                DrawShopCheckoutAndKeeper(shop, floorHeight, doors, occupied);
                return;
            }
            if (shop.FloorPoints.Count >= 64 && width >= 7 && depth >= 7) {
                DrawShopAisles(shop, floorHeight, doors, occupied, plan, width >= depth);
            } else {
                DrawShopEdgeDisplays(shop, floorHeight, doors, occupied, plan);
            }

            DrawShopCheckoutAndKeeper(shop, floorHeight, doors, occupied);
        }

        private void DrawShopAisles(ShopPlacement shop, int floorHeight, HashSet<BlockPosition> doors,
                HashSet<BlockPosition> occupied, ShopDisplayPlan plan, bool aislesRunEastWest) {
            int minX = shop.FloorPoints.Min(point => point.X);
            int maxX = shop.FloorPoints.Max(point => point.X);
            int minZ = shop.FloorPoints.Min(point => point.Z);
            int maxZ = shop.FloorPoints.Max(point => point.Z);
            int aisleSpacing = UsesWideShopRows(shop.Definition.Type) ? 4 : 3;

            if (aislesRunEastWest) {
                for (int z = minZ + 3; z <= maxZ - 3; z += aisleSpacing) {
                    DrawShopDisplayLine(shop, floorHeight, doors, occupied, plan,
                        new BlockPosition(minX + 2, z), new BlockPosition(maxX - 2, z), false);
                }
            } else {
                for (int x = minX + 3; x <= maxX - 3; x += aisleSpacing) {
                    DrawShopDisplayLine(shop, floorHeight, doors, occupied, plan,
                        new BlockPosition(x, minZ + 2), new BlockPosition(x, maxZ - 2), false);
                }
            }

            DrawShopEdgeDisplays(shop, floorHeight, doors, occupied, plan);
        }

        private void DrawShopEdgeDisplays(ShopPlacement shop, int floorHeight, HashSet<BlockPosition> doors,
                HashSet<BlockPosition> occupied, ShopDisplayPlan plan) {
            int minX = shop.FloorPoints.Min(point => point.X), maxX = shop.FloorPoints.Max(point => point.X);
            int minZ = shop.FloorPoints.Min(point => point.Z), maxZ = shop.FloorPoints.Max(point => point.Z);
            var lines = new[] {
                new[] { new BlockPosition(minX, minZ), new BlockPosition(maxX, minZ) },
                new[] { new BlockPosition(maxX, minZ), new BlockPosition(maxX, maxZ) },
                new[] { new BlockPosition(maxX, maxZ), new BlockPosition(minX, maxZ) },
                new[] { new BlockPosition(minX, maxZ), new BlockPosition(minX, minZ) },
            };
            int rotation = plan.Random.Next(lines.Length);
            for (int i = 0; i < lines.Length; i++) {
                var line = lines[(i + rotation) % lines.Length];
                DrawShopDisplayLine(shop, floorHeight, doors, occupied, plan, line[0], line[1], true);
            }
        }

        private void DrawShopDisplayLine(ShopPlacement shop, int floorHeight, HashSet<BlockPosition> doors,
                HashSet<BlockPosition> occupied, ShopDisplayPlan plan, BlockPosition start, BlockPosition end,
                bool againstWall) {
            int dx = Math.Sign(end.X - start.X), dz = Math.Sign(end.Z - start.Z);
            int length = Math.Max(Math.Abs(end.X - start.X), Math.Abs(end.Z - start.Z)) + 1;
            int index = plan.Random.Next(0, Math.Min(3, Math.Max(1, length)));
            while (length - index >= 1) {
                ShopSection section = plan.NextSection();
                if (section == null) return;
                int required = UsesWideShopRows(shop.Definition.Type)
                    ? Math.Min(plan.Random.Next(3, 6), length - index)
                    : section.Items.Length;
                if (length - index < required) break;
                var displayed = new List<BlockPosition>();
                var containers = new List<BlockPosition>();
                for (int i = 0; i < required; i++) {
                    var point = new BlockPosition(start.X + (index + i) * dx, start.Z + (index + i) * dz);
                    bool renderedContainer;
                    if (TryDrawShopDisplay(shop, point, floorHeight, doors, occupied, plan,
                            dx, dz, new[] { section.Items[i % section.Items.Length] },
                            againstWall ? floorHeight + 1 : floorHeight + 2,
                            againstWall, out renderedContainer)) {
                        displayed.Add(point);
                        if (renderedContainer) containers.Add(point);
                    }
                }
                if (displayed.Count > 0) {
                    if (shop.Definition.Type == Shop.Art) {
                        DrawArtShopSection(shop, displayed, floorHeight, dx, dz, againstWall, plan.Random);
                    } else if (shop.Definition.Type == Shop.Books) {
                        if (againstWall && containers.Count > 0) {
                            DrawWallShopSection(shop, section, containers, floorHeight, dx, dz);
                        }
                    } else if (shop.Definition.Type == Shop.Florist) {
                        if (againstWall && containers.Count > 0) {
                            DrawWallShopSection(shop, section, containers, floorHeight, dx, dz);
                        }
                    } else if (againstWall) {
                        if (containers.Count > 0) {
                            DrawWallShopSection(shop, section, containers, floorHeight, dx, dz);
                        }
                    } else {
                        DrawInteriorShopSection(shop, section, displayed, floorHeight, dx, dz);
                    }
                }
                index += required + plan.Random.Next(2, 4);
            }
        }

        private bool TryDrawShopDisplay(ShopPlacement shop, BlockPosition point, int floorHeight,
                HashSet<BlockPosition> doors, HashSet<BlockPosition> occupied, ShopDisplayPlan plan,
                int lineX, int lineZ, ShopItem[] sectionItems, int displayHeight, bool againstWall,
                out bool renderedContainer) {
            renderedContainer = false;
            if (!shop.FloorPoints.Contains(point) || occupied.Contains(point) ||
                    IsShopDoorApproach(point, doors) ||
                    _bm.GetStringID(point.X, displayHeight, point.Z) != AcquaticBlocks.Air) {
                return false;
            }

            var facing = FacingIntoShop(shop.FloorPoints, point);
            int offset = Math.Abs(unchecked(point.X * 397 ^ point.Z * 101));
            ShopItem[] displayInventory = sectionItems;
            if (shop.Definition.DisplayOverride != null) {
                displayInventory = shop.Definition.DisplayOverride(
                    this, shop, point, displayHeight, facing, offset, lineX, lineZ,
                    sectionItems, againstWall);
            }
            if (displayInventory == null) {
                occupied.Add(point);
                return true;
            }

            renderedContainer = true;
            if (plan.ContainerType == 0) {
                _bm.SetBlock(point.X, displayHeight, point.Z, AcquaticBlocks.Chest, facing);
                var chest = new TileEntityChest { UseModernFormat = true };
                FillInventory(chest.Items, displayInventory, offset);
                _bm.SetTileEntity(point.X, displayHeight, point.Z, chest);
            } else {
                string block;
                string entityId;
                TagNodeCompound properties = new TagNodeCompound();
                properties["facing"] = new TagNodeString(facing.ToString().ToLowerInvariant());
                if (plan.ContainerType == 1) {
                    block = AcquaticBlocks.Barrel;
                    entityId = "minecraft:barrel";
                    properties["open"] = new TagNodeString("false");
                } else {
                    block = plan.ShulkerBox;
                    entityId = "minecraft:shulker_box";
                }
                _bm.SetBlock(point.X, displayHeight, point.Z, block, properties);
                _bm.SetTileEntity(point.X, displayHeight, point.Z,
                    CreateModernShopContainer(entityId, displayInventory, offset));
            }
            occupied.Add(point);
            return true;
        }

        private static bool IsShopDoorApproach(BlockPosition point, HashSet<BlockPosition> doors) {
            // Leave enough room to enter and turn after passing through the door.  A two-block
            // exclusion allowed the first container in an aisle to be placed directly in front
            // of the doorway, at a Manhattan distance of three.
            return doors.Any(door => ManhattanDistance(point, door) <= 3);
        }

        private void DrawInteriorShopSection(ShopPlacement shop, ShopSection section,
                List<BlockPosition> displays, int floorHeight,
                int lineX, int lineZ) {
            if (displays.Count == 0) return;
            foreach (BlockPosition point in displays) {
                _bm.SetBlock(point.X, floorHeight + 1, point.Z, AcquaticBlocks.PolishedAndesite);
            }

            BlockFacing firstSide;
            BlockFacing secondSide;
            BlockFacing firstEnd;
            BlockFacing secondEnd;
            if (lineX != 0) {
                firstSide = BlockFacing.North;
                secondSide = BlockFacing.South;
                firstEnd = lineX > 0 ? BlockFacing.West : BlockFacing.East;
                secondEnd = Opposite(firstEnd);
            } else {
                firstSide = BlockFacing.West;
                secondSide = BlockFacing.East;
                firstEnd = lineZ > 0 ? BlockFacing.North : BlockFacing.South;
                secondEnd = Opposite(firstEnd);
            }

            BlockPosition label = displays[displays.Count / 2];
            DrawShopSectionSign(Offset(label, firstSide), floorHeight + 1, firstSide, section.Description);
            DrawShopSectionSign(Offset(label, secondSide), floorHeight + 1, secondSide, section.Description);
            int frameIndex = 0;
            for (int i = 0; i < displays.Count; i++) {
                if (!displays[i].Equals(label)) {
                    DrawShopItemFrame(shop, displays[i], floorHeight + 1, firstSide,
                        section.Items[i % section.Items.Length], frameIndex++);
                    DrawShopItemFrame(shop, displays[i], floorHeight + 1, secondSide,
                        section.Items[i % section.Items.Length], frameIndex++);
                }
            }
            DrawShopItemFrame(shop, displays[0], floorHeight + 1, firstEnd,
                section.Items[0], frameIndex++);
            DrawShopItemFrame(shop, displays[displays.Count - 1], floorHeight + 1, secondEnd,
                section.Items[section.Items.Length - 1], frameIndex);
        }

        private void DrawWallShopSection(ShopPlacement shop, ShopSection section,
                List<BlockPosition> containers, int floorHeight, int lineX, int lineZ) {
            if (containers.Count == 0) {
                return;
            }
            BlockPosition first = containers[0];
            BlockFacing facing = FacingPerpendicularToDisplayLine(
                shop.FloorPoints, first, lineX, lineZ);
            DrawShopSectionSign(first, floorHeight + 2, facing, section.Description);
            for (int i = 0; i < containers.Count; i++) {
                BlockPosition point = containers[i];
                BlockPosition wall = Offset(point, Opposite(facing));
                DrawShopItemFrame(shop, wall, floorHeight + 3, facing,
                    section.Items[i % section.Items.Length], i);
            }
        }

        private void DrawShopSectionSign(BlockPosition point, int height, BlockFacing facing, string description) {
            var block = new AlphaBlock(BlockType.WALL_SIGN);
            var sign = block.GetTileEntity() as TileEntitySign;
            var lines = new List<string>();
            AddSignName(lines, description);
            SetSignName(lines, sign);
            _bm.SetBlock(point.X, height, point.Z, AcquaticBlocks.OakWallSign, facing: facing);
            _bm.SetTileEntity(point.X, height, point.Z, sign);
        }

        private void DrawShopItemFrame(ShopPlacement shop, BlockPosition anchor, int height,
                BlockFacing facing, ShopItem item, int index) {
            long marker = unchecked(shop.Id ^ ((long)anchor.X << 32) ^ (uint)anchor.Z ^
                ((long)height << 16) ^ ((long)facing << 8) ^ index ^ 0x4954454d4652414dL);
            var frame = new TypedEntity("minecraft:item_frame");
            frame.Position = ItemFramePosition(anchor, height, facing);
            frame.Rotation = new Orientation { Yaw = FacingYaw(facing) };
            frame.Source["Facing"] = new TagNodeByte(ItemFrameFacing(facing));
            frame.Source["TileX"] = new TagNodeInt(anchor.X);
            frame.Source["TileY"] = new TagNodeInt(height);
            frame.Source["TileZ"] = new TagNodeInt(anchor.Z);
            frame.Source["Item"] = CreateModernItemStack(item.Id);
            frame.Source["ItemRotation"] = new TagNodeByte(0);
            frame.Source["Fixed"] = new TagNodeByte(1);
            frame.Source["Invulnerable"] = new TagNodeByte(1);
            frame.Source["MCMapShopFrameId"] = new TagNodeLong(marker);
            frame.Source["UUID"] = CreateEntityUuid(marker);
            Action<EntityCollection> update = entities => {
                entities.RemoveAll(entity => {
                    TagNode value;
                    return entity.Source.TryGetValue("MCMapShopFrameId", out value) &&
                        value is TagNodeLong && ((TagNodeLong)value).Data == marker;
                });
                entities.Add(frame);
            };
            if (_modernEntities != null) _modernEntities.Update(anchor.X >> 4, anchor.Z >> 4, update);
            else update(_bm.GetChunk(anchor.X, height, anchor.Z).Entities);
        }

        private static Vector3 ItemFramePosition(BlockPosition anchor, int height, BlockFacing facing) {
            double x = anchor.X + .5, z = anchor.Z + .5;
            if (facing == BlockFacing.North) z = anchor.Z - .03125;
            else if (facing == BlockFacing.South) z = anchor.Z + 1.03125;
            else if (facing == BlockFacing.West) x = anchor.X - .03125;
            else if (facing == BlockFacing.East) x = anchor.X + 1.03125;
            return new Vector3 { X = x, Y = height + .5, Z = z };
        }

        private static byte ItemFrameFacing(BlockFacing facing) {
            switch (facing) {
                case BlockFacing.North: return 2;
                case BlockFacing.South: return 3;
                case BlockFacing.West: return 4;
                case BlockFacing.East: return 5;
                default: return 2;
            }
        }

        private static BlockPosition Offset(BlockPosition point, BlockFacing facing) {
            switch (facing) {
                case BlockFacing.North: return new BlockPosition(point.X, point.Z - 1);
                case BlockFacing.South: return new BlockPosition(point.X, point.Z + 1);
                case BlockFacing.West: return new BlockPosition(point.X - 1, point.Z);
                case BlockFacing.East: return new BlockPosition(point.X + 1, point.Z);
                default: return point;
            }
        }

        private static BlockFacing Opposite(BlockFacing facing) {
            switch (facing) {
                case BlockFacing.North: return BlockFacing.South;
                case BlockFacing.South: return BlockFacing.North;
                case BlockFacing.West: return BlockFacing.East;
                case BlockFacing.East: return BlockFacing.West;
                default: return facing;
            }
        }

        private static void FillInventory(ItemCollection inventory, ShopItem[] items, int offset) {
            int slots = items.Any(item => item.FillContainer) ? 9 : Math.Min(9, items.Length);
            for (int slot = 0; slot < slots; slot++) {
                ShopItem item = items[(slot + offset) % items.Length];
                inventory[slot] = new Item(item.Id) { Count = item.Count };
            }
        }

        private static bool ShouldUseArmorStand(ShopPlacement shop, BlockPosition point) {
            uint value = unchecked((uint)(shop.Id ^ (shop.Id >> 32) ^
                ((long)point.X * 397) ^ ((long)point.Z * 101)));
            return value % 5 != 0;
        }

        private void PlaceClothingArmorStand(ShopPlacement shop, BlockPosition point,
                int height, BlockFacing facing, int offset, int lineX, int lineZ, string[] armor) {
            facing = FacingPerpendicularToDisplayLine(shop.FloorPoints, point, lineX, lineZ);
            long marker = ClothingDisplayMarker(shop, point);
            var stand = new TypedEntity("minecraft:armor_stand");
            stand.Position = new Vector3 { X = point.X + .5, Y = height, Z = point.Z + .5 };
            stand.IsOnGround = true;
            stand.Rotation = new Orientation { Yaw = FacingYaw(facing) };
            stand.Source["Health"] = new TagNodeFloat(20);
            stand.Source["Invulnerable"] = new TagNodeByte(1);
            stand.Source["PersistenceRequired"] = new TagNodeByte(1);
            stand.Source["ShowArms"] = new TagNodeByte(1);
            stand.Source["NoBasePlate"] = new TagNodeByte(0);
            stand.Source["equipment"] = new TagNodeCompound {
                ["feet"] = CreateModernItemStack(armor[3]),
                ["legs"] = CreateModernItemStack(armor[2]),
                ["chest"] = CreateModernItemStack(armor[1]),
                ["head"] = CreateModernItemStack(armor[0]),
            };
            stand.Source["MCMapShopDisplayId"] = new TagNodeLong(marker);
            stand.Source["UUID"] = CreateEntityUuid(marker);
            UpdateClothingArmorStand(point, height, marker, stand);
        }

        private void RemoveClothingArmorStand(ShopPlacement shop, BlockPosition point, int height) {
            UpdateClothingArmorStand(point, height, ClothingDisplayMarker(shop, point), null);
        }

        private void UpdateClothingArmorStand(BlockPosition point, int height, long marker, TypedEntity stand) {
            Action<EntityCollection> update = entities => {
                entities.RemoveAll(entity => {
                    TagNode value;
                    return entity.Source.TryGetValue("MCMapShopDisplayId", out value) &&
                        value is TagNodeLong && ((TagNodeLong)value).Data == marker;
                });
                if (stand != null) entities.Add(stand);
            };
            if (_modernEntities != null) _modernEntities.Update(point.X >> 4, point.Z >> 4, update);
            else update(_bm.GetChunk(point.X, height, point.Z).Entities);
        }

        private static long ClothingDisplayMarker(ShopPlacement shop, BlockPosition point) {
            return unchecked(shop.Id ^ ((long)point.X << 32) ^ (uint)point.Z ^ 0x41524d4f5253544cL);
        }

        private static TagNodeCompound CreateModernItemStack(string id) {
            return new TagNodeCompound {
                ["id"] = new TagNodeString(id), ["count"] = new TagNodeInt(1),
                ["components"] = new TagNodeCompound(),
            };
        }

        private static double FacingYaw(BlockFacing facing) {
            switch (facing) {
                case BlockFacing.South: return 0;
                case BlockFacing.West: return 90;
                case BlockFacing.North: return 180;
                case BlockFacing.East: return -90;
                default: return 0;
            }
        }

        private static TileEntity CreateModernShopContainer(string entityId, ShopItem[] inventory, int offset) {
            var items = new TagNodeList(TagType.TAG_COMPOUND);
            int slots = inventory.Any(item => item.FillContainer) ? 9 : Math.Min(9, inventory.Length);
            for (int slot = 0; slot < slots; slot++) {
                ShopItem item = inventory[(slot + offset) % inventory.Length];
                items.Add(new TagNodeCompound {
                    ["Slot"] = new TagNodeByte((byte)slot), ["id"] = new TagNodeString(item.Id),
                    ["count"] = new TagNodeInt(item.Count), ["components"] = new TagNodeCompound(),
                });
            }
            var entity = new TileEntity(entityId);
            entity.Source["Items"] = items;
            entity.Source["components"] = new TagNodeCompound();
            entity.Source["keepPacked"] = new TagNodeByte(0);
            return entity;
        }

        private static bool UsesWideShopRows(Shop type) {
            return type == Shop.Art || type == Shop.Books;
        }

        private void DrawShopCheckoutAndKeeper(ShopPlacement shop, int floorHeight,
                HashSet<BlockPosition> doors, HashSet<BlockPosition> occupied) {
            double centerX = shop.FloorPoints.Average(point => point.X);
            double centerZ = shop.FloorPoints.Average(point => point.Z);
            var openPoints = shop.FloorPoints
                .Where(point => !occupied.Contains(point) &&
                    doors.All(door => ManhattanDistance(point, door) >= 3) &&
                    _bm.GetStringID(point.X, floorHeight + 1, point.Z) == AcquaticBlocks.Air)
                .OrderBy(point => doors.Count == 0
                    ? Math.Abs(point.X - centerX) + Math.Abs(point.Z - centerZ)
                    : doors.Min(door => ManhattanDistance(point, door)))
                .ToList();
            if (openPoints.Count == 0) {
                return;
            }

            BlockPosition keeper = default(BlockPosition);
            BlockPosition? counter = null;
            foreach (var candidate in openPoints) {
                counter = Neighbors(candidate)
                    .Where(point => shop.FloorPoints.Contains(point) && !occupied.Contains(point) &&
                        doors.All(door => ManhattanDistance(point, door) >= 3) &&
                        _bm.GetStringID(point.X, floorHeight + 1, point.Z) == AcquaticBlocks.Air)
                    .Select(point => (BlockPosition?)point)
                    .FirstOrDefault();
                if (counter != null) {
                    keeper = candidate;
                    break;
                }
            }
            if (counter == null) return;

            _bm.SetBlock(counter.Value.X, floorHeight + 1, counter.Value.Z,
                AcquaticBlocks.OakSlab, BlockSlabType.Top, false);
            PlaceShopkeeper(shop, keeper, floorHeight + 1);
        }

        private void PlaceShopkeeper(ShopPlacement shop, BlockPosition point, int height) {
            long marker = unchecked(shop.Id ^ ((long)shop.Definition.Type << 56));

            var villager = new TypedEntity("minecraft:villager");
            villager.Position = new Vector3 { X = point.X + .5, Y = height, Z = point.Z + .5 };
            villager.IsOnGround = true;
            villager.Air = 300;
            villager.Source["Health"] = new TagNodeFloat(20);
            villager.Source["VillagerData"] = new TagNodeCompound {
                ["profession"] = new TagNodeString(shop.Definition.VillagerProfession),
                ["type"] = new TagNodeString("minecraft:plains"),
                ["level"] = new TagNodeInt(5),
            };
            villager.Source["PersistenceRequired"] = new TagNodeByte(1);
            villager.Source["NoAI"] = new TagNodeByte(1);
            villager.Source["CustomName"] = new TagNodeCompound {
                ["text"] = new TagNodeString(GetShopkeeperGreeting(shop)),
            };
            villager.Source["MCMapShopId"] = new TagNodeLong(marker);
            villager.Source["UUID"] = CreateEntityUuid(marker);

            if (_modernEntities != null) {
                _modernEntities.Update(point.X >> 4, point.Z >> 4, entities => {
                    RemoveShopkeeper(entities, marker);
                    entities.Add(villager);
                });
            } else {
                var entities = _bm.GetChunk(point.X, height, point.Z).Entities;
                RemoveShopkeeper(entities, marker);
                entities.Add(villager);
            }
        }

        private static void RemoveShopkeeper(EntityCollection entities, long marker) {
            entities.RemoveAll(entity => {
                TagNode value;
                return entity.Source.TryGetValue("MCMapShopId", out value) &&
                    value is TagNodeLong && ((TagNodeLong)value).Data == marker;
            });
        }

        private static TagNodeIntArray CreateEntityUuid(long marker) {
            long most = unchecked(marker * 6364136223846793005L);
            long least = unchecked(marker ^ -7046029254386353131L);
            return new TagNodeIntArray(new[] {
                unchecked((int)(most >> 32)), unchecked((int)most),
                unchecked((int)(least >> 32)), unchecked((int)least),
            });
        }

        private static string GetShopkeeperGreeting(ShopPlacement shop) {
            string name = shop.Name ?? shop.Definition.DisplayName;
            string[] greetings = {
                "Welcome to " + name + "!",
                "Thanks for visiting " + name + "!",
                "Come on in to " + name + "!",
                "Good to see you at " + name + "!",
                "Welcome! You're shopping at " + name + ".",
                "Hello, and welcome to " + name + "!",
                "We're glad you stopped by " + name + "!",
                "Make yourself at home in " + name + "!",
                "Take a look around " + name + "!",
                "Welcome in! This is " + name + ".",
                "It's a great day to visit " + name + "!",
                "Nice to meet you! Welcome to " + name + ".",
                "Step inside and explore " + name + "!",
                "There's always something good at " + name + "!",
                "Thanks for stopping by " + name + "!",
                "Come browse the shelves at " + name + "!",
            };
            int index = (int)(unchecked((ulong)shop.Id) % (ulong)greetings.Length);
            return greetings[index];
        }

        private static IEnumerable<BlockPosition> Neighbors(BlockPosition point) {
            yield return new BlockPosition(point.X + 1, point.Z);
            yield return new BlockPosition(point.X - 1, point.Z);
            yield return new BlockPosition(point.X, point.Z + 1);
            yield return new BlockPosition(point.X, point.Z - 1);
        }

        private static BlockFacing FacingIntoShop(HashSet<BlockPosition> points, BlockPosition point) {
            if (points.Contains(new BlockPosition(point.X + 1, point.Z))) return BlockFacing.East;
            if (points.Contains(new BlockPosition(point.X - 1, point.Z))) return BlockFacing.West;
            if (points.Contains(new BlockPosition(point.X, point.Z + 1))) return BlockFacing.South;
            return BlockFacing.North;
        }

        private static BlockFacing FacingPerpendicularToDisplayLine(HashSet<BlockPosition> points,
                BlockPosition point, int lineX, int lineZ) {
            double centerX = points.Average(candidate => candidate.X);
            double centerZ = points.Average(candidate => candidate.Z);
            var directions = lineX != 0
                ? new[] {
                    new { Facing = BlockFacing.South, X = 0, Z = 1 },
                    new { Facing = BlockFacing.North, X = 0, Z = -1 },
                }
                : new[] {
                    new { Facing = BlockFacing.East, X = 1, Z = 0 },
                    new { Facing = BlockFacing.West, X = -1, Z = 0 },
                };
            return directions
                .OrderByDescending(direction => InteriorDepth(points, point, direction.X, direction.Z))
                .ThenByDescending(direction =>
                    (centerX - point.X) * direction.X + (centerZ - point.Z) * direction.Z)
                .First().Facing;
        }

        private static int InteriorDepth(HashSet<BlockPosition> points, BlockPosition point, int dx, int dz) {
            int depth = 0;
            for (int distance = 1; distance <= 32; distance++) {
                if (!points.Contains(new BlockPosition(point.X + dx * distance, point.Z + dz * distance))) break;
                depth++;
            }
            return depth;
        }

        private static int ManhattanDistance(BlockPosition left, BlockPosition right) {
            return Math.Abs(left.X - right.X) + Math.Abs(left.Z - right.Z);
        }

        private static long DistanceSquared(BlockPosition left, BlockPosition right) {
            long x = left.X - right.X;
            long z = left.Z - right.Z;
            return x * x + z * z;
        }

    }
}
