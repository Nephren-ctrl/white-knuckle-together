using System;
using System.Collections.Generic;

namespace WhiteKnuckleTogether {
    /// <summary>Maps lobby modifiers to the game's gamemode settings, restricted to what the shared progression unlocks.</summary>
    internal static class WKTRunGamemodeSettings {
        public const string IRON_KNUCKLE_SETTING = "ironknuckle";
        public const string HARD_MODE_SETTING = "hardmode";
        private static readonly ModifierId[] s_modifiers = (ModifierId[])Enum.GetValues(typeof(ModifierId));

        /// <summary>The <c>GamemodeSetting.id</c> a modifier turns on. False for modifiers only the mod knows about.</summary>
        public static bool TryGetSettingId(ModifierId modifier, out string settingId) {
            switch (modifier) {
                case ModifierId.IronKnuckle:
                    settingId = IRON_KNUCKLE_SETTING;
                    return true;
                case ModifierId.Hard:
                    settingId = HARD_MODE_SETTING;
                    return true;
                case ModifierId.Instanced:
                case ModifierId.ShareTimedHandholds:
                case ModifierId.ShareRoaches:
                case ModifierId.ItemStealing:
                case ModifierId.InstancedArtifacts:
                case ModifierId.GrabAssists:
                case ModifierId.ProximityVoice:
                case ModifierId.DeadCanTalk:
                case ModifierId.KeepTogether:
                    settingId = string.Empty;
                    return false;
                default:
                    throw new ArgumentOutOfRangeException(nameof(modifier), modifier, null);
            }
        }

        /// <summary>
        /// Whether a setting gated by <paramref name="unlockId"/> (<c>GamemodeSetting.unlock</c>'s id, or null when the
        /// setting has no unlock) is available in <paramref name="shared"/>. A gated setting with an empty id is locked.
        /// </summary>
        public static bool IsSettingUnlocked(bool hasUnlock, string? unlockId, WKTProgressionProfile shared) =>
            !hasUnlock || shared.HasUnlock(unlockId);

        /// <summary>
        /// Clears every gamemode modifier of <paramref name="run"/> that <paramref name="gamemode"/> has no setting for, or
        /// whose setting the run's shared progression has not unlocked, then fills <paramref name="settingIds"/> (cleared
        /// first) with the settings of the remaining ones. Mod-only modifiers are kept as they are.
        /// </summary>
        public static void Resolve(M_Gamemode gamemode, WKTRunStart run, List<string> settingIds) {
            settingIds.Clear();
            foreach (var modifier in s_modifiers) {
                if (!run.HasModifier(modifier) || !TryGetSettingId(modifier, out var settingId)) continue;
                var setting = FindSetting(gamemode, settingId);
                if (setting != null && IsSettingUnlocked(setting.unlock != null, setting.unlock?.id, run.SharedProgression)) {
                    settingIds.Add(settingId);
                    continue;
                }
                WKT.Instance.Logger.LogInfo(setting == null ?
                    $"Dropped modifier {modifier}: gamemode {gamemode.gamemodeName} has no '{settingId}' setting." :
                    $"Dropped modifier {modifier}: not every player has unlocked it.");
                run.SetModifier(modifier, false);
            }
        }

        private static GamemodeSetting? FindSetting(M_Gamemode gamemode, string settingId) {
            if (gamemode.gamemodeSettings == null) return null;
            foreach (var setting in gamemode.gamemodeSettings) {
                if (setting != null && setting.id == settingId)
                    return setting;
            }
            return null;
        }
    }
}
