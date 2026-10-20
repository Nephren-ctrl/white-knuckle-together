using UnityEngine;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// The proportions of a remote player's body (see <see cref="WKTRemotePlayer"/>): a cube head at the eyes on a capsule
    /// torso down to the feet. The head turns on a neck below it, so looking down swings it forward and down and looking up
    /// swings it back and down, as a real head nods. Kept apart from Unity objects so it can be tested.
    /// </summary>
    internal static class WKTAvatarShape {
        /// <summary>The head cube's edge, as a fraction of the torso's width.</summary>
        public const float HEAD_SIZE = 0.8f;
        /// <summary>How far below the head's center its neck pivot is, as a fraction of the head's edge: just under the head.</summary>
        public const float NECK_LENGTH = 0.75f;
        /// <summary>
        /// How sharply the body and hands turn red as health and grip run out: the blend toward white is the fraction left
        /// raised to this, so half health already shows mostly red.
        /// </summary>
        public const float SPENT_CURVE = 1.6f;
        private const float c_max_pitch = 90f;

        /// <summary>
        /// How far from red toward the full color a body or hand shows with <paramref name="fraction"/> (0 to 1) of its health
        /// or grip left; red comes on sooner than the fraction alone.
        /// </summary>
        public static float GetSpentBlend(float fraction) => Mathf.Pow(Mathf.Clamp01(fraction), SPENT_CURVE);

        /// <summary>The head cube's edge, for a torso of <paramref name="radius"/>.</summary>
        public static float GetHeadSize(float radius) => radius * 2f * HEAD_SIZE;

        /// <summary>
        /// Where the head's center sits for a look <paramref name="pitch"/> in degrees (positive looking down, like the game's
        /// camera), relative to where it sits looking straight ahead: up (y) and forward (z) in the body's space. It swings
        /// around a neck <see cref="NECK_LENGTH"/> head sizes below it, so it never leaves the neck's reach.
        /// </summary>
        public static Vector2 GetHeadOffset(float pitch, float radius) {
            var neck = GetHeadSize(radius) * NECK_LENGTH;
            var radians = Mathf.Clamp(pitch, -c_max_pitch, c_max_pitch) * Mathf.Deg2Rad;
            return new Vector2(neck * (Mathf.Cos(radians) - 1f), neck * Mathf.Sin(radians));
        }

        /// <summary>
        /// The torso's center and height, from the feet at <paramref name="feet"/> up to the bottom of a head centered on the
        /// eyes at <paramref name="eyes"/> (both heights from the body's pivot). Never shorter than it is wide, so a deep
        /// crouch squashes it into a ball rather than flattening it; it then reaches into the head instead.
        /// </summary>
        public static void GetTorso(float feet, float eyes, float radius, out float center, out float height) {
            var top = eyes - GetHeadSize(radius) * 0.5f;
            height = Mathf.Max(top - feet, radius * 2f);
            center = feet + height * 0.5f;
        }
    }
}
