using HarmonyLib;
using JetBrains.Annotations;
using UnityEngine;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// A seamless teleporter (the Rotten Duct's and Body Cavity's looping rooms) runs on the machine of the player who set it
    /// off. In a synced co-op run each machine moves what it decides: a client moves its own player, whose held items go
    /// along, and what it simulates, and has the host move the host's objects in the box. The host moves everything in range
    /// but the stand-ins of remote players, whose own machines move them, and only its own player when it went through itself.
    /// </summary>
    [HarmonyPatch(typeof(UT_SeamlessTeleporter), nameof(UT_SeamlessTeleporter.InstantTeleport))]
    internal sealed class SeamlessTeleporter_Patch {
        [UsedImplicitly]
        private static bool Prefix(UT_SeamlessTeleporter __instance) {
            var sync = WKTWorldSync.Instance;
            if (sync == null || __instance.destinationTransform == null) return true;
            Move(__instance, sync, false);
            sync.OnLocalSeamlessTeleport(__instance);
            return false;
        }

        /// <summary>Moves what this machine decides through <paramref name="teleporter"/>, for a remote player's pass or the local player's.</summary>
        public static void Move(UT_SeamlessTeleporter teleporter, WKTWorldSync sync, bool forRemote) {
            var source = teleporter.transform;
            var destination = teleporter.destinationTransform;
            foreach (var entity in GameEntity.entities) {
                if (entity == null || !entity.gameObject.activeSelf || entity is WKTPlayerProxy || !teleporter.IsPositionInBounds(entity.transform.position)) continue;
                if (entity is ENT_Player ? forRemote : !sync.IsHost && !sync.SimulatesHere(entity)) continue;
                var position = destination.TransformPoint(source.InverseTransformPoint(entity.transform.position));
                var rotation = source.rotation * Quaternion.Inverse(destination.rotation) * entity.transform.rotation;
                entity.Teleport(position, rotation, keepVelocity: true);
            }
        }
    }
}
