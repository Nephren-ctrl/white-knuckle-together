using HarmonyLib;
using JetBrains.Annotations;
using Pathfinding;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// The host notes which players its Teeth find no path to, so they go for someone they can reach (see
    /// <see cref="WKTTeethReach"/>). Singleplayer is untouched.
    /// </summary>
    [HarmonyPatch(typeof(DEN_Teeth), nameof(DEN_Teeth.OnPathComplete))]
    internal sealed class TeethReach_Patch {
        [UsedImplicitly]
        private static void Prefix(DEN_Teeth __instance, Path p) {
            if (!WKTPlayerPositions.IsCoveringAllPlayers || p is not ABPath path) return;
            WKTTeethReach.OnPathComplete(__instance, p.error, path.originalEndPoint, path.endPoint);
        }
    }
}
