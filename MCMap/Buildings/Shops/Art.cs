using Substrate;
using Substrate.Nbt;
using System;
using System.Collections.Generic;
using System.Linq;
using static MinecraftMapper.OsmReader;

namespace MinecraftMapper {
    partial class Mapper {
        private sealed class PaintingVariant {
            public readonly string Id;
            public readonly int Width;
            public readonly int Height;

            public PaintingVariant(string id, int width, int height) {
                Id = "minecraft:" + id;
                Width = width;
                Height = height;
            }
        }

        private static readonly PaintingVariant[] PaintingVariants = {
            new PaintingVariant("kebab", 1, 1),
            new PaintingVariant("aztec", 1, 1),
            new PaintingVariant("alban", 1, 1),
            new PaintingVariant("aztec2", 1, 1),
            new PaintingVariant("bomb", 1, 1),
            new PaintingVariant("plant", 1, 1),
            new PaintingVariant("wasteland", 1, 1),
            new PaintingVariant("pool", 2, 1),
            new PaintingVariant("courbet", 2, 1),
            new PaintingVariant("sea", 2, 1),
            new PaintingVariant("sunset", 2, 1),
            new PaintingVariant("creebet", 2, 1),
            new PaintingVariant("wanderer", 1, 2),
            new PaintingVariant("graham", 1, 2),
            new PaintingVariant("match", 2, 2),
            new PaintingVariant("bust", 2, 2),
            new PaintingVariant("stage", 2, 2),
            new PaintingVariant("void", 2, 2),
            new PaintingVariant("skull_and_roses", 2, 2),
            new PaintingVariant("wither", 2, 2),
            new PaintingVariant("fighters", 4, 2),
            new PaintingVariant("skeleton", 4, 3),
            new PaintingVariant("donkey_kong", 4, 3),
            new PaintingVariant("pointer", 4, 4),
            new PaintingVariant("pigscene", 4, 4),
            new PaintingVariant("burning_skull", 4, 4),
        };

        private static ShopDefinition CreateArtStoreDefinition() {
            return new ShopDefinition(
                Shop.Art,
                "Art Store",
                "minecraft:shepherd",
                new[] { new ShopItemGroup("Paintings", new ShopItem("minecraft:painting")) },
                displayOverride: DrawArtStoreDisplay);
        }

        private static ShopItem[] DrawArtStoreDisplay(Mapper mapper, ShopPlacement shop,
                BlockPosition point, int height, BlockFacing facing, int offset, int lineX, int lineZ,
                ShopItem[] sectionItems, bool againstWall) {
            bool useBin = ShouldUseArtBin(shop, point);
            if (againstWall) {
                if (useBin) return sectionItems;
                return null;
            }

            for (int y = 0; y < 4; y++) {
                mapper._bm.SetBlock(point.X, height - 1 + y, point.Z, AcquaticBlocks.WhiteConcrete);
            }
            return null;
        }

        private static bool ShouldUseArtBin(ShopPlacement shop, BlockPosition point) {
            uint value = unchecked((uint)(shop.Id ^ (shop.Id >> 32) ^
                ((long)point.X * 613) ^ ((long)point.Z * 173)));
            return value % 5 == 0;
        }

        private void DrawArtShopSection(ShopPlacement shop, List<BlockPosition> displays,
                int floorHeight, int lineX, int lineZ, bool againstWall, Random random) {
            if (displays.Count == 0) return;
            int wallBottom = floorHeight + 1;
            if (againstWall) {
                BlockFacing inward = FacingPerpendicularToDisplayLine(
                    shop.FloorPoints, displays[displays.Count / 2], lineX, lineZ);
                DrawRandomPainting(shop, displays, wallBottom, inward, random, 0, true);
                return;
            }
            BlockFacing firstSide = lineX != 0 ? BlockFacing.North : BlockFacing.West;
            BlockFacing secondSide = Opposite(firstSide);
            DrawRandomPainting(shop, displays, wallBottom, firstSide, random, 0, againstWall);
            DrawRandomPainting(shop, displays, wallBottom, secondSide, random, 1, againstWall);
        }

        private void DrawRandomPainting(ShopPlacement shop, List<BlockPosition> displays,
                int wallBottom, BlockFacing facing, Random random, int index, bool againstWall) {
            var candidates = PaintingVariants
                .Where(variant => variant.Width <= displays.Count && variant.Height <= 4)
                .ToArray();
            if (candidates.Length == 0) return;
            var largerCandidates = candidates
                .Where(variant => variant.Width > 1 || variant.Height > 1)
                .ToArray();
            if (largerCandidates.Length > 0) candidates = largerCandidates;
            PaintingVariant variant = candidates[random.Next(candidates.Length)];
            int first = (displays.Count - variant.Width) / 2;
            int center = first + (variant.Width - 1) / 2;
            BlockPosition wall = displays[center];
            if (againstWall) wall = Offset(wall, Opposite(facing));
            int anchorY = wallBottom + (variant.Height - 1) / 2 + 1;
            DrawShopPainting(shop, wall, anchorY, facing, variant, index);
        }

        private void DrawShopPainting(ShopPlacement shop, BlockPosition wall, int anchorY,
                BlockFacing facing, PaintingVariant variant, int index) {
            BlockPosition anchor = Offset(wall, facing);
            long marker = unchecked(shop.Id ^ ((long)wall.X << 32) ^ (uint)wall.Z ^
                ((long)anchorY << 16) ^ ((long)facing << 8) ^ index ^ 0x5041494e54494e47L);
            var painting = new TypedEntity("minecraft:painting");
            painting.Position = PaintingPosition(anchor, anchorY, facing, variant);
            painting.Rotation = new Orientation { Yaw = FacingYaw(facing) };
            painting.Source["block_pos"] = new TagNodeIntArray(new[] { anchor.X, anchorY, anchor.Z });
            painting.Source["facing"] = new TagNodeByte(PaintingFacing(facing));
            painting.Source["variant"] = new TagNodeString(variant.Id);
            painting.Source["Invulnerable"] = new TagNodeByte(1);
            painting.Source["MCMapShopPaintingId"] = new TagNodeLong(marker);
            painting.Source["UUID"] = CreateEntityUuid(marker);
            Action<EntityCollection> update = entities => {
                entities.RemoveAll(entity => {
                    TagNode value;
                    return entity.Source.TryGetValue("MCMapShopPaintingId", out value) &&
                        value is TagNodeLong && ((TagNodeLong)value).Data == marker;
                });
                entities.Add(painting);
            };
            if (_modernEntities != null) _modernEntities.Update(anchor.X >> 4, anchor.Z >> 4, update);
            else update(_bm.GetChunk(anchor.X, anchorY, anchor.Z).Entities);
        }

        private static Vector3 PaintingPosition(BlockPosition anchor, int anchorY,
                BlockFacing facing, PaintingVariant variant) {
            double x = anchor.X + .5;
            double y = anchorY + .5 + (variant.Height % 2 == 0 ? .5 : 0);
            double z = anchor.Z + .5;
            if (facing == BlockFacing.South) z = anchor.Z + .03125;
            else if (facing == BlockFacing.West) x = anchor.X + .96875;
            else if (facing == BlockFacing.North) z = anchor.Z + .96875;
            else if (facing == BlockFacing.East) x = anchor.X + .03125;
            if (variant.Width % 2 == 0) {
                if (facing == BlockFacing.South) x += .5;
                else if (facing == BlockFacing.West) z += .5;
                else if (facing == BlockFacing.North) x -= .5;
                else if (facing == BlockFacing.East) z -= .5;
            }
            return new Vector3 { X = x, Y = y, Z = z };
        }

        private static byte PaintingFacing(BlockFacing facing) {
            switch (facing) {
                case BlockFacing.South: return 0;
                case BlockFacing.West: return 1;
                case BlockFacing.North: return 2;
                case BlockFacing.East: return 3;
                default: return 0;
            }
        }
    }
}
