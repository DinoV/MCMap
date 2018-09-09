using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MinecraftMapper {
    struct BlockPosition : IEquatable<BlockPosition> {
        public readonly int X, Z;
        public BlockPosition(int x, int z) {
            X = x;
            Z = z;
        }

        public override string ToString() {
            return string.Format("{0},{1}", X, Z);
        }

        public override int GetHashCode() {
            return X.GetHashCode() ^ Z.GetHashCode();
        }

        public override bool Equals(object obj) {
            if (obj is BlockPosition) {
                return Equals((BlockPosition)obj);
            }
            return false;
        }

        public bool Equals(BlockPosition other) {
            return X == other.X && Z == other.Z;
        }
    }
}
