using System;
using UnityEngine;

namespace WhiteKnuckleTogether {
    /// <summary>Where a replicated world object is, in world-offset-corrected space.</summary>
    public readonly struct WKTEntityPose : IWKTInterpolatable<WKTEntityPose> {
        public Vector3 Position { get; }
        public Quaternion Rotation { get; }

        public WKTEntityPose(Vector3 position, Quaternion rotation) {
            Position = position;
            Rotation = rotation;
        }

        public WKTEntityPose LerpTo(in WKTEntityPose to, float t) => new(Position + (to.Position - Position) * t, Slerp(Rotation, to.Rotation, t));

        /// <summary>Spherical interpolation the short way around, in managed code so it behaves the same everywhere.</summary>
        public static Quaternion Slerp(Quaternion from, Quaternion to, float t) {
            var dot = from.x * to.x + from.y * to.y + from.z * to.z + from.w * to.w;
            if (dot < 0f) {
                dot = -dot;
                to = new Quaternion(-to.x, -to.y, -to.z, -to.w);
            }
            float a, b;
            if (dot > 0.9995f) {
                // Nearly the same rotation: a normalized lerp is exact enough and avoids dividing by almost zero.
                a = 1f - t;
                b = t;
            }
            else {
                var theta = (float)Math.Acos(dot);
                var sin = (float)Math.Sin(theta);
                a = (float)Math.Sin((1f - t) * theta) / sin;
                b = (float)Math.Sin(t * theta) / sin;
            }
            var result = new Quaternion(a * from.x + b * to.x, a * from.y + b * to.y, a * from.z + b * to.z, a * from.w + b * to.w);
            var length = (float)Math.Sqrt(result.x * result.x + result.y * result.y + result.z * result.z + result.w * result.w);
            return length > 0f ? new Quaternion(result.x / length, result.y / length, result.z / length, result.w / length) : to;
        }

        public WKTEntityPose WithPosition(Vector3 position) => new(position, Rotation);
    }
}
