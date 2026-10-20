namespace WhiteKnuckleTogether {
    /// <summary>
    /// Keeps one player from flooding the chat: a token bucket allowing a short burst then one message every
    /// <see cref="REFILL_SECONDS"/>, and the same message again only after <see cref="DUPLICATE_COOLDOWN"/>. The sender runs it
    /// so a blocked message never leaves, and the host runs the same rules again per player, since clients cannot be trusted.
    /// </summary>
    internal sealed class WKTChatRateLimiter {
        public const int BURST = 4;
        public const float REFILL_SECONDS = 1.5f;
        public const float DUPLICATE_COOLDOWN = 3f;
        private float m_tokens = BURST;
        private float m_lastRefill = float.NegativeInfinity;
        private string? m_lastText;
        private float m_lastTextTime = float.NegativeInfinity;

        /// <summary>Whether <paramref name="text"/> may be sent at <paramref name="now"/> (seconds). Counts it if so.</summary>
        public WKTChatVerdict TrySend(string text, float now) {
            Refill(now);
            if (m_lastText == text && now - m_lastTextTime < DUPLICATE_COOLDOWN) return WKTChatVerdict.Duplicate;
            if (m_tokens < 1f) return WKTChatVerdict.TooFast;
            m_tokens -= 1f;
            m_lastText = text;
            m_lastTextTime = now;
            return WKTChatVerdict.Allowed;
        }

        public void Reset() {
            m_tokens = BURST;
            m_lastRefill = float.NegativeInfinity;
            m_lastText = null;
            m_lastTextTime = float.NegativeInfinity;
        }

        private void Refill(float now) {
            if (float.IsNegativeInfinity(m_lastRefill)) {
                m_lastRefill = now;
                return;
            }
            var elapsed = now - m_lastRefill;
            if (elapsed <= 0f) return;
            m_tokens = m_tokens + elapsed / REFILL_SECONDS;
            if (m_tokens > BURST)
                m_tokens = BURST;
            m_lastRefill = now;
        }
    }
}
