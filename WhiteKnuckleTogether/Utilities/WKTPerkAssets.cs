using System;
using System.Collections.Generic;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// The game's perk assets by id, for reading what another player's perks do (their buffs and modules). Lookups are
    /// cached, misses too. The assets are shared: never change or initialize one.
    /// </summary>
    internal static class WKTPerkAssets {
        private static readonly Dictionary<string, Perk?> s_perks = new();

        /// <summary>The perk asset with <paramref name="id"/>, as the game finds it. Null if there is none.</summary>
        public static Perk? Get(string id) {
            if (s_perks.TryGetValue(id, out var perk)) return perk;
            try {
                perk = CL_AssetManager.GetPerkAsset(id);
            }
            catch (Exception) {
                perk = null;
            }
            s_perks[id] = perk;
            return perk;
        }
    }
}
