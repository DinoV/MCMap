using Substrate;
using System;
using System.Collections.Generic;
using System.Linq;

namespace MinecraftMapper {
    partial class Mapper {
        private const long ClimatePledgeArenaWayId = 25123756;
        private const long SpaceNeedleWayId = 12903132;

        private bool TryDrawIconicBuilding(OsmReader.Building building,
                Dictionary<BlockPosition, int> buildingPoints,
                HashSet<BlockPosition> roofPoints) {
            switch (building.Id) {
                case ClimatePledgeArenaWayId:
                    DrawClimatePledgeArena(building, buildingPoints, roofPoints);
                    return true;
                case SpaceNeedleWayId:
                    DrawSpaceNeedle(building, buildingPoints, roofPoints);
                    return true;
                default:
                    return false;
            }
        }

        private void DrawSpaceNeedle(OsmReader.Building building,
                Dictionary<BlockPosition, int> buildingPoints,
                HashSet<BlockPosition> roofPoints) {
            List<BlockPosition> footprint = GetIconicFootprint(building);
            if (footprint.Count < 3) return;

            int centerX = (int)Math.Round(footprint.Average(point => point.X));
            int centerZ = (int)Math.Round(footprint.Average(point => point.Z));
            int radiusX = Math.Max(8, (footprint.Max(point => point.X) - footprint.Min(point => point.X)) / 2);
            int radiusZ = Math.Max(8, (footprint.Max(point => point.Z) - footprint.Min(point => point.Z)) / 2);
            int baseY = GetIconicBaseHeight(footprint);

            // Preserve the real 184 m proportion when the world ceiling permits it,
            // and scale the landmark down as a unit if terrain is ever raised.
            int totalHeight = Math.Max(80, Math.Min(184, 250 - baseY));
            int legJoinY = baseY + (int)Math.Round(totalHeight * 0.53);
            int saucerY = baseY + (int)Math.Round(totalHeight * 0.78);
            int roofY = baseY + (int)Math.Round(totalHeight * 0.88);
            int antennaTopY = baseY + totalHeight;

            // Three tapered legs, splayed across the mapped footprint.
            for (int leg = 0; leg < 3; leg++) {
                double angle = -Math.PI / 2.0 + leg * Math.PI * 2.0 / 3.0;
                int footX = centerX + (int)Math.Round(Math.Cos(angle) * radiusX * 0.72);
                int footZ = centerZ + (int)Math.Round(Math.Sin(angle) * radiusZ * 0.72);
                DrawIconicBeam(footX, baseY + 1, footZ, centerX, legJoinY, centerZ,
                    2, AcquaticBlocks.SmoothQuartz, buildingPoints);
            }

            DrawIconicCylinder(centerX, centerZ, legJoinY - 4, saucerY + 3,
                2, AcquaticBlocks.LightGrayConcrete, buildingPoints);

            int saucerRadiusX = Math.Max(10, (int)Math.Round(radiusX * 0.88));
            int saucerRadiusZ = Math.Max(10, (int)Math.Round(radiusZ * 0.88));
            DrawIconicEllipsoidLayer(centerX, centerZ, saucerY - 2,
                saucerRadiusX - 3, saucerRadiusZ - 3,
                AcquaticBlocks.SmoothQuartz, buildingPoints, roofPoints);
            DrawIconicEllipsoidLayer(centerX, centerZ, saucerY - 1,
                saucerRadiusX, saucerRadiusZ,
                AcquaticBlocks.SmoothQuartz, buildingPoints, roofPoints);
            for (int y = saucerY; y <= saucerY + 3; y++) {
                DrawIconicEllipseRing(centerX, centerZ, y,
                    saucerRadiusX, saucerRadiusZ,
                    y == saucerY + 1 ? AcquaticBlocks.CyanStainedGlass : AcquaticBlocks.LightGrayConcrete,
                    buildingPoints, roofPoints);
            }
            DrawIconicEllipsoidLayer(centerX, centerZ, saucerY + 4,
                saucerRadiusX - 1, saucerRadiusZ - 1,
                AcquaticBlocks.SmoothQuartz, buildingPoints, roofPoints);
            DrawIconicEllipsoidLayer(centerX, centerZ, saucerY + 5,
                saucerRadiusX - 4, saucerRadiusZ - 4,
                AcquaticBlocks.SmoothQuartz, buildingPoints, roofPoints);

            DrawIconicCylinder(centerX, centerZ, saucerY + 5, roofY,
                3, AcquaticBlocks.LightBlueStainedGlass, buildingPoints);
            DrawIconicEllipsoidLayer(centerX, centerZ, roofY,
                Math.Max(5, saucerRadiusX / 2), Math.Max(5, saucerRadiusZ / 2),
                AcquaticBlocks.SmoothQuartz, buildingPoints, roofPoints);
            DrawIconicCylinder(centerX, centerZ, roofY + 1, antennaTopY,
                0, AcquaticBlocks.IronBlock, buildingPoints);
            for (int y = roofY + 4; y <= antennaTopY; y += 8) {
                _bm.SetBlock(centerX, y, centerZ, AcquaticBlocks.SeaLantern);
            }
        }

        private void DrawClimatePledgeArena(OsmReader.Building building,
                Dictionary<BlockPosition, int> buildingPoints,
                HashSet<BlockPosition> roofPoints) {
            List<BlockPosition> polygon = GetIconicFootprint(building);
            if (polygon.Count < 3) return;

            int left = polygon.Min(point => point.X);
            int right = polygon.Max(point => point.X);
            int top = polygon.Min(point => point.Z);
            int bottom = polygon.Max(point => point.Z);
            int centerX = (left + right) / 2;
            int centerZ = (top + bottom) / 2;
            int baseY = GetIconicBaseHeight(polygon);
            int availableHeight = Math.Max(12, Math.Min(35, 250 - baseY));
            int wallHeight = Math.Max(7, availableHeight / 3);
            int roofRise = Math.Max(6, availableHeight - wallHeight);

            for (int z = top; z <= bottom; z++) {
                for (int x = left; x <= right; x++) {
                    if (!_conv.IsValidPoint(new BlockPosition(x, z)) ||
                            !IsInsidePolygon(x + 0.5, z + 0.5, polygon)) continue;

                    bool edge = !IsInsidePolygon(x - 0.5, z + 0.5, polygon) ||
                        !IsInsidePolygon(x + 1.5, z + 0.5, polygon) ||
                        !IsInsidePolygon(x + 0.5, z - 0.5, polygon) ||
                        !IsInsidePolygon(x + 0.5, z + 1.5, polygon);
                    var point = new BlockPosition(x, z);
                    buildingPoints[point] = baseY;

                    if (edge) {
                        for (int y = baseY + 1; y <= baseY + wallHeight; y++) {
                            string wall = ((x + z + y) & 7) == 0
                                ? AcquaticBlocks.SeaLantern
                                : AcquaticBlocks.CyanStainedGlass;
                            _bm.SetBlock(x, y, z, wall);
                        }
                    }

                    double nx = Math.Abs((x + 0.5 - centerX) / Math.Max(1.0, (right - left) / 2.0));
                    double nz = Math.Abs((z + 0.5 - centerZ) / Math.Max(1.0, (bottom - top) / 2.0));
                    double edgeDistance = Math.Min(1.0, Math.Max(nx, nz));
                    int roofY = baseY + wallHeight +
                        (int)Math.Round((1.0 - edgeDistance) * roofRise);
                    string roofBlock = ((x - left) / 7) % 4 == 1
                        ? AcquaticBlocks.BlackConcrete
                        : AcquaticBlocks.SmoothQuartz;
                    _bm.SetBlock(x, roofY, z, roofBlock);
                    roofPoints.Add(point);
                }
            }

            // The four historic roof supports make the broad roof silhouette
            // legible from ground level.
            int insetX = Math.Max(5, (right - left) / 7);
            int insetZ = Math.Max(5, (bottom - top) / 7);
            foreach (var support in new[] {
                    new BlockPosition(left + insetX, top + insetZ),
                    new BlockPosition(right - insetX, top + insetZ),
                    new BlockPosition(left + insetX, bottom - insetZ),
                    new BlockPosition(right - insetX, bottom - insetZ) }) {
                DrawIconicCylinder(support.X, support.Z, baseY + 1,
                    baseY + wallHeight + roofRise / 2, 2,
                    AcquaticBlocks.LightGrayConcrete, buildingPoints);
            }
        }

        private List<BlockPosition> GetIconicFootprint(OsmReader.Building building) {
            var points = building.BuildingNodes
                .Select(node => _conv.ToBlock(node.Lat, node.Long))
                .Where(point => _conv.IsValidPoint(point))
                .ToList();
            if (points.Count > 1 && points[0].Equals(points[points.Count - 1]))
                points.RemoveAt(points.Count - 1);
            return points;
        }

        private int GetIconicBaseHeight(IList<BlockPosition> footprint) {
            int maximum = Int32.MinValue;
            foreach (BlockPosition point in footprint)
                maximum = Math.Max(maximum, GetTerrainHeight(point));
            return maximum == Int32.MinValue ? 0 : maximum;
        }

        private void DrawIconicBeam(int x0, int y0, int z0, int x1, int y1, int z1,
                int radius, string blockName, Dictionary<BlockPosition, int> buildingPoints) {
            int steps = Math.Max(Math.Abs(x1 - x0), Math.Max(Math.Abs(y1 - y0), Math.Abs(z1 - z0)));
            for (int step = 0; step <= steps; step++) {
                double t = steps == 0 ? 0 : (double)step / steps;
                int x = (int)Math.Round(x0 + (x1 - x0) * t);
                int y = (int)Math.Round(y0 + (y1 - y0) * t);
                int z = (int)Math.Round(z0 + (z1 - z0) * t);
                for (int dz = -radius; dz <= radius; dz++)
                    for (int dx = -radius; dx <= radius; dx++)
                        if (dx * dx + dz * dz <= radius * radius)
                            SetIconicBlock(x + dx, y, z + dz, blockName, buildingPoints);
            }
        }

        private void DrawIconicCylinder(int centerX, int centerZ, int bottomY, int topY,
                int radius, string blockName, Dictionary<BlockPosition, int> buildingPoints) {
            for (int y = bottomY; y <= topY; y++)
                for (int z = centerZ - radius; z <= centerZ + radius; z++)
                    for (int x = centerX - radius; x <= centerX + radius; x++)
                        if ((x - centerX) * (x - centerX) + (z - centerZ) * (z - centerZ) <= radius * radius)
                            SetIconicBlock(x, y, z, blockName, buildingPoints);
        }

        private void DrawIconicEllipsoidLayer(int centerX, int centerZ, int y,
                int radiusX, int radiusZ, string blockName,
                Dictionary<BlockPosition, int> buildingPoints, HashSet<BlockPosition> roofPoints) {
            for (int z = centerZ - radiusZ; z <= centerZ + radiusZ; z++)
                for (int x = centerX - radiusX; x <= centerX + radiusX; x++) {
                    double distance = Math.Pow((double)(x - centerX) / radiusX, 2) +
                        Math.Pow((double)(z - centerZ) / radiusZ, 2);
                    if (distance <= 1.0) {
                        SetIconicBlock(x, y, z, blockName, buildingPoints);
                        roofPoints.Add(new BlockPosition(x, z));
                    }
                }
        }

        private void DrawIconicEllipseRing(int centerX, int centerZ, int y,
                int radiusX, int radiusZ, string blockName,
                Dictionary<BlockPosition, int> buildingPoints, HashSet<BlockPosition> roofPoints) {
            for (int z = centerZ - radiusZ; z <= centerZ + radiusZ; z++)
                for (int x = centerX - radiusX; x <= centerX + radiusX; x++) {
                    double outer = Math.Pow((double)(x - centerX) / radiusX, 2) +
                        Math.Pow((double)(z - centerZ) / radiusZ, 2);
                    double inner = Math.Pow((double)(x - centerX) / Math.Max(1, radiusX - 2), 2) +
                        Math.Pow((double)(z - centerZ) / Math.Max(1, radiusZ - 2), 2);
                    if (outer <= 1.0 && inner >= 1.0) {
                        SetIconicBlock(x, y, z, blockName, buildingPoints);
                        roofPoints.Add(new BlockPosition(x, z));
                    }
                }
        }

        private void SetIconicBlock(int x, int y, int z, string blockName,
                Dictionary<BlockPosition, int> buildingPoints) {
            var point = new BlockPosition(x, z);
            if (!_conv.IsValidPoint(point) || y < 0 || y > 250) return;
            _bm.SetBlock(x, y, z, blockName);
            if (!buildingPoints.ContainsKey(point))
                buildingPoints[point] = GetTerrainHeight(point);
        }
    }
}
