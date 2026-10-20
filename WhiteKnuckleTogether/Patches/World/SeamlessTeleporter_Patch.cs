using HarmonyLib;
using JetBrains.Annotations;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// A seamless teleporter (the Rotten Duct's and Body Cavity's looping rooms) runs on the machine of the player who set it
    /// off. In a synced co-op run it moves only what that machine decides: a client moves its own player, whose held items
    /// go along, and leaves the host's world objects to the host. The host moves everything in range but the stand-ins of
    /// remote players, whose own machines move them.
    /// </summary>
    [HarmonyPatch(typeof(UT_SeamlessTeleporter), nameof(UT_SeamlessTeleporter.InstantTeleport))]
    internal sealed class SeamlessTeleporter_Patch {
        [UsedImplicitly]
        private static bool Prefix(UT_SeamlessTeleporter __instance) {
            var sync = WKTWorldSync.Instance;
            if (sync == null || __instance.destinationTransform == null) return true;
            var source = __instance.transform;
            var destination = __instance.destinationTransform;
            foreach (var entity in GameEntity.entities) {
                if (entity == null || !entity.gameObject.activeSelf || entity is WKTPlayerProxy || !__instance.IsPositionInBounds(entity.transform.position)) continue;
                if (!sync.IsHost && entity is not ENT_Player) continue;
                var position = destination.TransformPoint(source.InverseTransformPoint(entity.transform.position));
                var rotation = source.rotation * UnityEngine.Quaternion.Inverse(destination.rotation) * entity.transform.rotation;
                entity.Teleport(position, rotation, keepVelocity: true);
            }
            return false;
        }
    }
}
