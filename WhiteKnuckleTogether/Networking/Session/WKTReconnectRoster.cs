using System.Collections.Generic;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// The players of a session whose host left, held by the new host until they reconnect. A player is recognized by the
    /// Steam id of their authenticated connection, never by anything they send, and keeps their id and player object. Pure,
    /// so it can be tested without Steam.
    /// </summary>
    public sealed class WKTReconnectRoster {
        private readonly Dictionary<ulong, WKTPlayer> m_players = new();
        private float m_deadline;

        public int Count => m_players.Count;
        public Dictionary<ulong, WKTPlayer>.ValueCollection Players => m_players.Values;

        /// <summary>Adds a player to wait for. Every player waits until the same deadline.</summary>
        public void Add(WKTPlayer player, float deadline) {
            m_players[player.friend.Id.Value] = player;
            m_deadline = deadline;
        }

        public bool Contains(ulong steamId) => m_players.ContainsKey(steamId);

        /// <summary>Takes the waiting player out of the roster when they come back.</summary>
        public bool TryClaim(ulong steamId, out WKTPlayer player) => m_players.Remove(steamId, out player);

        /// <summary>Once the deadline has passed, moves every player still missing into <paramref name="expired"/>.</summary>
        public void CollectExpired(float time, List<WKTPlayer> expired) {
            if (m_players.Count == 0 || time < m_deadline) return;
            expired.AddRange(m_players.Values);
            m_players.Clear();
        }
    }
}
