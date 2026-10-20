using HarmonyLib;
using UnityEngine;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// Players bump into each other (<see cref="PlayerCollisionId"/>, the host's lobby choice). Each machine only moves its own
    /// player out of the others' bodies, through the game's character controller so walls still stop it, before the game
    /// moves the player each physics step: nothing extra is sent. Soft eases an overlap out over a moment; hard undoes it at
    /// once and stops walking into the other. Climbers too: a hanging player's body has slack around its grip, which the
    /// game only pulls back past, and a push is far smaller than that. Never in an instanced run, where the others are ghosts
    /// from other copies of the world. Allocates nothing.
    /// </summary>
    internal static class WKTPlayerCollision {
        private static readonly AccessTools.FieldRef<ENT_Player, Vector3> s_velocity = AccessTools.FieldRefAccess<ENT_Player, Vector3>("vel");

        /// <summary>Call before the local player's physics step.</summary>
        public static void Step(ENT_Player player) {
            var wkt = WKT.InstanceOrNull;
            if (wkt == null) return;
            var network = wkt.Network;
            var session = network.Session;
            if (!session.IsSyncedWorld) return;
            var mode = session.PlayerCollision;
            var sync = WKTPlayerSync.Instance;
            var local = network.LocalPlayer;
            if (mode == PlayerCollisionId.None || sync == null || local == null) return;
            var controller = player.cCon;
            if (controller == null || !controller.enabled || player.dead || player.noclip || player.IsMoveLocked() || player.isFrozen()) return;
            var playerTransform = player.transform;
            var scale = playerTransform.lossyScale.y;
            var center = playerTransform.TransformPoint(controller.center);
            var halfHeight = controller.height * 0.5f * scale;
            // As thin as other players' bodies are drawn, so bodies touch where they look like they do.
            var radius = controller.radius * scale * WKTRemotePlayer.BODY_WIDTH_SCALE;
            var deltaTime = Time.fixedDeltaTime;
            var push = Vector3.zero;
            var velocity = s_velocity(player);
            var hit = false;
            for (var id = 0; id <= byte.MaxValue; id++) {
                var otherId = (byte)id;
                if (otherId == local.Id || !sync.TryGetRemote(otherId, out var remote) ||
                    !remote.TryGetBody(out var otherCenter, out var otherBottom, out var otherTop, out var otherRadius)) continue;
                if (!WKTPlayerCollisionRules.TryGetOverlap(center, center.y - halfHeight, center.y + halfHeight, radius, otherCenter, otherBottom, otherTop,
                        otherRadius, local.Id, otherId, out var normal, out var depth)) continue;
                hit = true;
                push += WKTPlayerCollisionRules.GetStep(mode, normal, depth, deltaTime);
                if (mode == PlayerCollisionId.Hard)
                    velocity = WKTPlayerCollisionRules.RemoveInward(velocity, normal);
            }
            if (!hit) return;
            controller.Move(push);
            if (mode == PlayerCollisionId.Hard)
                s_velocity(player) = velocity;
        }
    }
}
