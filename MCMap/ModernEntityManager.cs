using Substrate;
using Substrate.Core;
using Substrate.Nbt;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MinecraftMapper {
    /// <summary>
    /// Reads and writes the separate entity region files used by modern Java worlds.
    /// </summary>
    sealed class ModernEntityManager {
        private sealed class EntityChunk {
            public readonly int X;
            public readonly int Z;
            public readonly TagNodeCompound Root;
            public readonly EntityCollection Entities;
            public bool Dirty;

            public EntityChunk(int x, int z, TagNodeCompound root, TagNodeList entities) {
                X = x;
                Z = z;
                Root = root;
                Entities = new EntityCollection(entities);
            }
        }

        private readonly string _path;
        private readonly int _dataVersion;
        private readonly Dictionary<string, EntityChunk> _chunks = new Dictionary<string, EntityChunk>();

        public ModernEntityManager(string path) {
            _path = path;
            _dataVersion = FindDataVersion(path);
        }

        public void Update(int chunkX, int chunkZ, Action<EntityCollection> update) {
            var chunk = GetChunk(chunkX, chunkZ);
            update(chunk.Entities);
            chunk.Dirty = true;
        }

        public void Save() {
            foreach (var regionGroup in _chunks.Values.Where(chunk => chunk.Dirty)
                    .GroupBy(chunk => new { X = chunk.X >> 5, Z = chunk.Z >> 5 })) {
                string file = Path.Combine(_path, $"r.{regionGroup.Key.X}.{regionGroup.Key.Z}.mca");
                using (var region = new RegionFile(file)) {
                    foreach (var chunk in regionGroup) {
                        using (var stream = region.GetChunkDataOutputStream(chunk.X & 31, chunk.Z & 31)) {
                            new NbtTree(chunk.Root).WriteTo(stream);
                        }
                        chunk.Dirty = false;
                    }
                }
            }
        }

        private EntityChunk GetChunk(int chunkX, int chunkZ) {
            string key = chunkX + "," + chunkZ;
            EntityChunk cached;
            if (_chunks.TryGetValue(key, out cached)) {
                return cached;
            }

            TagNodeCompound root = null;
            string file = Path.Combine(_path, $"r.{chunkX >> 5}.{chunkZ >> 5}.mca");
            if (File.Exists(file)) {
                using (var region = new RegionFile(file))
                using (var stream = region.GetChunkDataInputStream(chunkX & 31, chunkZ & 31)) {
                    if (stream != null) {
                        root = new NbtTree(stream).Root;
                    }
                }
            }

            if (root == null) {
                root = new TagNodeCompound {
                    ["Position"] = new TagNodeIntArray(new[] { chunkX, chunkZ }),
                    ["DataVersion"] = new TagNodeInt(_dataVersion),
                    ["Entities"] = new TagNodeList(TagType.TAG_COMPOUND),
                };
            }

            TagNode entitiesNode;
            TagNodeList entities = root.TryGetValue("Entities", out entitiesNode)
                ? entitiesNode as TagNodeList : null;
            if (entities == null) {
                entities = new TagNodeList(TagType.TAG_COMPOUND);
                root["Entities"] = entities;
            }
            return _chunks[key] = new EntityChunk(chunkX, chunkZ, root, entities);
        }

        private static int FindDataVersion(string path) {
            foreach (string file in Directory.EnumerateFiles(path, "r.*.*.mca")) {
                using (var region = new RegionFile(file)) {
                    for (int z = 0; z < 32; z++) {
                        for (int x = 0; x < 32; x++) {
                            if (!region.HasChunk(x, z)) continue;
                            using (var stream = region.GetChunkDataInputStream(x, z)) {
                                TagNode version;
                                var root = new NbtTree(stream).Root;
                                if (root.TryGetValue("DataVersion", out version) && version is TagNodeInt) {
                                    return ((TagNodeInt)version).Data;
                                }
                            }
                        }
                    }
                }
            }
            return 4903;
        }
    }
}
