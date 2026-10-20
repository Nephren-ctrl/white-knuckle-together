using UnityEngine;

namespace WhiteKnuckleTogether {
    /// <summary>Closest-point math for rays and segments. Pure, so it can be tested without Unity's native code.</summary>
    internal static class WKTRayMath {
        /// <summary>
        /// The closest points between the ray from <paramref name="origin"/> along the unit <paramref name="direction"/> and the
        /// segment <paramref name="a"/>–<paramref name="b"/>: <paramref name="onSegment"/>, <paramref name="onRay"/>, and how far
        /// along the ray that is (never negative).
        /// </summary>
        public static void ClosestPoints(Vector3 origin, Vector3 direction, Vector3 a, Vector3 b, out float along, out Vector3 onSegment, out Vector3 onRay) {
            var segment = b - a;
            var length = Vector3.Dot(segment, segment);
            var w = origin - a;
            var dDotS = Vector3.Dot(direction, segment);
            var dDotW = Vector3.Dot(direction, w);
            var sDotW = Vector3.Dot(segment, w);
            float t;
            float s;
            if (length < 1e-8f) {
                t = 0f;
                s = Mathf.Max(0f, -dDotW);
            }
            else {
                var denominator = length - dDotS * dDotS;
                t = denominator > 1e-8f ? Mathf.Clamp01((sDotW - dDotS * dDotW) / denominator) : 0f;
                s = Mathf.Max(0f, t * dDotS - dDotW);
                // Re-fit the segment point to the clamped ray point.
                t = Mathf.Clamp01(Vector3.Dot(origin + direction * s - a, segment) / length);
            }
            onSegment = a + segment * t;
            onRay = origin + direction * s;
            along = s;
        }
    }
}
