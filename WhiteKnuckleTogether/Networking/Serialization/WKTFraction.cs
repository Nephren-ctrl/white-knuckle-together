using UnityEngine;

namespace WhiteKnuckleTogether {
    /// <summary>A fraction in [0, 1] quantized to one byte, so <see cref="MAX"/> is 1. Used for compact pose values.</summary>
    public static class WKTFraction {
        public const byte MAX = byte.MaxValue;

        /// <summary>Out-of-range fractions are clamped to [0, 1], and NaN maps to 0.</summary>
        public static byte Quantize(float fraction) {
            if (float.IsNaN(fraction)) return 0;
            return (byte)Mathf.RoundToInt(Mathf.Clamp01(fraction) * MAX);
        }

        public static float ToFloat(byte value) => value / (float)MAX;

        /// <summary><paramref name="t"/> is not clamped, but the result never leaves the byte range.</summary>
        public static byte Lerp(byte from, byte to, float t) =>
            (byte)Mathf.Clamp(Mathf.RoundToInt(from + (to - from) * t), 0, MAX);
    }
}
