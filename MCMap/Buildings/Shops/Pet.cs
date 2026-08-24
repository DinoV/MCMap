using Substrate;
using Substrate.Nbt;
using System;
using System.Collections.Generic;
using System.Linq;
using static MinecraftMapper.OsmReader;

namespace MinecraftMapper {
    partial class Mapper {
        private sealed class PetStoreAnimal {
            public readonly string EntityId;
            public readonly string Label;
            public readonly int Count;

            public PetStoreAnimal(string entityId, string label, int count) {
                EntityId = entityId;
                Label = label;
                Count = count;
            }
        }

        private static readonly PetStoreAnimal[] PetStoreAnimals = {
            new PetStoreAnimal("minecraft:cat", "Cats", 2),
            new PetStoreAnimal("minecraft:chicken", "Chickens", 3),
            new PetStoreAnimal("minecraft:llama", "Llamas", 1),
            new PetStoreAnimal("minecraft:wolf", "Dogs", 2),
        };

        private static ShopDefinition CreatePetStoreDefinition() {
            return new ShopDefinition(
                Shop.Pet,
                "Pet Store",
                "minecraft:shepherd",
                new ShopItemGroup[0]);
        }

        private void DrawPetStore(ShopPlacement shop, int floorHeight,
                HashSet<BlockPosition> doors, HashSet<BlockPosition> occupied) {
            int animalIndex = 0;
            foreach (PetStoreAnimal animal in PetStoreAnimals) {
                List<BlockPosition> pen = FindPetStorePen(shop, floorHeight, doors, occupied);
                if (pen != null) {
                    DrawPetStorePen(animal, pen, floorHeight, occupied);
                    var interior = pen.Where(point =>
                            point.X > pen.Min(candidate => candidate.X) &&
                            point.X < pen.Max(candidate => candidate.X) &&
                            point.Z > pen.Min(candidate => candidate.Z) &&
                            point.Z < pen.Max(candidate => candidate.Z))
                        .ToArray();
                    for (int i = 0; i < animal.Count; i++) {
                        PlacePetStoreAnimal(shop, animal, interior[i % interior.Length],
                            floorHeight + 1, animalIndex++);
                    }
                    continue;
                }

                var roaming = FindPetStoreRoamingPoints(shop, floorHeight, doors, occupied)
                    .Take(animal.Count)
                    .ToArray();
                foreach (BlockPosition point in roaming) {
                    occupied.Add(point);
                    PlacePetStoreAnimal(shop, animal, point, floorHeight + 1, animalIndex++);
                }
            }
        }

        private List<BlockPosition> FindPetStorePen(ShopPlacement shop, int floorHeight,
                HashSet<BlockPosition> doors, HashSet<BlockPosition> occupied) {
            int minX = shop.FloorPoints.Min(point => point.X);
            int maxX = shop.FloorPoints.Max(point => point.X);
            int minZ = shop.FloorPoints.Min(point => point.Z);
            int maxZ = shop.FloorPoints.Max(point => point.Z);
            for (int z = minZ; z <= maxZ - 4; z++) {
                for (int x = minX; x <= maxX - 4; x++) {
                    var points = new List<BlockPosition>();
                    bool usable = true;
                    for (int dz = 0; dz < 5 && usable; dz++) {
                        for (int dx = 0; dx < 5; dx++) {
                            var point = new BlockPosition(x + dx, z + dz);
                            if (!shop.FloorPoints.Contains(point) || occupied.Contains(point) ||
                                    doors.Any(door => ManhattanDistance(point, door) <= 2) ||
                                    _bm.GetStringID(point.X, floorHeight + 1, point.Z) != AcquaticBlocks.Air) {
                                usable = false;
                                break;
                            }
                            points.Add(point);
                        }
                    }
                    if (usable && !points.Any(point => Neighbors(point).Any(occupied.Contains))) {
                        return points;
                    }
                }
            }
            return null;
        }

        private void DrawPetStorePen(PetStoreAnimal animal, List<BlockPosition> pen,
                int floorHeight, HashSet<BlockPosition> occupied) {
            int minX = pen.Min(point => point.X), maxX = pen.Max(point => point.X);
            int minZ = pen.Min(point => point.Z), maxZ = pen.Max(point => point.Z);
            foreach (BlockPosition point in pen) {
                bool edge = point.X == minX || point.X == maxX ||
                    point.Z == minZ || point.Z == maxZ;
                if (edge) {
                    _bm.SetBlock(point.X, floorHeight + 1, point.Z, AcquaticBlocks.OakFence);
                }
                occupied.Add(point);
            }

            var signPoint = new BlockPosition((minX + maxX) / 2, minZ);
            DrawSign(signPoint, Direction.North, BlockType.SIGN_POST,
                floorHeight + 2, animal.Label);
        }

        private IEnumerable<BlockPosition> FindPetStoreRoamingPoints(ShopPlacement shop,
                int floorHeight, HashSet<BlockPosition> doors, HashSet<BlockPosition> occupied) {
            double centerX = shop.FloorPoints.Average(point => point.X);
            double centerZ = shop.FloorPoints.Average(point => point.Z);
            return shop.FloorPoints
                .Where(point => !occupied.Contains(point) &&
                    doors.All(door => ManhattanDistance(point, door) >= 3) &&
                    _bm.GetStringID(point.X, floorHeight + 1, point.Z) == AcquaticBlocks.Air)
                .OrderBy(point => Math.Abs(point.X - centerX) + Math.Abs(point.Z - centerZ));
        }

        private void PlacePetStoreAnimal(ShopPlacement shop, PetStoreAnimal animal,
                BlockPosition point, int height, int index) {
            long marker = unchecked(shop.Id ^ ((long)index << 40) ^ 0x50455453484f504cL);
            var entity = new TypedEntity(animal.EntityId);
            entity.Position = new Vector3 { X = point.X + .5, Y = height, Z = point.Z + .5 };
            entity.IsOnGround = true;
            entity.Air = 300;
            entity.Source["Health"] = new TagNodeFloat(20);
            entity.Source["PersistenceRequired"] = new TagNodeByte(1);
            entity.Source["MCMapPetShopAnimalId"] = new TagNodeLong(marker);
            entity.Source["UUID"] = CreateEntityUuid(marker);
            if (animal.EntityId == "minecraft:wolf") {
                entity.Source["CustomName"] = new TagNodeCompound {
                    ["text"] = new TagNodeString("Dog"),
                };
            }

            Action<EntityCollection> update = entities => {
                entities.RemoveAll(candidate => {
                    TagNode value;
                    return candidate.Source.TryGetValue("MCMapPetShopAnimalId", out value) &&
                        value is TagNodeLong && ((TagNodeLong)value).Data == marker;
                });
                entities.Add(entity);
            };
            if (_modernEntities != null) _modernEntities.Update(point.X >> 4, point.Z >> 4, update);
            else update(_bm.GetChunk(point.X, height, point.Z).Entities);
        }
    }
}
