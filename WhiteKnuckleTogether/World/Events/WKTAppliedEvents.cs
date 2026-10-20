using System.Collections.Generic;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// The world events applied so far in each loaded level, in order, so a player who joins late or reconnects gets the same
    /// world. Every machine keeps it, so a client that takes over as host already has it. A once-only event is applied only
    /// once, even when it is replayed to a machine that already has it. Pure, so it can be tested without Unity.
    /// </summary>
    internal sealed class WKTAppliedEvents {
        private readonly List<Entry> m_entries = new();
        private readonly HashSet<(ulong, byte)> m_once = new();

        public int Count => m_entries.Count;
        public IReadOnlyList<Entry> Entries => m_entries;

        /// <summary>Records an applied event. False, and nothing recorded, for a once-only event that was already applied.</summary>
        public bool Record(ulong key, byte index, int levelId, bool once) {
            if (once && !m_once.Add((key, index))) return false;
            m_entries.Add(new Entry(key, index, levelId, once));
            return true;
        }

        public bool WasApplied(ulong key, byte index) => m_once.Contains((key, index));

        public void ForgetLevel(int levelId) {
            for (var i = m_entries.Count - 1; i >= 0; i--) {
                var entry = m_entries[i];
                if (entry.LevelId != levelId) continue;
                if (entry.Once)
                    m_once.Remove((entry.Key, entry.Index));
                m_entries.RemoveAt(i);
            }
        }

        public void Clear() {
            m_entries.Clear();
            m_once.Clear();
        }

        public readonly struct Entry {
            public readonly ulong Key;
            public readonly byte Index;
            public readonly int LevelId;
            public readonly bool Once;

            public Entry(ulong key, byte index, int levelId, bool once) {
                Key = key;
                Index = index;
                LevelId = levelId;
                Once = once;
            }
        }
    }
}
