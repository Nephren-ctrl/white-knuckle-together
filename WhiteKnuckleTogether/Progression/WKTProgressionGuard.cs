namespace WhiteKnuckleTogether {
    /// <summary>
    /// Game-facing side of <see cref="WKTProgressionLock"/> and <see cref="WKTSharedProgressionOverride"/>: engages both
    /// for a multiplayer run, and on returning to the main menu discards any progression the run changed in memory and
    /// restores the player's real progression before releasing the lock.
    /// </summary>
    internal static class WKTProgressionGuard {
        private static StatManager? s_statManagerDuringRun;

        /// <summary>Locks progression and restricts the game to <paramref name="shared"/> until rollback.</summary>
        public static void EngageForRun(WKTProgressionProfile shared) {
            if (!WKTProgressionLock.IsLocked) {
                s_statManagerDuringRun = StatManager.instance;
                WKTProgressionLock.Engage();
            }
            WKTSharedProgressionOverride.Activate(shared);
            ApplyToLoadedState();
        }

        /// <summary>Call once the main menu has loaded. Does nothing if the lock is not engaged.</summary>
        public static void RollbackAndRelease() {
            if (!WKTProgressionLock.IsLocked) return;
            WKTSharedProgressionOverride.Deactivate();
            // StatManager is per-scene and reloads wk_save.json in Awake, so a new instance already discarded whatever
            // the run changed in its static save data. Only an instance that survived the run needs an explicit reload.
            var statManager = StatManager.instance;
            if (statManager != null && ReferenceEquals(statManager, s_statManagerDuringRun))
                ReloadStats(statManager);
            s_statManagerDuringRun = null;
            if (CL_AchievementManager.instance != null && StatManager.saveData != null)
                CL_AchievementManager.LoadAchievements(StatManager.saveData.flags);
            WKTProgressionLock.Release();
            // Recomputes unlock states from the real save, as on startup. Needs the lock released since UpdateUnlocks is
            // a sink. Without popups, so nothing is announced as newly unlocked.
            if (CL_ProgressionManager.instance == null || StatManager.saveData == null) return;
            var unlockAll = SettingsManager.settings != null && SettingsManager.settings.unlockAll;
            foreach (var unlock in CL_ProgressionManager.GetUnlockList()) {
                if (unlock != null)
                    unlock.state = unlockAll;
            }
            CL_ProgressionManager.RefreshExperience();
        }

        /// <summary>Applies the shared progression to whatever the current scene has already loaded.</summary>
        private static void ApplyToLoadedState() {
            if (SettingsManager.settings != null)
                SettingsManager.settings.unlockAll = false;
            var saveData = StatManager.saveData;
            if (saveData != null) {
                WKTSharedProgressionOverride.FilterSaveData(saveData);
                if (CL_AchievementManager.instance != null)
                    CL_AchievementManager.LoadAchievements(saveData.flags);
            }
            // UpdateUnlocks is blocked by the lock, so unlock states only change here and in the CheckUnlock override.
            if (CL_ProgressionManager.instance == null) return;
            foreach (var unlock in CL_ProgressionManager.GetUnlockList()) {
                if (unlock != null)
                    unlock.state = WKTSharedProgressionOverride.IsUnlockShared(unlock.id);
            }
        }

        /// <summary>Mirrors StatManager.Awake's load sequence, without re-registering its console commands.</summary>
        private static void ReloadStats(StatManager statManager) {
            WKT.Instance.Logger.LogInfo("Reloading stats that survived the multiplayer run.");
            StatManager.sessionStats = new StatManager.GameStats();
            StatManager.saveData = null;
            statManager.LoadStats();
            StatManager.saveData!.gameStats.InitializeDictionary();
            StatManager.sessionStats.InitializeDictionary();
            if (StatManager.saveData.gameModes == null) return;
            foreach (var gameMode in StatManager.saveData.gameModes)
                gameMode.stats.InitializeDictionary();
        }
    }
}
