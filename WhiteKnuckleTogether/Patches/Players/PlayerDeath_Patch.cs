using System;
using HarmonyLib;
using JetBrains.Annotations;

// A death in a multiplayer run only marks the player dead, and they spectate until everyone is. See WKTPlayerDeaths.
namespace WhiteKnuckleTogether {
    /// <summary>
    /// The game's death stops time for the whole machine, fades out and ends the run, or revives from a save. In a
    /// multiplayer run the world goes on for everyone else, the host's simulation included. Critical: without it, the
    /// host's death would freeze every player's world.
    /// </summary>
    [WKTCriticalPatch(WKTPatchCategory.RunSync), HarmonyPatch(typeof(CL_GameManager), nameof(CL_GameManager.Die))]
    internal sealed class GameManagerDie_Patch {
        [UsedImplicitly]
        private static bool Prefix(CL_GameManager __instance, string type) {
            if (WKT.InstanceOrNull == null || !WKT.InstanceOrNull.Network.IsMultiplayerRun) return true;
            WKTPlayerDeaths.HandleLocalDeath(__instance, type ?? string.Empty);
            return false;
        }
    }

    /// <summary>
    /// The score screen shows once every player is dead, long after this player saw "You Died", so it opens straight on
    /// the stats. Only in a multiplayer run, and without changing the setting for good.
    /// </summary>
    [HarmonyPatch(typeof(UI_ScoreScreen), nameof(UI_ScoreScreen.ShowScreen))]
    internal sealed class ScoreScreenShowScreen_Patch {
        [UsedImplicitly]
        private static void Prefix(out bool? __state) {
            __state = null;
            var settings = SettingsManager.settings;
            if (settings == null || WKT.InstanceOrNull == null || !WKT.InstanceOrNull.Network.IsMultiplayerRun) return;
            __state = settings.g_skipDeathPopup;
            settings.g_skipDeathPopup = true;
        }

        /// <summary>The popup's setting is read before the screen's animation first waits, so it can be restored right away.</summary>
        [UsedImplicitly]
        private static Exception? Finalizer(Exception? __exception, bool? __state) {
            if (__state.HasValue && SettingsManager.settings != null)
                SettingsManager.settings.g_skipDeathPopup = __state.Value;
            return __exception;
        }
    }

    /// <summary>
    /// Reloading the scene on one machine would leave the shared run. A multiplayer run restarts only through its host's
    /// score screen, for everyone. Only reached from debug keys and console commands.
    /// </summary>
    [HarmonyPatch(typeof(CL_GameManager), nameof(CL_GameManager.RestartScene))]
    internal sealed class GameManagerRestartScene_Patch {
        [UsedImplicitly]
        private static bool Prefix() {
            if (WKT.InstanceOrNull == null || !WKT.InstanceOrNull.Network.IsMultiplayerRun) return true;
            WKT.InstanceOrNull.Logger.LogInfo("Blocked a local restart of a multiplayer run.");
            return false;
        }
    }

    /// <summary>
    /// A computer takes over the camera of whoever uses it. A dead player spectates through that camera, so the computer
    /// never opens for them, whatever reaches it.
    /// </summary>
    [HarmonyPatch(typeof(OS_Computer_Interface), nameof(OS_Computer_Interface.ActivateComputer))]
    internal sealed class ComputerActivate_Patch {
        [UsedImplicitly]
        private static bool Prefix() => WKTPlayerDeaths.Sync is not { IsLocalDead: true };
    }
}
