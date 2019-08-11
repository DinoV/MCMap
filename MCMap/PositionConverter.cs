using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MinecraftMapper {
    /// <summary>
    /// Uses multiple PositionBase's to calculate a mapping into the minecraft world.  The multiple
    /// positions are averaged and weighted based upon how close we are to them.
    /// </summary>
    class PositionConverter {
        private readonly PositionBase[] _positions;
        private readonly int _maxX, _maxZ, _minX, _minZ;

        public PositionConverter(int minX, int minZ, int worldWidth, int worldHeight, params PositionBase[] positions) {
            _minX = minX;
            _minZ = minZ;
            _maxX = worldWidth;
            _maxZ = worldHeight;
            _positions = positions;
        }

        public bool IsValidPoint(BlockPosition point)   {
            //return point.X >= 0 && point.Z >= 0 && point.X < 10688 && point.Z < 22500;
            return point.X >= _minX && point.Z >= _minZ && point.X < _maxX && point.Z < _maxZ;
        }

        public BlockPosition ToBlock(double latitude, double longitude) {
            var targetZ = PositionBase.Lat2YMeters(latitude);
            var targetX = PositionBase.Long2XMeters(longitude);

            double totalInverseDist = 0;
            double zTotal = 0, xTotal = 0;
            for (int i = 0; i < this._positions.Length; i++) {
                var zDelta = ((this._positions[i].baseZ - targetZ) * 1.10);
                var xDelta = ((targetX - this._positions[i].baseX) * 1.10);

                var dist = zDelta * zDelta + xDelta * xDelta;
                var inverseDist = dist == 0 ? 1 : (1 / dist);
                zTotal += (this._positions[i].mcZ + zDelta) * inverseDist;
                xTotal += (this._positions[i].mcX + xDelta) * inverseDist;
                totalInverseDist += inverseDist;
            }

            return new BlockPosition(
                (int)(xTotal / totalInverseDist) /*+ 1100*/, 
                (int)(zTotal / totalInverseDist) /*+ 200*/
            );
        }
    }
}
