using System;
using System.Collections.Generic;
using System.Text;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// Trinkets and bindings in multiplayer. Each player picks their own in their lobby's picker, but only from those every
    /// player in the lobby has unlocked: the host publishes those names (<see cref="WKTLobbyConsts.KEY_TRINKETS_AVAILABLE"/>),
    /// and every picker treats the rest as locked. A run then starts each player with their own picks, kept only if they
    /// are still available, allowed with the run's settings and within the trinket budget. The picks are the player's own
    /// selection state and never unlock anything.
    /// </summary>
    internal static class WKTTrinkets {
        private const char c_separator = ',';
        /// <summary>Trinket asset names are short; anything longer is not one.</summary>
        public const int MAX_NAME_LENGTH = 96;
        public const int MAX_TRINKETS = 64;
        private static readonly List<string> s_runPicks = new();
        private static readonly List<string> s_runTrinkets = new();
        private static readonly List<Trinket> s_resolved = new();

        /// <summary>A trinket with no unlock is always available; otherwise its unlock must be shared.</summary>
        public static bool IsAvailable(string? unlockId, bool hasUnlock, Func<string, bool> isUnlockShared) =>
            !hasUnlock || unlockId != null && isUnlockShared(unlockId);

        /// <summary>Adds the names of the trinkets and bindings of <paramref name="list"/> available with <paramref name="shared"/>.</summary>
        public static void GetAvailable(TrinketList? list, WKTProgressionProfile shared, HashSet<string> names) {
            names.Clear();
            if (list == null) return;
            AddAvailable(list.trinkets, shared, names);
            AddAvailable(list.bindings, shared, names);
        }

        private static void AddAvailable(List<Trinket>? trinkets, WKTProgressionProfile shared, HashSet<string> names) {
            if (trinkets == null) return;
            foreach (var trinket in trinkets) {
                if (trinket == null) continue;
                var unlock = trinket.progressionUnlock;
                if (IsAvailable(unlock != null ? unlock.id : null, unlock != null, shared.HasUnlock))
                    names.Add(trinket.name);
            }
        }

        /// <summary>Joins names for the lobby data. Names that are empty, overlong or contain the separator are left out.</summary>
        public static string FormatNames(IEnumerable<string> names, StringBuilder sb) {
            sb.Clear();
            var count = 0;
            foreach (var name in names) {
                if (!IsName(name) || count == MAX_TRINKETS) continue;
                if (count++ > 0)
                    sb.Append(c_separator);
                sb.Append(name);
            }
            var result = sb.ToString();
            sb.Clear();
            return result;
        }

        /// <summary>Replaces <paramref name="names"/> with those in <paramref name="value"/>, as <see cref="FormatNames"/> writes them.</summary>
        public static void ParseNames(string? value, HashSet<string> names) {
            names.Clear();
            if (string.IsNullOrEmpty(value)) return;
            foreach (var name in value.Split(c_separator)) {
                if (IsName(name) && names.Count < MAX_TRINKETS)
                    names.Add(name);
            }
        }

        private static bool IsName(string? name) => !string.IsNullOrEmpty(name) && name.Length <= MAX_NAME_LENGTH && name.IndexOf(c_separator) < 0;

        /// <summary>The game's budget: the trinkets' costs may not exceed one plus the bindings' costs.</summary>
        public static bool IsWithinBudget(int trinketCost, int bindingCost) => trinketCost <= 1 + bindingCost;

        /// <summary>
        /// Drops trinkets (never bindings), last picked first, until the rest fit the budget. <paramref name="isBinding"/> and
        /// <paramref name="cost"/> describe each pick.
        /// </summary>
        public static void FitBudget<T>(List<T> picks, Func<T, bool> isBinding, Func<T, int> cost) {
            var trinketCost = 0;
            var bindingCost = 0;
            foreach (var pick in picks) {
                if (isBinding(pick))
                    bindingCost += cost(pick);
                else
                    trinketCost += cost(pick);
            }
            for (var i = picks.Count - 1; i >= 0 && !IsWithinBudget(trinketCost, bindingCost); i--) {
                if (isBinding(picks[i])) continue;
                trinketCost -= cost(picks[i]);
                picks.RemoveAt(i);
            }
        }

        /// <summary>
        /// A multiplayer run is starting: remembers the local player's picks, as saved under the gamemode's name with their
        /// own settings, before the run's settings replace those.
        /// </summary>
        public static void CaptureRunPicks(M_Gamemode gamemode) {
            s_runPicks.Clear();
            var saveData = StatManager.saveData;
            if (saveData == null) return;
            s_runPicks.AddRange(saveData.GetGamemodeTrinkets(gamemode.GetGamemodeName()));
        }

        /// <summary>
        /// The trinkets the local player starts this run with: their picks that are unlocked (the shared progression decides
        /// during a run), not ruled out by the run's settings and within the budget.
        /// </summary>
        public static List<string> GetRunTrinkets(M_Gamemode gamemode) {
            s_resolved.Clear();
            foreach (var name in s_runPicks) {
                var trinket = CL_AssetManager.GetTrinketAsset(name);
                if (trinket == null || s_resolved.Contains(trinket) || !trinket.IsUnlocked() || IsBlacklisted(trinket, gamemode)) continue;
                // As the picker: Iron Knuckle has bindings only.
                if (!trinket.isBinding && gamemode.IsIronKnuckle()) continue;
                s_resolved.Add(trinket);
            }
            FitBudget(s_resolved, t => t.isBinding, t => t.cost);
            s_runTrinkets.Clear();
            foreach (var trinket in s_resolved)
                s_runTrinkets.Add(trinket.name);
            s_resolved.Clear();
            return s_runTrinkets;
        }

        private static bool IsBlacklisted(Trinket trinket, M_Gamemode gamemode) {
            if (trinket.settingBlacklist == null) return false;
            foreach (var setting in trinket.settingBlacklist) {
                if (gamemode.HasActiveSetting(setting)) return true;
            }
            return false;
        }

        public static void ClearRunPicks() => s_runPicks.Clear();
    }
}
