using HarmonyLib;
using JetBrains.Annotations;

// The pause menu opens without stopping time in a multiplayer run. See WKTMultiplayerPause. Not critical: without them,
// pausing only stops time for the player who paused, as in singleplayer.
namespace WhiteKnuckleTogether {
    /// <summary>Every pause: the pause key, the bug reporter and level scripts.</summary>
    [HarmonyPatch(typeof(CL_GameManager), nameof(CL_GameManager.Pause))]
    internal sealed class GameManagerPause_Patch {
        [UsedImplicitly]
        private static bool Prefix(CL_GameManager __instance) => !WKTMultiplayerPause.TryOpen(__instance);
    }

    /// <summary>The game's own unpause also closes the menu opened in a multiplayer run, and changes nothing else there.</summary>
    [HarmonyPatch(typeof(CL_GameManager), nameof(CL_GameManager.UnPause))]
    internal sealed class GameManagerUnPause_Patch {
        [UsedImplicitly]
        private static void Postfix() => WKTMultiplayerPause.OnClosed();
    }

    [HarmonyPatch(typeof(InputManager), nameof(InputManager.UpdateInputs))]
    internal sealed class InputManagerUpdateInputs_Patch {
        [UsedImplicitly]
        private static void Postfix(InputManager __instance) {
            // First, so the spectator reads the input before anything clears it.
            WKTDeadPlayerInput.Filter(__instance);
            WKTMultiplayerPause.FilterInput(__instance);
        }
    }
}
