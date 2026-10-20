using System;
using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;
using JetBrains.Annotations;

// Every method that persists or awards achievements, meta-progression, unlocks, stats, saves, or leaderboard scores.
// Each prefix skips the original while WKTProgressionLock is engaged. All are critical: if one fails to apply,
// multiplayer is disabled. When adding a sink, also add it to ProgressionLockTests.ExpectedSinks.
namespace WhiteKnuckleTogether {
    /// <summary>Writes wk_save.json: stats, roach banks, gamemode stats, flags, upgrades. Also runs on scene unload and quit.</summary>
    [WKTCriticalPatch(WKTPatchCategory.ProgressionSink), HarmonyPatch(typeof(StatManager), nameof(StatManager.SaveStats))]
    internal sealed class StatManagerSaveStats_Patch {
        [UsedImplicitly]
        private static bool Prefix() => WKTProgressionLock.ShouldRunOriginal("StatManager.SaveStats");
    }

    /// <summary>Console command that deletes the save file.</summary>
    [WKTCriticalPatch(WKTPatchCategory.ProgressionSink), HarmonyPatch(typeof(StatManager), nameof(StatManager.EraseStats), new Type[0])]
    internal sealed class StatManagerEraseStats_Patch {
        [UsedImplicitly]
        private static bool Prefix() => WKTProgressionLock.ShouldRunOriginal("StatManager.EraseStats()");
    }

    /// <summary>Console command overload that deletes the save file.</summary>
    [WKTCriticalPatch(WKTPatchCategory.ProgressionSink), HarmonyPatch(typeof(StatManager), nameof(StatManager.EraseStats), new[] { typeof(string[]) })]
    internal sealed class StatManagerEraseStatsArgs_Patch {
        [UsedImplicitly]
        private static bool Prefix() => WKTProgressionLock.ShouldRunOriginal("StatManager.EraseStats(string[])");
    }

    /// <summary>Console command that wipes facility upgrades.</summary>
    [WKTCriticalPatch(WKTPatchCategory.ProgressionSink), HarmonyPatch(typeof(StatManager), nameof(StatManager.EraseFacilityData))]
    internal sealed class StatManagerEraseFacilityData_Patch {
        [UsedImplicitly]
        private static bool Prefix() => WKTProgressionLock.ShouldRunOriginal("StatManager.EraseFacilityData");
    }

    /// <summary>Copies achievement flags into the save data.</summary>
    [WKTCriticalPatch(WKTPatchCategory.ProgressionSink), HarmonyPatch(typeof(CL_AchievementManager), nameof(CL_AchievementManager.SaveAchievements))]
    internal sealed class AchievementManagerSaveAchievements_Patch {
        [UsedImplicitly]
        private static bool Prefix() => WKTProgressionLock.ShouldRunOriginal("CL_AchievementManager.SaveAchievements");
    }

    /// <summary>Console command that clears local achievement flags and saves.</summary>
    [WKTCriticalPatch(WKTPatchCategory.ProgressionSink), HarmonyPatch(typeof(CL_AchievementManager), nameof(CL_AchievementManager.ResetAchievements))]
    internal sealed class AchievementManagerResetAchievements_Patch {
        [UsedImplicitly]
        private static bool Prefix() => WKTProgressionLock.ShouldRunOriginal("CL_AchievementManager.ResetAchievements");
    }

    /// <summary>Console command that resets Steam achievements.</summary>
    [WKTCriticalPatch(WKTPatchCategory.ProgressionSink), HarmonyPatch(typeof(CL_AchievementManager), nameof(CL_AchievementManager.ResetSteamAchievements))]
    internal sealed class AchievementManagerResetSteamAchievements_Patch {
        [UsedImplicitly]
        private static bool Prefix() => WKTProgressionLock.ShouldRunOriginal("CL_AchievementManager.ResetSteamAchievements");
    }

    /// <summary>Awards an achievement: experience, unlock popups, and the Steam achievement.</summary>
    [WKTCriticalPatch(WKTPatchCategory.ProgressionSink), HarmonyPatch(typeof(CL_AchievementManager.GameAchievement), nameof(CL_AchievementManager.GameAchievement.SetState))]
    internal sealed class GameAchievementSetState_Patch {
        [UsedImplicitly]
        private static bool Prefix() => WKTProgressionLock.ShouldRunOriginal("CL_AchievementManager.GameAchievement.SetState");
    }

    /// <summary>Queues a Steam achievement.</summary>
    [WKTCriticalPatch(WKTPatchCategory.ProgressionSink), HarmonyPatch(typeof(SteamManager), nameof(SteamManager.GiveSteamAchievement))]
    internal sealed class SteamManagerGiveSteamAchievement_Patch {
        [UsedImplicitly]
        private static bool Prefix() => WKTProgressionLock.ShouldRunOriginal("SteamManager.GiveSteamAchievement");
    }

    /// <summary>The leaderboard API's score submission, which also caches submissions to disk.</summary>
    [WKTCriticalPatch(WKTPatchCategory.ProgressionSink), HarmonyPatch]
    internal sealed class SteamLeaderboardApiPlaceScore_Patch {
        [UsedImplicitly]
        private static MethodBase TargetMethod() =>
            AccessTools.Method(AccessTools.Inner(typeof(SteamManager), "SteamLeaderboardApi"), "PlaceScore");

        [UsedImplicitly]
        private static bool Prefix(ref Task<(bool scoreChanged, Task detailsUploadFinished)> __result) {
            if (WKTProgressionLock.ShouldRunOriginal("SteamManager.SteamLeaderboardApi.PlaceScore")) return true;
            // Callers await the result, so it must be a completed task rather than null.
            __result = Task.FromResult((false, Task.CompletedTask));
            return false;
        }
    }

    /// <summary>Records a score on the local offline leaderboard file.</summary>
    [WKTCriticalPatch(WKTPatchCategory.ProgressionSink), HarmonyPatch(typeof(CL_Leaderboard.OfflineLeaderboard), "SubmitOfflineScore")]
    internal sealed class OfflineLeaderboardSubmitOfflineScore_Patch {
        [UsedImplicitly]
        private static bool Prefix() => WKTProgressionLock.ShouldRunOriginal("CL_Leaderboard.OfflineLeaderboard.SubmitOfflineScore");
    }

    /// <summary>Adds meta-progression experience.</summary>
    [WKTCriticalPatch(WKTPatchCategory.ProgressionSink), HarmonyPatch(typeof(CL_ProgressionManager), nameof(CL_ProgressionManager.AddExperience))]
    internal sealed class ProgressionManagerAddExperience_Patch {
        [UsedImplicitly]
        private static bool Prefix() => WKTProgressionLock.ShouldRunOriginal("CL_ProgressionManager.AddExperience");
    }

    /// <summary>Re-evaluates unlock requirements and shows unlock popups.</summary>
    [WKTCriticalPatch(WKTPatchCategory.ProgressionSink), HarmonyPatch(typeof(CL_ProgressionManager), nameof(CL_ProgressionManager.UpdateUnlocks))]
    internal sealed class ProgressionManagerUpdateUnlocks_Patch {
        [UsedImplicitly]
        private static bool Prefix() => WKTProgressionLock.ShouldRunOriginal("CL_ProgressionManager.UpdateUnlocks");
    }

    /// <summary>Writes a mid-run session save, which could overwrite a singleplayer save.</summary>
    [WKTCriticalPatch(WKTPatchCategory.ProgressionSink), HarmonyPatch(typeof(CL_SaveManager), nameof(CL_SaveManager.SaveSessionToFile))]
    internal sealed class SaveManagerSaveSessionToFile_Patch {
        [UsedImplicitly]
        private static bool Prefix() => WKTProgressionLock.ShouldRunOriginal("CL_SaveManager.SaveSessionToFile");
    }

    /// <summary>Deletes a session save, which could delete a singleplayer save.</summary>
    [WKTCriticalPatch(WKTPatchCategory.ProgressionSink), HarmonyPatch(typeof(CL_SaveManager), "DeleteSessionSave")]
    internal sealed class SaveManagerDeleteSessionSave_Patch {
        [UsedImplicitly]
        private static bool Prefix() => WKTProgressionLock.ShouldRunOriginal("CL_SaveManager.DeleteSessionSave");
    }

    /// <summary>Writes a speedrun record file.</summary>
    [WKTCriticalPatch(WKTPatchCategory.ProgressionSink), HarmonyPatch(typeof(SpeedrunManager), nameof(SpeedrunManager.SaveLog))]
    internal sealed class SpeedrunManagerSaveLog_Patch {
        [UsedImplicitly]
        private static bool Prefix() => WKTProgressionLock.ShouldRunOriginal("SpeedrunManager.SaveLog");
    }
}
