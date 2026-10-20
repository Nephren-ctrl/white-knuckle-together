using System;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// How far each remote player has fallen since they last stood or hung on something, worked out from their poses, as the
    /// game's <c>ENT_Player.GetCurrentFallDistance</c> is for the local player. Enemy code asking a remote player's fall
    /// distance gets this (<see cref="WKTTargets.GetCurrentFallDistance"/>).
    /// </summary>
    internal static class WKTRemoteFalls {
        private static readonly float[] s_fallStart = new float[byte.MaxValue + 1];
        private static readonly float[] s_distance = new float[byte.MaxValue + 1];
        private static readonly bool[] s_falling = new bool[byte.MaxValue + 1];

        /// <summary>A remote player's latest pose: their height, and whether they stand or hang (supported).</summary>
        public static void Update(byte playerId, float height, bool supported) {
            s_distance[playerId] = Step(ref s_fallStart[playerId], ref s_falling[playerId], height, supported);
        }

        /// <summary>How far a remote player has fallen, 0 while supported.</summary>
        public static float Get(byte playerId) => s_distance[playerId];

        public static void Reset() {
            Array.Clear(s_fallStart, 0, s_fallStart.Length);
            Array.Clear(s_distance, 0, s_distance.Length);
            Array.Clear(s_falling, 0, s_falling.Length);
        }

        /// <summary>
        /// One pose of fall tracking: the fall starts from the highest point since last supported, so rising after a jump
        /// does not count as falling.
        /// </summary>
        public static float Step(ref float fallStart, ref bool falling, float height, bool supported) {
            if (supported) {
                falling = false;
                return 0f;
            }
            if (!falling || height > fallStart) {
                falling = true;
                fallStart = height;
            }
            return fallStart - height;
        }
    }
}
