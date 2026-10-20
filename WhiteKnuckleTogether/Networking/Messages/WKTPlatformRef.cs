using System;
using UnityEngine;
using WhiteKnuckleTogether.Networking.LiteNetLib;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// What a player stands on or hangs from, and where they are on it: a transform in a keyed level, found by the sibling
    /// indices down from the level's root, checked by its name, and <see cref="Local"/>, the player's position in its space.
    /// Others place the player on their own copy of it, so a player riding something that moves (an elevator, a tram) rides
    /// it on every screen instead of trailing behind it by the interpolation delay.
    /// </summary>
    public readonly struct WKTPlatformRef : IEquatable<WKTPlatformRef> {
        /// <summary>The deepest path kept: <see cref="MAX_DEPTH"/> sibling indices of 16 bits, in two 64-bit words.</summary>
        public const int MAX_DEPTH = 8;
        private const int c_index_bits = 16;
        private const int c_per_word = 64 / c_index_bits;

        public ulong LevelKey { get; }
        /// <summary>How many sibling indices lead down from the level's root. 0 is the root itself.</summary>
        public byte Depth { get; }
        public ulong PathLow { get; }
        public ulong PathHigh { get; }
        /// <summary>The stable hash of the transform's name, so a hierarchy that changed since is not mistaken for it.</summary>
        public uint NameHash { get; }
        public Vector3 Local { get; }
        /// <summary>Whether there is a platform at all. Level keys are never 0.</summary>
        public bool IsSet => LevelKey != 0;

        public WKTPlatformRef(ulong levelKey, byte depth, ulong pathLow, ulong pathHigh, uint nameHash, Vector3 local) {
            LevelKey = levelKey;
            Depth = depth;
            PathLow = pathLow;
            PathHigh = pathHigh;
            NameHash = nameHash;
            Local = local;
        }

        /// <summary>The sibling index at <paramref name="level"/> below the root, 0 for the first.</summary>
        public int GetIndex(int level) {
            var word = level < c_per_word ? PathLow : PathHigh;
            return (int)((word >> (level % c_per_word * c_index_bits)) & ushort.MaxValue);
        }

        /// <summary>Packs <paramref name="index"/> into the path at <paramref name="level"/>.</summary>
        public static void SetIndex(ref ulong pathLow, ref ulong pathHigh, int level, ushort index) {
            var shift = level % c_per_word * c_index_bits;
            if (level < c_per_word)
                pathLow |= (ulong)index << shift;
            else
                pathHigh |= (ulong)index << shift;
        }

        /// <summary>Whether both name the same transform, wherever on it the player is.</summary>
        public bool IsSameFrame(in WKTPlatformRef other) =>
            LevelKey == other.LevelKey && Depth == other.Depth && PathLow == other.PathLow && PathHigh == other.PathHigh && NameHash == other.NameHash;

        /// <summary>
        /// Blends the position on the platform while both are on the same one; otherwise the nearer one's, as the world
        /// position blends across the change.
        /// </summary>
        public static WKTPlatformRef Lerp(in WKTPlatformRef from, in WKTPlatformRef to, float t) {
            if (from.IsSet && from.IsSameFrame(to))
                return new WKTPlatformRef(to.LevelKey, to.Depth, to.PathLow, to.PathHigh, to.NameHash, from.Local + (to.Local - from.Local) * t);
            return t >= 0.5f ? to : from;
        }

        public void Write(NetDataWriter writer) {
            writer.Put(LevelKey);
            writer.Put(Depth);
            for (var level = 0; level < Depth; level++)
                writer.Put((ushort)GetIndex(level));
            writer.Put(NameHash);
            WKTWire.Put(writer, Local);
        }

        /// <summary>Fails on truncation, level key 0, a path deeper than <see cref="MAX_DEPTH"/> and a bad position.</summary>
        public static bool TryRead(NetDataReader reader, out WKTPlatformRef platform) {
            platform = default;
            if (!reader.TryGetULong(out var levelKey) || levelKey == 0) return false;
            if (!reader.TryGetByte(out var depth) || depth > MAX_DEPTH) return false;
            var pathLow = 0UL;
            var pathHigh = 0UL;
            for (var level = 0; level < depth; level++) {
                if (!reader.TryGetUShort(out var index)) return false;
                SetIndex(ref pathLow, ref pathHigh, level, index);
            }
            if (!reader.TryGetUInt(out var nameHash) || !WKTWire.TryGetPosition(reader, out var local)) return false;
            platform = new WKTPlatformRef(levelKey, depth, pathLow, pathHigh, nameHash, local);
            return true;
        }

        public bool Equals(WKTPlatformRef other) => IsSameFrame(other) && Local.Equals(other.Local);

        public override bool Equals(object? obj) => obj is WKTPlatformRef other && Equals(other);

        public override int GetHashCode() {
            var hash = (LevelKey.GetHashCode() * 397 ^ Depth) * 397 ^ PathLow.GetHashCode();
            return ((hash * 397 ^ PathHigh.GetHashCode()) * 397 ^ (int)NameHash) * 397 ^ Local.GetHashCode();
        }

        public override string ToString() => IsSet ? $"on {LevelKey:X16}/{Depth} at ({Local.x:F2}, {Local.y:F2}, {Local.z:F2})" : "on nothing";
    }
}
