using System;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// The multiplayer gamemodes the main menu builds at runtime (they are clones, not game assets, so the game's own
    /// asset lookup cannot find them). Lets a client resolve the gamemode named in StartRun, and tells multiplayer runs
    /// apart from singleplayer ones when Play is pressed.
    /// </summary>
    internal static class WKTMultiplayerGamemodes {
        private static readonly M_Gamemode?[] s_gamemodes = new M_Gamemode?[GetIdCount()];

        /// <summary>Replaces any gamemode registered for <paramref name="id"/>, e.g. when the main menu reloads.</summary>
        public static void Register(GamemodeId id, M_Gamemode gamemode) => s_gamemodes[(int)id] = gamemode;

        public static bool TryGet(GamemodeId id, out M_Gamemode gamemode) {
            gamemode = null!;
            if ((uint)id >= (uint)s_gamemodes.Length) return false;
            var registered = s_gamemodes[(int)id];
            if (registered == null) return false;
            gamemode = registered;
            return true;
        }

        public static bool TryGetId(M_Gamemode? gamemode, out GamemodeId id) {
            id = default;
            if (gamemode == null) return false;
            for (var i = 0; i < s_gamemodes.Length; i++) {
                if (!ReferenceEquals(s_gamemodes[i], gamemode)) continue;
                id = (GamemodeId)i;
                return true;
            }
            return false;
        }

        private static int GetIdCount() {
            var max = 0;
            foreach (GamemodeId id in Enum.GetValues(typeof(GamemodeId))) {
                if ((int)id > max)
                    max = (int)id;
            }
            return max + 1;
        }
    }
}
