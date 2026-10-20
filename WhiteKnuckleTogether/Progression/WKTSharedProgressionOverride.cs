using System.Collections.Generic;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// While active, the game only sees the session's shared progression: what every player has unlocked. It can only
    /// ever hide content a player really has, never reveal content they lack, so nobody is spoiled.
    /// <para>
    /// Save flags (including achievement flags) and facility upgrades are filtered in the in-memory save data itself, so
    /// every game reader agrees without patching each one. That is safe because <see cref="WKTProgressionLock"/> blocks
    /// every save while active, and the save is reloaded from disk on rollback. Unlocks and achievement queries, which
    /// the game evaluates from stats and experience, are answered from the shared profile instead.
    /// </para>
    /// </summary>
    internal static class WKTSharedProgressionOverride {
        private static readonly WKTProgressionProfile s_shared = new();
        private static readonly List<string> s_runSettings = new();
        private static string? s_runGamemodeName;

        public static bool IsActive { get; private set; }

        public static void Activate(WKTProgressionProfile shared) {
            s_shared.CopyFrom(shared);
            IsActive = true;
        }

        public static void Deactivate() {
            IsActive = false;
            s_shared.Clear();
            s_runGamemodeName = null;
            s_runSettings.Clear();
        }

        /// <summary>
        /// Gamemode settings such as hard mode are per-player save data, so each machine would otherwise run with its own.
        /// While active, the gamemode's active settings are forced to exactly <paramref name="settingIds"/> after every
        /// save load. Applies to the currently loaded save on the next <see cref="FilterSaveData"/>.
        /// </summary>
        public static void SetRunGamemodeSettings(string gamemodeName, List<string> settingIds) {
            s_runGamemodeName = gamemodeName;
            s_runSettings.Clear();
            s_runSettings.AddRange(settingIds);
        }

        /// <summary>The state an unlock takes while active. Callers only consult this when <see cref="IsActive"/>.</summary>
        public static bool IsUnlockShared(string? unlockId) => s_shared.HasUnlock(unlockId);

        /// <summary>Restricts a game unlock query to the shared set while active. Never turns false into true.</summary>
        public static bool FilterUnlock(string? unlockId, bool original) =>
            original && (!IsActive || s_shared.HasUnlock(unlockId));

        /// <summary>Restricts a game achievement query to the shared set while active. Never turns false into true.</summary>
        public static bool FilterAchievement(string? achievementId, bool original) =>
            original && (!IsActive || s_shared.HasAchievement(achievementId));

        /// <summary>
        /// Lowers, in place, every true flag and upgrade the shared profile lacks. Flags are cleared rather than removed
        /// so achievement loading resets their flags too. Also applies the run's gamemode settings. Allocation-free once
        /// the run's gamemode has save data.
        /// </summary>
        public static void FilterSaveData(StatManager.SaveData saveData) {
            if (!IsActive) return;
            ApplyRunGamemodeSettings(saveData);
            if (saveData.flags != null) {
                foreach (var flag in saveData.flags) {
                    if (flag != null && flag.value && !s_shared.HasFlag(flag.name))
                        flag.value = false;
                }
            }
            if (saveData.facilities == null) return;
            foreach (var facility in saveData.facilities) {
                var upgrades = facility?.upgrades;
                if (upgrades == null) continue;
                for (var i = upgrades.Count - 1; i >= 0; i--) {
                    var upgrade = upgrades[i];
                    if (upgrade == null) continue;
                    // Facility.HasUpgrade only checks presence, so an unshared upgrade must be removed, not just unowned.
                    if (!s_shared.HasUpgrade(facility!.id, upgrade.id))
                        upgrades.RemoveAt(i);
                    else if (upgrade.owned && !s_shared.OwnsUpgrade(facility.id, upgrade.id))
                        upgrade.owned = false;
                }
            }
        }

        private static void ApplyRunGamemodeSettings(StatManager.SaveData saveData) {
            if (s_runGamemodeName == null) return;
            var gameMode = FindGameMode(saveData, s_runGamemodeName);
            if (gameMode == null) {
                saveData.SetupGamemode(s_runGamemodeName);
                gameMode = FindGameMode(saveData, s_runGamemodeName)!;
            }
            gameMode.activeSettings ??= new List<string>();
            gameMode.activeSettings.Clear();
            gameMode.activeSettings.AddRange(s_runSettings);
        }

        /// <summary>Unlike SaveData.GetGameMode, never logs to the console or creates the entry.</summary>
        private static StatManager.SaveData.GameModeData? FindGameMode(StatManager.SaveData saveData, string name) {
            if (saveData.gameModes == null) return null;
            foreach (var gameMode in saveData.gameModes) {
                if (gameMode != null && gameMode.name == name)
                    return gameMode;
            }
            return null;
        }
    }
}
