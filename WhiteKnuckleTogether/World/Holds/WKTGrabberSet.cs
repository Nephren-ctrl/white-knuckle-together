using System;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// The distinct players holding one object, by player id. Holds up to <see cref="WKTLobbyConsts.MAX_PLAYERS"/>, the most
    /// players a lobby has. A value type with no heap storage, so a table of them never allocates.
    /// </summary>
    internal struct WKTGrabberSet {
        public const int CAPACITY = WKTLobbyConsts.MAX_PLAYERS;
        private uint m_ids;

        public int Count { get; private set; }

        public byte this[int index] {
            get {
                if ((uint)index >= (uint)Count) throw new ArgumentOutOfRangeException(nameof(index));
                return (byte)(m_ids >> (index * 8));
            }
        }

        public bool Contains(byte playerId) => IndexOf(playerId) >= 0;

        /// <summary>False if the player already holds it or the set is full.</summary>
        public bool Add(byte playerId) {
            if (Count >= CAPACITY || Contains(playerId)) return false;
            m_ids |= (uint)playerId << (Count * 8);
            Count++;
            return true;
        }

        /// <summary>False if the player did not hold it. Keeps the order of the others.</summary>
        public bool Remove(byte playerId) {
            var index = IndexOf(playerId);
            if (index < 0) return false;
            var low = index == 0 ? 0u : m_ids & (uint)((1UL << (index * 8)) - 1);
            var high = index + 1 >= CAPACITY ? 0u : m_ids >> ((index + 1) * 8);
            m_ids = low | (high << (index * 8));
            Count--;
            return true;
        }

        public void Clear() {
            m_ids = 0;
            Count = 0;
        }

        private int IndexOf(byte playerId) {
            for (var i = 0; i < Count; i++) {
                if ((byte)(m_ids >> (i * 8)) == playerId) return i;
            }
            return -1;
        }
    }
}
