using System;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// Decides when to send the local player's state. Ticks are whole <see cref="WKTNetworkConsts.SEND_DELTA"/> steps of
    /// real time, so a hitch skips sequence numbers instead of delaying them, and the sequence always tracks the sender's
    /// clock. Receivers treat skipped numbers like lost packets.
    /// </summary>
    public sealed class WKTSendClock {
        private long m_lastTick = long.MinValue;

        /// <summary>True once per tick, with that tick's sequence number. Time moving backwards never sends.</summary>
        public bool TryAdvance(double realtime, out ushort sequence) {
            var tick = (long)Math.Floor(realtime * WKTNetworkConsts.SEND_RATE);
            sequence = unchecked((ushort)tick);
            if (tick <= m_lastTick) return false;
            m_lastTick = tick;
            return true;
        }

        public void Reset() => m_lastTick = long.MinValue;
    }
}
