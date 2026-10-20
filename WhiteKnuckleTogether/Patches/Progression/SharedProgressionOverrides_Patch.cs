using HarmonyLib;
using JetBrains.Annotations;

// Restricts what the game considers unlocked to the session's shared progression while WKTSharedProgressionOverride is
// active. Each patch does nothing otherwise, so singleplayer is untouched. All are critical: without them a player
// could see content another player has not unlocked, and worlds could generate differently from the same seed.
// Competitive runs are not exempt: the shared set applies uniformly, which keeps every machine identical.
namespace WhiteKnuckleTogether {
    /// <summary>
    /// Re-evaluates an unlock from the player's real requirements. Replaced by the shared state, which also keeps the
    /// unlock from being earned or announced during the run.
    /// </summary>
    [WKTCriticalPatch(WKTPatchCategory.ProgressionOverride), HarmonyPatch(typeof(ProgressionUnlock), nameof(ProgressionUnlock.CheckUnlock))]
    internal sealed class ProgressionUnlockCheckUnlock_Patch {
        [UsedImplicitly]
        private static bool Prefix(ProgressionUnlock __instance, ref bool __result) {
            if (!WKTSharedProgressionOverride.IsActive) return true;
            var state = WKTSharedProgressionOverride.IsUnlockShared(__instance.id);
            __instance.state = state;
            // The original mirrors every evaluation into a game flag that level objects read.
            if (CL_GameManager.gMan != null)
                CL_GameManager.SetGameFlag("unlock_" + __instance.name, state);
            __result = state;
            return false;
        }
    }

    /// <summary>Read by spawn tables, items, perks, and cosmetics. Spawn tables make it part of world generation.</summary>
    [WKTCriticalPatch(WKTPatchCategory.ProgressionOverride), HarmonyPatch(typeof(CL_ProgressionManager), nameof(CL_ProgressionManager.HasProgressionUnlock))]
    internal sealed class ProgressionManagerHasProgressionUnlock_Patch {
        [UsedImplicitly]
        private static void Postfix(string unlockName, ref bool __result) =>
            __result = WKTSharedProgressionOverride.FilterUnlock(unlockName, __result);
    }

    /// <summary>Gates gamemodes, menu capsules, and achievement requirements.</summary>
    [WKTCriticalPatch(WKTPatchCategory.ProgressionOverride), HarmonyPatch(typeof(CL_AchievementManager), nameof(CL_AchievementManager.GetAchievementValue))]
    internal sealed class AchievementManagerGetAchievementValue_Patch {
        [UsedImplicitly]
        private static void Postfix(string name, ref bool __result) =>
            __result = WKTSharedProgressionOverride.FilterAchievement(name, __result);
    }

    /// <summary>
    /// Every scene's StatManager reloads the save from disk on Awake. Filtering right after each load means every reader
    /// of save flags and facility upgrades sees only shared progression, however and whenever it reads them.
    /// </summary>
    [WKTCriticalPatch(WKTPatchCategory.ProgressionOverride), HarmonyPatch(typeof(StatManager), nameof(StatManager.LoadStats))]
    internal sealed class StatManagerLoadStats_Patch {
        [UsedImplicitly]
        private static void Postfix(StatManager __instance) {
            if (!WKTSharedProgressionOverride.IsActive) return;
            // Back at the main menu the run is over. Stop overriding before the menu reads the freshly loaded save, so
            // the player's own progression shows again. The lock itself is released on rollback once the menu loaded.
            if (__instance.gameObject.scene.name == WKTSceneConsts.MAIN_MENU) {
                WKTSharedProgressionOverride.Deactivate();
                return;
            }
            if (StatManager.saveData != null)
                WKTSharedProgressionOverride.FilterSaveData(StatManager.saveData);
        }
    }

    /// <summary>
    /// The console can turn on unlockAll, which spawn tables and flag checks read directly. It would reveal everything,
    /// so it is kept off while the override is active. The game itself resets it on every scene load.
    /// </summary>
    [WKTCriticalPatch(WKTPatchCategory.ProgressionOverride), HarmonyPatch(typeof(SettingsManager), nameof(SettingsManager.SetSetting))]
    internal sealed class SettingsManagerSetSetting_Patch {
        [UsedImplicitly]
        private static void Postfix() {
            if (WKTSharedProgressionOverride.IsActive && SettingsManager.settings != null)
                SettingsManager.settings.unlockAll = false;
        }
    }
}
