using System.Collections.Generic;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// Voice cosmetics replace some of a player's sounds. Each player publishes the ids of their active ones (see
    /// <see cref="WKTNetwork.PublishLocalVoices"/>), and every machine looks them up among its own cosmetics.
    /// So a voice is heard by everyone who has it: every built-in one, and custom ones installed on both machines.
    /// Perks replace sounds the same way (Roach Mode's roach voice, through <c>PerkModule_PlayerAudioOverride</c>): those
    /// travel as perk ids with the player's body (see <see cref="WKTPlayerBodyState.VoicePerks"/>).
    /// </summary>
    internal static class WKTVoiceCosmetics {
        private static readonly List<string> s_ids = new();

        /// <summary>The ids of the local player's active voices. Reused on the next call.</summary>
        public static List<string> GetLocalIds() {
            s_ids.Clear();
            var cosmeticData = SettingsManager.settings?.cosmeticSaveData;
            if (cosmeticData == null) return s_ids;
            foreach (var cosmetic in CL_CosmeticManager.GetAllLoadedCosmetics()) {
                if (cosmetic is not Cosmetic_Voice || cosmetic.cosmeticInfo == null) continue;
                if (cosmeticData.CosmeticIsActive(cosmetic.cosmeticInfo.id))
                    s_ids.Add(cosmetic.cosmeticInfo.id);
            }
            return s_ids;
        }

        /// <summary>Fills <paramref name="ids"/> with the ids of <paramref name="player"/>'s perks that replace their sounds, in their order.</summary>
        public static void GetLocalPerkVoiceIds(ENT_Player player, List<string> ids) {
            ids.Clear();
            if (player.perks == null) return;
            foreach (var perk in player.perks) {
                if (perk != null && !string.IsNullOrEmpty(perk.id) && HasAudioOverride(perk))
                    ids.Add(perk.id);
            }
        }

        /// <summary>
        /// Fills <paramref name="overrides"/> with what replaces a remote player's sounds, in the order the game searches
        /// them: the local player's overrides that are neither voices nor from the local player's own perks, then the voices
        /// in <paramref name="memberData"/> (see <see cref="WKTLobbyData.ParseVoiceIds"/>), then the overrides of the remote
        /// player's <paramref name="perkIds"/>. The game adds voices as the player starts and perks after. Voices and perks
        /// this machine does not have are skipped.
        /// </summary>
        public static void Resolve(ENT_Player? localPlayer, string? memberData, IReadOnlyList<string> perkIds, List<AudioClipHandlerOverride> overrides) {
            overrides.Clear();
            var cosmetics = CL_CosmeticManager.GetAllLoadedCosmetics();
            var localHandler = localPlayer != null ? localPlayer.clipHandler : null;
            if (localHandler != null && localHandler.overrides != null) {
                foreach (var handlerOverride in localHandler.overrides) {
                    if (handlerOverride != null && !IsVoice(cosmetics, handlerOverride) && !IsFromPerk(localPlayer!, handlerOverride))
                        overrides.Add(handlerOverride);
                }
            }
            WKTLobbyData.ParseVoiceIds(memberData, s_ids);
            foreach (var id in s_ids) {
                var voice = FindVoice(cosmetics, id);
                if (voice != null)
                    overrides.Add(voice);
            }
            s_ids.Clear();
            foreach (var perkId in perkIds)
                AddPerkOverrides(WKTPerkAssets.Get(perkId), overrides);
        }

        private static void AddPerkOverrides(Perk? perk, List<AudioClipHandlerOverride> overrides) {
            if (perk == null || perk.modules == null) return;
            foreach (var module in perk.modules) {
                if (module is PerkModule_PlayerAudioOverride audio && audio.audioOverride != null)
                    overrides.Add(audio.audioOverride);
            }
        }

        private static bool HasAudioOverride(Perk perk) {
            if (perk.modules == null) return false;
            foreach (var module in perk.modules)
                if (module is PerkModule_PlayerAudioOverride audio && audio.audioOverride != null) return true;
            return false;
        }

        /// <summary>Whether one of <paramref name="player"/>'s perks added <paramref name="handlerOverride"/> to their sounds.</summary>
        private static bool IsFromPerk(ENT_Player player, AudioClipHandlerOverride handlerOverride) {
            if (player.perks == null) return false;
            foreach (var perk in player.perks) {
                if (perk == null || perk.modules == null) continue;
                foreach (var module in perk.modules)
                    if (module is PerkModule_PlayerAudioOverride audio && audio.audioOverride == handlerOverride) return true;
            }
            return false;
        }

        private static bool IsVoice(List<Cosmetic_Base> cosmetics, AudioClipHandlerOverride handlerOverride) {
            foreach (var cosmetic in cosmetics)
                if (cosmetic is Cosmetic_Voice voice && voice.cosmeticData?.clipHandlerOverride == handlerOverride) return true;
            return false;
        }

        private static AudioClipHandlerOverride? FindVoice(List<Cosmetic_Base> cosmetics, string id) {
            foreach (var cosmetic in cosmetics) {
                if (cosmetic is Cosmetic_Voice voice && voice.cosmeticInfo?.id == id && voice.cosmeticData?.clipHandlerOverride != null)
                    return voice.cosmeticData.clipHandlerOverride;
            }
            return null;
        }
    }
}
