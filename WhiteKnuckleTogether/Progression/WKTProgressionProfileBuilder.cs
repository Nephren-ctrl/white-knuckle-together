using System.Collections.Generic;
using HarmonyLib;

namespace WhiteKnuckleTogether {
    /// <summary>Snapshots the local player's real progression from the game's loaded state.</summary>
    internal static class WKTProgressionProfileBuilder {
        internal const string ACHIEVEMENTS_FIELD = "achievements";
        private static readonly AccessTools.FieldRef<CL_AchievementManager, List<CL_AchievementManager.GameAchievement>>? s_achievementsRef =
            CreateAchievementsRef();

        /// <summary>
        /// Fills <paramref name="profile"/> (cleared first). Anything that cannot be read is left out, which locks it for
        /// the session rather than revealing it. Must be called while no multiplayer run is overriding progression, since
        /// that would snapshot the shared progression instead of the real one.
        /// </summary>
        public static void BuildLocal(WKTProgressionProfile profile, WKTLogger logger) {
            profile.Clear();
            if (WKTProgressionLock.IsLocked || WKTSharedProgressionOverride.IsActive) {
                logger.LogError("Progression is still overridden by a multiplayer run. Sharing no progression.");
                return;
            }
            if (CL_ProgressionManager.instance != null) {
                foreach (var unlock in CL_ProgressionManager.GetUnlockList()) {
                    if (unlock != null && unlock.state)
                        profile.AddUnlock(unlock.id);
                }
            }
            var achievementManager = CL_AchievementManager.instance;
            if (achievementManager != null && s_achievementsRef != null) {
                var achievements = s_achievementsRef(achievementManager);
                if (achievements != null) {
                    foreach (var achievement in achievements) {
                        if (achievement != null && achievement.flagged)
                            profile.AddAchievement(achievement.id);
                    }
                }
            }
            var saveData = StatManager.saveData;
            if (saveData != null) {
                if (saveData.flags != null) {
                    foreach (var flag in saveData.flags) {
                        if (flag != null && flag.value)
                            profile.AddFlag(flag.name);
                    }
                }
                if (saveData.facilities != null) {
                    foreach (var facility in saveData.facilities) {
                        if (facility?.upgrades == null) continue;
                        foreach (var upgrade in facility.upgrades) {
                            if (upgrade != null)
                                profile.AddUpgrade(facility.id, upgrade.id, upgrade.owned);
                        }
                    }
                }
            }
            logger.LogInfo($"Local progression: {profile.UnlockCount} unlocks, {profile.AchievementCount} achievements, " +
                $"{profile.FlagCount} flags, {profile.UpgradeCount} upgrades.");
        }

        private static AccessTools.FieldRef<CL_AchievementManager, List<CL_AchievementManager.GameAchievement>>? CreateAchievementsRef() {
            try {
                return AccessTools.FieldRefAccess<CL_AchievementManager, List<CL_AchievementManager.GameAchievement>>(ACHIEVEMENTS_FIELD);
            }
            catch {
                // Achievement-gated content then stays locked for the session, which is safe.
                return null;
            }
        }
    }
}
