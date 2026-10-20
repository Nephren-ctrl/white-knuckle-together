using HarmonyLib;
using JetBrains.Annotations;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// The player's low health effects (the hurt screen effect, the damaged and dying sounds) follow their health every
    /// frame, and a death from damage leaves it at nothing, so a spectating player would see and hear them at full strength
    /// until they come back. While the local player is dead in a multiplayer run, the effects read full health instead; the
    /// health itself, which others are sent, is left as it is.
    /// </summary>
    [HarmonyPatch(typeof(ENT_Player), "LateUpdate")]
    internal sealed class DeadPlayerEffects_Patch {
        [UsedImplicitly]
        private static void Prefix(ENT_Player __instance, out float? __state) {
            __state = null;
            if (WKTPlayerDeaths.Sync is not { IsLocalDead: true } || __instance != ENT_Player.playerObject) return;
            __state = __instance.health;
            __instance.health = __instance.maxHealth;
        }

        [UsedImplicitly]
        private static void Postfix(ENT_Player __instance, float? __state) {
            if (__state.HasValue)
                __instance.health = __state.Value;
        }
    }

    /// <summary>
    /// <c>FixedUpdate</c> also aims the hurt vignette at the lost health every step, so a spectating player's screen turned
    /// red again after their death cleared it. While the local player is dead in a multiplayer run, it aims at none.
    /// </summary>
    [HarmonyPatch(typeof(ENT_Player), nameof(ENT_Player.FixedUpdate))]
    internal sealed class DeadPlayerHurtVignette_Patch {
        private const string c_hurt_vignette = "hurt";

        [UsedImplicitly]
        private static void Postfix(ENT_Player __instance) {
            if (WKTPlayerDeaths.Sync is not { IsLocalDead: true } || __instance != ENT_Player.playerObject || CL_UIManager.instance == null) return;
            CL_UIManager.instance.SetVignetteTarget(c_hurt_vignette, 0f);
        }
    }
}
