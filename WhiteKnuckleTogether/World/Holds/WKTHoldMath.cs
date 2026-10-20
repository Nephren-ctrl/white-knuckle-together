using UnityEngine;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// The game's own math for a player holding a prop (<c>ENT_Player.GrabPropUpdate</c>) and steering a ridden flier
    /// (<c>HandholdModule_FlierSteering.LateUpdate</c>), taken apart from the player it reads, so the host can apply it for
    /// remote players and blend several. Pure, so it can be tested without Unity objects.
    /// </summary>
    internal static class WKTHoldMath {
        /// <summary>How hard a held prop's anchor is pulled toward the hold point, per second.</summary>
        public const float ANCHOR_FORCE = 200f;
        /// <summary>How much of the prop's centre the pulled point leans to, so it does not spin around its anchor.</summary>
        public const float CENTRE_LEAN = 0.01f;
        /// <summary>The game scales the pull by this over the prop's mass.</summary>
        public const float PULL_SCALE = 8f;
        /// <summary>The fastest several holders' pulls together may move a prop, so a crowd cannot fling it.</summary>
        public const float MAX_SHARED_SPEED = 30f;

        /// <summary>
        /// The pull toward a holder's hold point: the velocity the game gives a held prop, and the force it adds at the anchor.
        /// <paramref name="holdOffset"/> is the prop's hold offset already turned by the holder's view.
        /// </summary>
        public static Vector3 PropVelocity(Vector3 eye, Vector3 forward, float holdDistance, Vector3 holdOffset, Vector3 anchorWorld, Vector3 propPosition, float mass,
                                           float deltaTime, out Vector3 anchorForce, out Vector3 pull) {
            var goal = eye + forward * holdDistance;
            var leaned = Vector3.Lerp(anchorWorld, propPosition, CENTRE_LEAN);
            pull = goal - leaned + holdOffset;
            anchorForce = ANCHOR_FORCE * deltaTime * (goal - anchorWorld);
            return pull / mass * PULL_SCALE + Vector3.down * mass;
        }

        /// <summary>The velocity one holder's <paramref name="pull"/> (see <see cref="PropVelocity"/>) adds to a prop, without its weight.</summary>
        public static Vector3 PullVelocity(Vector3 pull, float mass) => pull / mass * PULL_SCALE;

        /// <summary>
        /// A prop's velocity with every holder's pull added together: <paramref name="pullVelocities"/> (see
        /// <see cref="PullVelocity"/>) plus its weight once, as the game moves a prop one player holds. Pulls the same way add
        /// up, opposite ones cancel. No faster than <see cref="MAX_SHARED_SPEED"/>.
        /// </summary>
        public static Vector3 SharedVelocity(Vector3 pullVelocities, float mass) =>
            Vector3.ClampMagnitude(pullVelocities + Vector3.down * mass, MAX_SHARED_SPEED);

        /// <summary>
        /// The force pulling a player toward a prop they hold, with its lift taken out while it is not
        /// <paramref name="anchored"/>: a prop another player carries cannot carry them too.
        /// </summary>
        public static Vector3 HangForce(Vector3 force, bool anchored) {
            if (!anchored && force.y > 0f)
                force.y = 0f;
            return force;
        }

        /// <summary>Where one rider steers a flier, as the game computes it from their camera and move input.</summary>
        public static Vector3 SteerTarget(in SteerSettings settings, Vector3 cameraPosition, Vector3 cameraForward, Vector3 cameraRight, Vector2 move) {
            var steer = Vector3.ClampMagnitude(cameraForward * move.y + cameraRight * move.x, 1f);
            steer.x *= settings.HorizontalForce;
            steer.z *= settings.HorizontalForce;
            steer.y *= steer.y > 0f ? settings.UpwardsForce : settings.DownwardsForce;
            steer += -Vector3.up * settings.Gravity;
            var ahead = Vector3.Scale(cameraForward * settings.PlacementDistance, new Vector3(1f, 0.5f, 1f)).normalized;
            var placement = cameraPosition + (ahead + Vector3.up * settings.PlacementHeight);
            return placement + steer * settings.ForceMult;
        }

        /// <summary>A camera's right from its forward, as a camera that does not roll has it.</summary>
        public static Vector3 RightOf(Vector3 forward) {
            var right = Vector3.Cross(Vector3.up, forward);
            return right.sqrMagnitude > 1e-8f ? right.normalized : Vector3.right;
        }

        /// <summary>A flier's steering tuning, from its <c>HandholdModule_FlierSteering</c>.</summary>
        public readonly struct SteerSettings {
            public readonly float HorizontalForce;
            public readonly float DownwardsForce;
            public readonly float UpwardsForce;
            public readonly float Gravity;
            public readonly float PlacementDistance;
            public readonly float PlacementHeight;
            public readonly float ForceMult;

            public SteerSettings(float horizontalForce, float downwardsForce, float upwardsForce, float gravity, float placementDistance, float placementHeight,
                                 float forceMult) {
                HorizontalForce = horizontalForce;
                DownwardsForce = downwardsForce;
                UpwardsForce = upwardsForce;
                Gravity = gravity;
                PlacementDistance = placementDistance;
                PlacementHeight = placementHeight;
                ForceMult = forceMult;
            }
        }
    }
}
