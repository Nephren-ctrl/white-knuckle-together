namespace WhiteKnuckleTogether {
    /// <summary>
    /// The perk the local player just bought at a perk machine, so only that perk is told as a choice
    /// (see <see cref="WKTRunEvents.OnPerk"/>). The machine adds the perk a moment after the purchase, at the end of its
    /// animation. Perks from anywhere else (trinkets, injuries, infections, Mother) are never a choice. Pure, so it can be
    /// tested without Unity.
    /// </summary>
    internal sealed class WKTPerkChoice {
        /// <summary>How long after the purchase the perk may arrive. The machine's animation takes about a second.</summary>
        public const float WINDOW_SECONDS = 10f;
        private string? m_perkId;
        private float m_time;

        /// <summary>A perk with <paramref name="perkId"/> was bought at <paramref name="time"/>.</summary>
        public void Record(string? perkId, float time) {
            m_perkId = string.IsNullOrEmpty(perkId) ? null : perkId;
            m_time = time;
        }

        /// <summary>Whether a perk with <paramref name="perkId"/> added at <paramref name="time"/> is the one bought. Each purchase matches once.</summary>
        public bool TryTake(string? perkId, float time) {
            if (m_perkId == null || perkId != m_perkId || time < m_time || time - m_time > WINDOW_SECONDS) return false;
            m_perkId = null;
            return true;
        }
    }
}
