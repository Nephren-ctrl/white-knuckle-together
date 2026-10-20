using UnityEngine;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// Packs an offset from an object into a short per axis, in steps of <see cref="STEP"/> metres: about 512 m either way at
    /// a 1.6 cm resolution. Further offsets clamp. Pure, so it can be tested without Unity's runtime.
    /// </summary>
    internal static class WKTOffsetCodec {
        public const float STEP = 1f / 64f;

        public static short Pack(float value) => (short)Mathf.Clamp(Mathf.RoundToInt(value / STEP), short.MinValue, short.MaxValue);

        public static float Unpack(short value) => value * STEP;
    }
}
