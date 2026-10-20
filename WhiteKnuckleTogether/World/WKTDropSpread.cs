using UnityEngine;

namespace WhiteKnuckleTogether {
    /// <summary>Spreads several items dropped at once around one spot on a golden-angle spiral, instead of piling them up.</summary>
    internal static class WKTDropSpread {
        public const float RADIUS = 0.7f;
        private const float c_golden_angle = 137.5f;
        private const float c_lift = 0.4f;

        /// <summary>Where item <paramref name="index"/> of <paramref name="count"/> goes, from the spot: horizontally within <see cref="RADIUS"/>, and a little up.</summary>
        public static Vector3 Offset(int index, int count) {
            var angle = index * c_golden_angle * Mathf.Deg2Rad;
            var radius = RADIUS * Mathf.Sqrt((index + 1f) / Mathf.Max(count, index + 1));
            return new Vector3(Mathf.Cos(angle), c_lift, Mathf.Sin(angle)) * radius;
        }
    }
}
