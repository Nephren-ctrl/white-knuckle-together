using System;

namespace WhiteKnuckleTogether {
    /// <summary>Which players of a multiplayer run are dead, by player id. A bit set, so it never allocates.</summary>
    public sealed class WKTDeathRoster {
        private const int c_word_bits = 64;
        private readonly ulong[] m_words = new ulong[(byte.MaxValue + 1) / c_word_bits];

        public int DeadCount { get; private set; }

        public bool IsDead(byte id) => (m_words[id / c_word_bits] & Bit(id)) != 0;

        /// <summary>Returns whether <paramref name="id"/>'s state changed.</summary>
        public bool Set(byte id, bool dead) {
            if (IsDead(id) == dead) return false;
            if (dead) {
                m_words[id / c_word_bits] |= Bit(id);
                DeadCount++;
            } else {
                m_words[id / c_word_bits] &= ~Bit(id);
                DeadCount--;
            }
            return true;
        }

        /// <summary>Whether every player in <paramref name="ids"/> is dead. False for no players at all.</summary>
        public bool AreAllDead(ReadOnlySpan<byte> ids) {
            if (ids.IsEmpty) return false;
            foreach (var id in ids) {
                if (!IsDead(id)) return false;
            }
            return true;
        }

        public void Clear() {
            Array.Clear(m_words, 0, m_words.Length);
            DeadCount = 0;
        }

        private static ulong Bit(byte id) => 1UL << (id % c_word_bits);
    }
}
