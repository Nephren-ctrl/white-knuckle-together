using System;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// Estimates how far one of the host's clocks is ahead of the matching local clock. Each sample is a round trip: when the
    /// request left, the host's clock when it answered, and when the answer arrived. The sample with the shortest round trip
    /// among the last <see cref="WINDOW"/> is the most accurate, taking the host's answer to be half way. The offset moves
    /// toward it slowly, so the shared clock never jumps backwards in small steps, but snaps across a large error such as the
    /// first sample or a new host. Pure, so it can be tested without Unity. Allocation free.
    /// </summary>
    internal sealed class WKTClockOffset {
        public const int WINDOW = 8;
        /// <summary>Round trips longer than this are not samples worth taking.</summary>
        public const double MAX_ROUND_TRIP = 5.0;
        /// <summary>Errors larger than this are corrected at once.</summary>
        public const double SNAP_ERROR = 0.5;
        /// <summary>How fast smaller errors are corrected, in seconds per second.</summary>
        public const double SLEW_RATE = 0.05;
        private readonly double[] m_roundTrips = new double[WINDOW];
        private readonly double[] m_offsets = new double[WINDOW];
        private int m_count;
        private int m_next;
        private double m_target;

        public bool HasOffset { get; private set; }
        /// <summary>Host clock minus local clock, as currently applied.</summary>
        public double Offset { get; private set; }

        /// <summary>
        /// Adds a round trip. <paramref name="sent"/> and <paramref name="received"/> are on any steady local clock; the host's
        /// clock read <paramref name="hostTime"/> when it answered, and the local clock this offset is for read
        /// <paramref name="localTime"/> when the answer arrived. False if the round trip makes no sense.
        /// </summary>
        public bool AddSample(double sent, double received, double hostTime, double localTime) {
            var roundTrip = received - sent;
            if (!(roundTrip >= 0.0 && roundTrip <= MAX_ROUND_TRIP) || double.IsNaN(hostTime) || double.IsInfinity(hostTime)) return false;
            m_roundTrips[m_next] = roundTrip;
            m_offsets[m_next] = hostTime + roundTrip * 0.5 - localTime;
            m_next = (m_next + 1) % WINDOW;
            m_count = Math.Min(m_count + 1, WINDOW);
            // Newest first, so an equally short round trip prefers the fresher sample and the estimate follows drift.
            var best = -1;
            for (var age = 0; age < m_count; age++) {
                var i = (m_next - 1 - age + WINDOW) % WINDOW;
                if (best < 0 || m_roundTrips[i] < m_roundTrips[best])
                    best = i;
            }
            m_target = m_offsets[best];
            if (!HasOffset || Math.Abs(m_target - Offset) > SNAP_ERROR) {
                Offset = m_target;
                HasOffset = true;
            }
            return true;
        }

        /// <summary>Moves the applied offset toward the best estimate, by at most <see cref="SLEW_RATE"/> per second.</summary>
        public void Update(double deltaTime) {
            if (!HasOffset || deltaTime <= 0.0) return;
            var step = SLEW_RATE * deltaTime;
            var error = m_target - Offset;
            Offset += Math.Abs(error) <= step ? error : Math.Sign(error) * step;
        }

        /// <summary>Forgets every sample, as when the host changes.</summary>
        public void Reset() {
            m_count = 0;
            m_next = 0;
            m_target = 0.0;
            Offset = 0.0;
            HasOffset = false;
        }
    }
}
