using System;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// Which level ("room") each player is in, by its content key (<see cref="WKTContentSeeding"/>, the same on every machine),
    /// so a player whose room is not loaded here is not "loaded" either: their body is hidden, its sounds are silent and their
    /// proximity voice is muted. A room nobody reported yet counts as loaded.
    /// </summary>
    internal static class WKTPlayerRooms {
        /// <summary>No room known.</summary>
        public const ulong UNKNOWN = 0;
        /// <summary>
        /// Any chamber, such as the Bazaar: a room built outside the levels, which hides them while a player is in it. Everyone
        /// is taken in together, so a player in one is present to anyone else in one.
        /// </summary>
        public const ulong CHAMBER = ulong.MaxValue;
        /// <summary>A player's room is sent again this often even unchanged, so players who join late learn it.</summary>
        public const float RESEND_SECONDS = 3f;
        private static readonly ulong[] s_rooms = new ulong[byte.MaxValue + 1];

        /// <summary>The last room a player reported.</summary>
        public static ulong Get(byte playerId) => s_rooms[playerId];

        public static void Set(byte playerId, ulong room) => s_rooms[playerId] = room;

        public static void Reset() => Array.Clear(s_rooms, 0, s_rooms.Length);

        /// <summary>Whether a player in <paramref name="room"/> is shown and heard here.</summary>
        public static bool IsPresent(ulong room, bool roomLoaded) => room == UNKNOWN || roomLoaded;

        /// <summary>The local player's room, or <see cref="UNKNOWN"/> outside a keyed level.</summary>
        public static ulong GetLocalRoom() {
            if (IsInChamber()) return CHAMBER;
            var loader = WorldLoader.instance;
            var level = loader != null && loader.GetCurrentBranch() != null ? loader.GetCurrentLevel()?.GetLevel() : null;
            return level != null && WKTContentSeeding.TryGetLevelKey(level, out var key) ? key : UNKNOWN;
        }

        /// <summary>
        /// Whether the level <paramref name="room"/> exists here and is active, as the game keeps the levels around the player, or
        /// for a chamber whether the local player is in one too.
        /// </summary>
        public static bool IsLoadedHere(ulong room) => room == CHAMBER
            ? IsInChamber()
            : room != UNKNOWN && WKTContentSeeding.TryGetLevel(room, out var level) && level != null && level.gameObject.activeInHierarchy;

        /// <summary>Whether the local player is in a chamber: while one is active, the game spawns what is dropped into it.</summary>
        public static bool IsInChamber() {
            var parent = WorldLoader.GetCurrentLevelParentRoot();
            return parent != null && parent.TryGetComponent<M_Chamber>(out _);
        }
    }
}
