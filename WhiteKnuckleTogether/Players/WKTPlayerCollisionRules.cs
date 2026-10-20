using UnityEngine;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// The maths of players bumping into each other (<see cref="WKTPlayerCollision"/>), kept apart from Unity so it can be
    /// tested. Bodies are upright cylinders; only sideways overlap is undone, so nobody is ever lifted or pressed down.
    /// </summary>
    internal static class WKTPlayerCollisionRules {
        /// <summary>Soft: the share of the overlap undone each second, as an exponential rate.</summary>
        public const float SOFT_RATE = 6f;
        /// <summary>Soft: the fastest two players drift apart, in m/s, so a deep overlap eases out rather than shoves.</summary>
        public const float SOFT_MAX_SPEED = 2.5f;

        /// <summary>
        /// Whether the local body overlaps the other, and if so the sideways direction (unit, flat) that takes the local body
        /// out and how far. Bodies standing on the same spot part along a direction both machines agree on from the player
        /// ids, so they go opposite ways.
        /// </summary>
        public static bool TryGetOverlap(Vector3 localCenter, float localBottom, float localTop, float localRadius, Vector3 otherCenter,
            float otherBottom, float otherTop, float otherRadius, byte localId, byte otherId, out Vector3 normal, out float depth) {
            normal = Vector3.zero;
            depth = 0f;
            if (localTop <= otherBottom || otherTop <= localBottom) return false;
            var offset = new Vector3(localCenter.x - otherCenter.x, 0f, localCenter.z - otherCenter.z);
            var distance = offset.magnitude;
            var reach = localRadius + otherRadius;
            if (distance >= reach) return false;
            depth = reach - distance;
            normal = distance > 1e-4f ? offset / distance : localId < otherId ? Vector3.right : Vector3.left;
            return true;
        }

        /// <summary>How far to move the local body this step to undo an overlap of <paramref name="depth"/> along <paramref name="normal"/>.</summary>
        public static Vector3 GetStep(PlayerCollisionId mode, Vector3 normal, float depth, float deltaTime) {
            switch (mode) {
                case PlayerCollisionId.Soft:
                    var eased = depth * (1f - Mathf.Exp(-SOFT_RATE * deltaTime));
                    return normal * Mathf.Min(eased, SOFT_MAX_SPEED * deltaTime);
                case PlayerCollisionId.Hard:
                    return normal * depth;
                default:
                    return Vector3.zero;
            }
        }

        /// <summary>Hard: <paramref name="velocity"/> without the part heading into the other body, so walking into them stops.</summary>
        public static Vector3 RemoveInward(Vector3 velocity, Vector3 normal) {
            var into = Vector3.Dot(velocity, normal);
            return into < 0f ? velocity - normal * into : velocity;
        }
    }
}
