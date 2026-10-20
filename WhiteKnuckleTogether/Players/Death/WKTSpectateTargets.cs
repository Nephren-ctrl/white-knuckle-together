using System;

namespace WhiteKnuckleTogether {
    /// <summary>Which living player a dead player spectates. Players are cycled in ascending id order, wrapping around.</summary>
    internal static class WKTSpectateTargets {
        public const int NONE = -1;

        /// <summary>
        /// The living player after <paramref name="current"/> going forward (<paramref name="direction"/> &gt; 0) or back.
        /// <paramref name="current"/> need not be alive any more: the search starts from its id. <see cref="NONE"/> starts
        /// from the first player going forward, or the last going back. <paramref name="aliveIds"/> must be ascending.
        /// </summary>
        public static int Next(int current, int direction, ReadOnlySpan<byte> aliveIds) {
            if (aliveIds.IsEmpty) return NONE;
            if (direction >= 0) {
                if (current == NONE) return aliveIds[0];
                foreach (var id in aliveIds) {
                    if (id > current) return id;
                }
                return aliveIds[0];
            }
            if (current == NONE) return aliveIds[aliveIds.Length - 1];
            for (var i = aliveIds.Length - 1; i >= 0; i--) {
                if (aliveIds[i] < current) return aliveIds[i];
            }
            return aliveIds[aliveIds.Length - 1];
        }

        /// <summary>Keeps <paramref name="current"/> while it is alive, and otherwise moves on to the next living player.</summary>
        public static int Resolve(int current, ReadOnlySpan<byte> aliveIds) {
            if (current != NONE && aliveIds.IndexOf((byte)current) >= 0) return current;
            return Next(current, 1, aliveIds);
        }
    }
}
