using CsvHelper;
using Substrate;
using Substrate.TileEntities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using static MinecraftMapper.OsmReader;

namespace MinecraftMapper {
    partial class Mapper {
        private static void SetSignName(List<string> names, TileEntitySign ent) {
            for (int i = 0; i < names.Count; i++) {
                if (names[i].Length > 17) {
                    names[i] = names[i].Substring(0, 17);
                }
            }
            if (names.Count > 0) {
                ent.Text1 = SignJson(names[0]);
                if (names.Count > 1) {
                    ent.Text2 = SignJson(names[1]);
                    if (names.Count > 2) {
                        ent.Text3 = SignJson(names[2]);
                        if (names.Count > 3) {
                            ent.Text4 = SignJson(names[3]);
                        }
                    }
                }
            }
        }

        private static string SignJson(string name) {
            return string.Format("{{\"text\":\"{0}\"}}", name.Replace("\"", "\\\""));
        }

        private static void AddSignName(List<string> names, string name) {
            if (name != null) {
                while (name.Length > 17) {
                    var space = name.Substring(0, 17).LastIndexOf(' ');
                    if (space == -1) {
                        break;
                    }
                    names.Add(name.Substring(0, space));
                    name = name.Substring(space + 1);
                }
            }
            if (!string.IsNullOrWhiteSpace(name)) {
                names.Add(name);
            }
        }

    }
}
