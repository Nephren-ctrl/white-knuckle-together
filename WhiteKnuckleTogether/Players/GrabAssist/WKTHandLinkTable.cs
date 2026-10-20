namespace WhiteKnuckleTogether {
    /// <summary>
    /// Which hands hold which (Grab Assists): every machine keeps one, the host decides what goes in it. A hand holds at most
    /// one other hand, and a player never holds their own.
    /// </summary>
    internal sealed class WKTHandLinkTable {
        private const int c_hands = WKTHandLink.HAND_COUNT;
        /// <summary>Per player and hand: the other player's id and hand packed as id * 2 + hand, or -1 when free.</summary>
        private readonly int[] m_partners = new int[(byte.MaxValue + 1) * c_hands];

        public WKTHandLinkTable() => Clear();

        public bool IsLinked(byte playerId, byte hand) => m_partners[Slot(playerId, hand)] >= 0;

        public bool TryGetPartner(byte playerId, byte hand, out byte partnerId, out byte partnerHand) {
            var packed = m_partners[Slot(playerId, hand)];
            partnerId = (byte)(packed >= 0 ? packed / c_hands : 0);
            partnerHand = (byte)(packed >= 0 ? packed % c_hands : 0);
            return packed >= 0;
        }

        /// <summary>Whether the two hands could be linked now: different players, both hands free.</summary>
        public bool CanLink(byte playerId, byte hand, byte otherId, byte otherHand) =>
            playerId != otherId && hand < c_hands && otherHand < c_hands && !IsLinked(playerId, hand) && !IsLinked(otherId, otherHand);

        /// <summary>Links the two hands. False, changing nothing, if <see cref="CanLink"/> is false.</summary>
        public bool Link(byte playerId, byte hand, byte otherId, byte otherHand) {
            if (!CanLink(playerId, hand, otherId, otherHand)) return false;
            m_partners[Slot(playerId, hand)] = Slot(otherId, otherHand);
            m_partners[Slot(otherId, otherHand)] = Slot(playerId, hand);
            return true;
        }

        /// <summary>Frees the hand and its partner. False if it was not linked.</summary>
        public bool Release(byte playerId, byte hand) {
            var slot = Slot(playerId, hand);
            var partner = m_partners[slot];
            if (partner < 0) return false;
            m_partners[slot] = -1;
            m_partners[partner] = -1;
            return true;
        }

        /// <summary>Frees both hands of a player who left or died.</summary>
        public void ReleasePlayer(byte playerId) {
            for (byte hand = 0; hand < c_hands; hand++)
                Release(playerId, hand);
        }

        public void Clear() {
            for (var i = 0; i < m_partners.Length; i++)
                m_partners[i] = -1;
        }

        private static int Slot(byte playerId, byte hand) => playerId * c_hands + hand;
    }
}
