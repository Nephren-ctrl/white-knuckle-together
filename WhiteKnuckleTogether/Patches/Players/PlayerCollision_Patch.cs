using HarmonyLib;
using JetBrains.Annotations;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// Before the local player's physics step, so the push is part of the step's movement and the game's own velocity and
    /// camera bookkeeping include it (see <see cref="WKTPlayerCollision"/>).
    /// </summary>
    [HarmonyPatch(typeof(ENT_Player), nameof(ENT_Player.FixedUpdate))]
    internal sealed class PlayerCollision_Patch {
        [UsedImplicitly]
        private static void Prefix(ENT_Player __instance) {
            if (__instance == ENT_Player.playerObject)
                WKTPlayerCollision.Step(__instance);
        }
    }
}
