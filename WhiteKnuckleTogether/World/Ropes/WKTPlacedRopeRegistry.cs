using System.Collections.Generic;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// The ropes thrown rebars left in walls during a synced co-op run, so a player who joins late gets them. They are secure
    /// and never fall out, so they last until their level unloads. Pure, so it can be tested without Unity.
    /// </summary>
    internal sealed class WKTPlacedRopeRegistry {
        private static readonly ulong s_ropeSalt = WKTStableHash.Of("WKT.PlacedRope");
        private readonly Dictionary<uint, WKTRopePlaced> m_ropes = new();
        private readonly List<uint> m_idScratch = new();

        public int Count => m_ropes.Count;
        public Dictionary<uint, WKTRopePlaced>.ValueCollection Ropes => m_ropes.Values;

        /// <summary>The key the <paramref name="index"/>th rope of a placement gets on every machine.</summary>
        public static ulong GetRopeKey(uint placedId, int index) => WKTStableHash.Mix(WKTStableHash.Mix(s_ropeSalt, placedId), (ulong)index);

        /// <summary>Records a placed rope. False for an id already known.</summary>
        public bool Add(in WKTRopePlaced placed) {
            if (placed.Id == 0 || m_ropes.ContainsKey(placed.Id)) return false;
            m_ropes.Add(placed.Id, placed);
            return true;
        }

        public bool Contains(uint id) => m_ropes.ContainsKey(id);

        /// <summary>Forgets the ropes of a level that unloaded.</summary>
        public void ForgetLevel(ulong levelKey) {
            m_idScratch.Clear();
            foreach (var pair in m_ropes) {
                if (pair.Value.LevelKey == levelKey)
                    m_idScratch.Add(pair.Key);
            }
            foreach (var id in m_idScratch)
                m_ropes.Remove(id);
            m_idScratch.Clear();
        }
    }
}
