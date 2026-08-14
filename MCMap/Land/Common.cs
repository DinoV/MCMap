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
        private void DrawLandUses() {
            foreach (var landUse in _reader.LandUses) {
                var start = landUse.Nodes[0];
                Dictionary<BlockPosition, int> wallPoints = new Dictionary<BlockPosition, int>();
                int top, left, bottom, right;
                CalculateBounds(landUse.Nodes, out top, out left, out bottom, out right);

                for (int i = 0; i < landUse.Nodes.Length; i++) {

                    var from = _conv.ToBlock(start.Lat, start.Long);
                    var to = _conv.ToBlock(landUse.Nodes[i].Lat, landUse.Nodes[i].Long);

                    foreach (var point in PlotLine(from.X, from.Z, to.X, to.Z)) {
                        wallPoints[point.Block] = 0;
                    }
                    start = landUse.Nodes[i];
                }
                var rand = new Random((int)landUse.WayId);
                FillArea(top, left, bottom, right, wallPoints, point => {
                    switch (rand.Next() % 48) {
                        case 0: _bm.SetBlock(point.X, GetHeight(point) + 1, point.Z, AcquaticBlocks.CarvedPumpkin, facing: BlockFacing.South); break;
                        case 1: _bm.SetBlock(point.X, GetHeight(point) + 1, point.Z, AcquaticBlocks.Cocoa, facing: BlockFacing.South, age: 0); break;
                        case 2: _bm.SetBlock(point.X, GetHeight(point) + 1, point.Z, AcquaticBlocks.Wheat, age: 0); break;
                        case 3: _bm.SetBlock(point.X, GetHeight(point) + 1, point.Z, AcquaticBlocks.DeadBush); break;
                        case 4: _bm.SetBlock(point.X, GetHeight(point) + 1, point.Z, AcquaticBlocks.Farmland, moisture: 0); break;
                        case 5: _bm.SetBlock(point.X, GetHeight(point) + 1, point.Z, AcquaticBlocks.FlowerPot); break;
                        case 6: _bm.SetBlock(point.X, GetHeight(point) + 1, point.Z, AcquaticBlocks.GrassBlock, snowy: false); break;
                        case 7: _bm.SetBlock(point.X, GetHeight(point) + 1, point.Z, AcquaticBlocks.HayBlock, axis: BlockAxis.Y); break;
                        case 8: _bm.SetBlock(point.X, GetHeight(point) + 1, point.Z, AcquaticBlocks.BrownMushroomBlock, north: false, east: false, south: false, west: false, up: false, down: false); break;
                        case 9: _bm.SetBlock(point.X, GetHeight(point) + 1, point.Z, AcquaticBlocks.RedMushroomBlock, north: false, east: false, south: false, west: false, up: false, down: false); break;
                        case 10: _bm.SetBlock(point.X, GetHeight(point) + 1, point.Z, AcquaticBlocks.Melon); break;
                        case 11: _bm.SetBlock(point.X, GetHeight(point) + 1, point.Z, AcquaticBlocks.MelonStem, age: 0); break;
                        case 12: _bm.SetBlock(point.X, GetHeight(point) + 1, point.Z, AcquaticBlocks.Mycelium, snowy: false); break;
                        case 13: _bm.SetBlock(point.X, GetHeight(point) + 1, point.Z, AcquaticBlocks.Potatoes, age: 0); break;
                        case 14: _bm.SetBlock(point.X, GetHeight(point) + 1, point.Z, AcquaticBlocks.PumpkinStem, age: 0); break;
                        case 15: _bm.SetBlock(point.X, GetHeight(point) + 1, point.Z, AcquaticBlocks.Poppy); break;
                        case 16: _bm.SetBlock(point.X, GetHeight(point) + 1, point.Z, AcquaticBlocks.SugarCane, age: 0); break;
                        default:
                            _bm.SetBlock(point.X, GetHeight(point), point.Z, AcquaticBlocks.Farmland, moisture: 0); break;
                    }
                });
            }
        }

        private bool FillMissingFence(BlockPosition prev, BlockPosition cur, BlockPosition compare) {
            if (prev.Equals(compare)) {
                var point = new BlockPosition(cur.X, prev.Z);
                _bm.SetBlock(point.X, GetHeight(point) + 1, point.Z, AcquaticBlocks.OakFence,
                    north: false, east: false, south: false, west: false);
                return true;
            }
            return false;
        }

        private void DrawLeisures() {
            foreach (var landUse in _reader.Leisures) {
                var start = landUse.Nodes[0];

                int count = 0;
                BlockPosition? prev = null;
                for (int i = 1; i < landUse.Nodes.Length; i++) {
                    var from = _conv.ToBlock(start.Lat, start.Long);
                    var to = _conv.ToBlock(landUse.Nodes[i].Lat, landUse.Nodes[i].Long);

                    bool first = true;
                    foreach (var point in PlotLine(from.X, from.Z, to.X, to.Z)) {
                        if (!_conv.IsValidPoint(point.Block)) {
                            continue;
                        }
                        if (first || prev == null || (++count % 10) != 0) {
                            if (prev != null) {

                                bool filled = FillMissingFence(prev.Value, point.Block, new BlockPosition(point.Block.X - 1, point.Block.Z - 1)) ||
                                     FillMissingFence(prev.Value, point.Block, new BlockPosition(point.Block.X + 1, point.Block.Z + 1)) ||
                                     FillMissingFence(prev.Value, point.Block, new BlockPosition(point.Block.X + 1, point.Block.Z - 1)) ||
                                     FillMissingFence(prev.Value, point.Block, new BlockPosition(point.Block.X - 1, point.Block.Z + 1));
                            }
                            _bm.SetBlock(point.Block.X, GetHeight(point.Block) + 1, point.Block.Z, AcquaticBlocks.OakFence,
                                north: false, east: false, south: false, west: false);
                        } else {
                            BlockFacing facing =
                                prev.Value.X == point.Block.X - 1
                                || prev.Value.X == point.Block.X + 1
                                ? BlockFacing.South
                                : BlockFacing.East;
                            _bm.SetBlock(
                                point.Block.X,
                                GetHeight(point.Block) + 1,
                                point.Block.Z,
                                AcquaticBlocks.OakFenceGate,
                                facing: facing,
                                open: false,
                                powered: false,
                                inWall: false);
                        }

                        prev = point.Block;
                        first = false;
                    }
                    start = landUse.Nodes[i];
                }
            }
        }


        private void DrawParkingLots() {
            foreach (var parking in _reader.ParkingLots) {
                DrawParkingLot(parking);
            }
        }

        private void DrawParkingLot(OsmReader.ParkingInfo parking) {
            if (parking.Parking != OsmReader.Parking.Surface) {
                return;
            }
            int blockType = BlockType.CONCRETE;
            int blockData = 15; // black concrete
            var start = parking.Nodes[0];
            var baseHeights = new Dictionary<BlockPosition, int>();
            for (int i = 1; i < parking.Nodes.Length; i++) {
                var from = _conv.ToBlock(start.Lat, start.Long);
                var to = _conv.ToBlock(parking.Nodes[i].Lat, parking.Nodes[i].Long);

                foreach (var point in PlotLine(from.X, from.Z, to.X, to.Z)) {
                    if (!_conv.IsValidPoint(point.Block)) {
                        continue;
                    }

                    int height = baseHeights[point.Block] = GetHeight(point.Block);

                    SetMappedBlock(_bm, point.Block.X, height, point.Block.Z, blockType, blockData);
                }

                start = parking.Nodes[i];
            }

            int top, left, bottom, right;
            CalculateBounds(parking.Nodes, out top, out left, out bottom, out right);
            FillArea(top, left, bottom, right, baseHeights, blockType, blockData);
        }

        private void DrawCoastlines() {
            const int MaximumCoastlineArea = 250000;

            foreach (var coastline in _reader.Coastlines) {
                var polygon = coastline.Nodes
                    .Select(node => _conv.ToBlock(node.Lat, node.Long))
                    .Where(point => _conv.IsValidPoint(point))
                    .ToList();
                if (polygon.Count < 3) {
                    continue;
                }

                // A coastline is deliberately open: its ordered nodes have land on
                // the left and water on the right. Closing its two landward ends
                // produces the bounded piece which must be joined to the existing
                // terrain (piers are a common example).
                int left = polygon.Min(point => point.X);
                int right = polygon.Max(point => point.X);
                int top = polygon.Min(point => point.Z);
                int bottom = polygon.Max(point => point.Z);
                long area = (long)(right - left + 1) * (bottom - top + 1);
                if (area > MaximumCoastlineArea) {
                    WriteLine("Skipping coastline {0}: its {1}-block bounding area is too large.",
                        coastline.WayId, area);
                    continue;
                }

                int mergeHeight = Math.Max(
                    GetHeight(polygon[0]),
                    GetHeight(polygon[polygon.Count - 1]));
                int rendered = 0;
                for (int z = top; z <= bottom; z++) {
                    for (int x = left; x <= right; x++) {
                        var point = new BlockPosition(x, z);
                        if (!_conv.IsValidPoint(point) ||
                                !IsInsidePolygon(x + 0.5, z + 0.5, polygon)) {
                            continue;
                        }

                        int height = GetHeight(point);
                        if (height > mergeHeight) {
                            continue;
                        }

                        // Build a solid platform one block above the adjoining
                        // terrain. Filling the short column also prevents a pier
                        // from floating when the heightmap contains water or air.
                        int firstY = Math.Max(height + 1, mergeHeight - 15);
                        for (int y = firstY; y <= mergeHeight + 1; y++) {
                            _bm.SetBlock(x, y, z, AcquaticBlocks.StoneBricks);
                        }
                        rendered++;
                    }
                }
                WriteLine("Rendered coastline {0}: {1} blocks at height {2}.",
                    coastline.WayId, rendered, mergeHeight + 1);
            }
        }

        private static bool IsInsidePolygon(double x, double z,
                IList<BlockPosition> polygon) {
            bool inside = false;
            for (int current = 0, previous = polygon.Count - 1;
                    current < polygon.Count; previous = current++) {
                double currentZ = polygon[current].Z;
                double previousZ = polygon[previous].Z;
                if ((currentZ > z) == (previousZ > z)) {
                    continue;
                }
                double edgeX = (polygon[previous].X - polygon[current].X) *
                    (z - currentZ) / (previousZ - currentZ) + polygon[current].X;
                if (x < edgeX) {
                    inside = !inside;
                }
            }
            return inside;
        }

    }
}
