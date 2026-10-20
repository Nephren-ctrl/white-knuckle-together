using System.Collections.Generic;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// The placed pitons of a synced co-op run, kept by the host so a player who joins late gets them. Each piton is named by
    /// the player who placed it (<see cref="MakeId"/>), so no machine waits for the host to name it. A piton that broke or
    /// was taken is removed once; a removal that arrives again, or before its piton, is ignored. Pure, so it can be tested
    /// without Unity.
    /// </summary>
    internal sealed class WKTPitonRegistry {
        private const int c_counter_bits = 24;
        private const uint c_counter_mask = (1u << c_counter_bits) - 1;
        private readonly Dictionary<uint, WKTPitonPlaced> m_pitons = new();
        private readonly HashSet<uint> m_removed = new();
        private readonly List<uint> m_idScratch = new();

        public int Count => m_pitons.Count;
        public IEnumerable<WKTPitonPlaced> Pitons => m_pitons.Values;

        /// <summary>The id of a player's <paramref name="counter"/>th piton (wrapping after 16 million), never zero.</summary>
        public static uint MakeId(byte playerId, uint counter) => ((uint)playerId << c_counter_bits) | ((counter & c_counter_mask) == 0 ? 1 : counter & c_counter_mask);

        public static byte GetPlayer(uint id) => (byte)(id >> c_counter_bits);

        /// <summary>Records a placed piton. False for an id already known or already removed.</summary>
        public bool Add(in WKTPitonPlaced piton) {
            if (piton.Id == 0 || m_removed.Contains(piton.Id) || m_pitons.ContainsKey(piton.Id)) return false;
            m_pitons.Add(piton.Id, piton);
            return true;
        }

        /// <summary>Adds a hammer hit to a piton. False when it is unknown.</summary>
        public bool Hammer(uint id, float amount) {
            if (!m_pitons.TryGetValue(id, out var piton)) return false;
            m_pitons[id] = piton.WithHammered(amount);
            return true;
        }

        /// <summary>Removes a piton for good. False when it was already removed.</summary>
        public bool Remove(uint id) {
            if (id == 0 || !m_removed.Add(id)) return false;
            m_pitons.Remove(id);
            return true;
        }

        public bool IsRemoved(uint id) => m_removed.Contains(id);

        /// <summary>Forgets the pitons of a level that unloaded.</summary>
        public void ForgetLevel(ulong levelKey) {
            m_idScratch.Clear();
            foreach (var pair in m_pitons) {
                if (pair.Value.LevelKey == levelKey)
                    m_idScratch.Add(pair.Key);
            }
            foreach (var id in m_idScratch)
                m_pitons.Remove(id);
            m_idScratch.Clear();
        }

        public void Clear() {
            m_pitons.Clear();
            m_removed.Clear();
        }
    }
}
