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
        private void DrawRails() {
            IEnumerable<IGrouping<string, OsmReader.Rail>> rails = _reader.Rails.Values.GroupBy(x => x.Name ?? "")
                .OrderBy(x => MapOrder(_conv, x.First().Nodes.First()));
            int cur = 0;
            foreach (var railGroup in rails) {
                foreach (var rail in railGroup) {
                    if ((++cur % 50) == 0) {
                        SaveBlocks();
                    }

                    WriteLine($"{rail.Id:10} Rail: {rail.Name} ({cur}/{_reader.Rails.Count})");
                    var start = rail.Nodes[0];
                    for (int i = 1; i < rail.Nodes.Length; i++) {
                        var from = _conv.ToBlock(start.Lat, start.Long);
                        var to = _conv.ToBlock(rail.Nodes[i].Lat, rail.Nodes[i].Long);

                        foreach (var point in PlotLine(from.X, from.Z, to.X, to.Z)) {
                            if (_conv.IsValidPoint(point.Block) && point.Block.X > 1 && point.Block.Z > 1) {
                                _bm.SetBlock(
                                    point.Block.X,
                                    GetHeight(point.Block),
                                    point.Block.Z,
                                    AcquaticBlocks.Rail,
                                    new Substrate.Nbt.TagNodeCompound {
                                        {
                                            BlockProperties.Shape,
                                            new Substrate.Nbt.TagNodeString(
                                                "north_south")
                                        }
                                    });
                            }
                        }
                    }
                }
            }
        }

    }
}
