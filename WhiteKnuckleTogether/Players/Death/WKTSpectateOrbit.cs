using UnityEngine;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// The math of the third person spectate camera, which orbits the spectated player's head. Pure: the sphere cast that
    /// finds geometry in the way comes from the caller.
    /// </summary>
    internal static class WKTSpectateOrbit {
        /// <summary>
        /// Turns the orbit by a frame's look input, the same way round as the game's own camera: up looks up, unless the
        /// player inverted it.
        /// </summary>
        public static void ApplyLook(ref float yaw, ref float pitch, Vector2 look, bool invertY) {
            yaw = Mathf.Repeat(yaw + look.x, 360f);
            pitch = ClampPitch(pitch + (invertY ? look.y : -look.y));
        }

        public static float ClampPitch(float pitch) => Mathf.Clamp(pitch, WKTDeathConsts.MIN_PITCH, WKTDeathConsts.MAX_PITCH);

        /// <summary>The direction the camera looks in, like <c>Quaternion.Euler(pitch, yaw, 0) * Vector3.forward</c>.</summary>
        public static Vector3 Forward(float yaw, float pitch) {
            var yawRadians = yaw * Mathf.Deg2Rad;
            var pitchRadians = pitch * Mathf.Deg2Rad;
            var horizontal = Mathf.Cos(pitchRadians);
            return new Vector3(Mathf.Sin(yawRadians) * horizontal, -Mathf.Sin(pitchRadians), Mathf.Cos(yawRadians) * horizontal);
        }

        /// <summary>
        /// How far behind the head the camera may sit, given where a cast backwards from the head first hit geometry.
        /// Negative <paramref name="hitDistance"/> means nothing was hit.
        /// </summary>
        public static float GetAllowedDistance(float hitDistance) {
            if (hitDistance < 0f) return WKTDeathConsts.ORBIT_DISTANCE;
            return Mathf.Clamp(hitDistance - WKTDeathConsts.CAMERA_SKIN, WKTDeathConsts.MIN_ORBIT_DISTANCE,
                WKTDeathConsts.ORBIT_DISTANCE);
        }

        /// <summary>
        /// The camera's next distance. It snaps in at once when geometry is closer, so it never shows the inside of a wall,
        /// and moves back out smoothly.
        /// </summary>
        public static float StepDistance(float current, float allowed, float deltaTime) {
            if (allowed <= current) return allowed;
            return Mathf.MoveTowards(current, allowed, WKTDeathConsts.ORBIT_RETURN_SPEED * deltaTime);
        }
    }
}
