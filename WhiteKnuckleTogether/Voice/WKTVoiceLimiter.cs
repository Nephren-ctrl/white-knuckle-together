using System;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// Turns a voice up by a gain and keeps every sample under <see cref="CEILING"/>, so a volume above 100% makes speech
    /// louder instead of flattening its peaks. A lookahead limiter: the voice is delayed by <see cref="LOOKAHEAD"/> samples, so
    /// the gain can start easing down before a peak arrives and be just low enough when it does. The gain falls as a straight
    /// ramp (the minimum gain any sample in the window needs, averaged over the window) and recovers over
    /// <see cref="RELEASE_SECONDS"/>, so it never follows the waveform itself, which is what makes a plain peak limiter
    /// sound harsh. User gain changes are eased across the frame so moving a slider does not click. Allocates nothing.
    /// </summary>
    internal sealed class WKTVoiceLimiter {
        /// <summary>The loudest any sample is allowed to get.</summary>
        public const float CEILING = 0.8f;
        /// <summary>How far ahead the limiter looks, in samples (3 ms): also how late the voice comes out.</summary>
        public const int LOOKAHEAD = 144;
        public const float RELEASE_SECONDS = 0.08f;
        private const int c_window = LOOKAHEAD + 1;
        private static readonly float s_release = 1f - (float)Math.Exp(-1.0 / (RELEASE_SECONDS * WKTVoiceConsts.SAMPLE_RATE));
        /// <summary>The voice, delayed by <see cref="LOOKAHEAD"/>.</summary>
        private readonly float[] m_delay = new float[LOOKAHEAD];
        /// <summary>The gain each of the last <see cref="c_window"/> samples needs, held at the window's minimum (see <see cref="PushMinimum"/>).</summary>
        private readonly float[] m_held = new float[c_window];
        /// <summary>A monotonic queue of the needed gains in the window: positions and values, smallest at the head.</summary>
        private readonly long[] m_queuePositions = new long[c_window];
        private readonly float[] m_queueValues = new float[c_window];
        private int m_queueHead;
        private int m_queueCount;
        private long m_position;
        private double m_heldSum;
        private float m_gain;
        private float m_userGain;

        public WKTVoiceLimiter() => Reset();

        public void Reset() {
            Array.Clear(m_delay, 0, m_delay.Length);
            for (var i = 0; i < m_held.Length; i++)
                m_held[i] = 1f;
            m_heldSum = c_window;
            m_queueHead = 0;
            m_queueCount = 0;
            m_position = 0;
            m_gain = 1f;
            m_userGain = 1f;
        }

        /// <summary>
        /// Scales <paramref name="samples"/> (at <see cref="WKTVoiceConsts.SAMPLE_RATE"/>) by <paramref name="gain"/>, then
        /// limits them. What comes out is <see cref="LOOKAHEAD"/> samples behind what goes in.
        /// </summary>
        public void Process(Span<float> samples, float gain) {
            if (samples.Length == 0) return;
            if (gain < 0f)
                gain = 0f;
            var step = (gain - m_userGain) / samples.Length;
            for (var i = 0; i < samples.Length; i++) {
                var value = samples[i] * (m_userGain + step * (i + 1));
                var magnitude = Math.Abs(value);
                var needed = magnitude > CEILING ? CEILING / magnitude : 1f;
                var slot = (int)(m_position % c_window);
                var minimum = PushMinimum(m_position, needed);
                m_heldSum += minimum - m_held[slot];
                m_held[slot] = minimum;
                // The average of the held minimums is at most the gain a peak needs by the time it leaves the delay.
                var target = (float)(m_heldSum / c_window);
                m_gain = target < m_gain ? target : m_gain + (target - m_gain) * s_release;
                var delaySlot = (int)(m_position % LOOKAHEAD);
                var delayed = m_delay[delaySlot];
                m_delay[delaySlot] = value;
                var output = delayed * m_gain;
                // Rounding in the running sum must never let a sample through above the ceiling.
                samples[i] = output > CEILING ? CEILING : output < -CEILING ? -CEILING : output;
                m_position++;
            }
            // Drift in the running sum is resummed now and then.
            if (m_position % (c_window * 64) < samples.Length)
                Resum();
            m_userGain = gain;
        }

        /// <summary>Adds the gain a new sample needs and returns the smallest needed in the window ending at it.</summary>
        private float PushMinimum(long position, float needed) {
            if (m_queueCount > 0 && m_queuePositions[m_queueHead] <= position - c_window) {
                m_queueHead = (m_queueHead + 1) % c_window;
                m_queueCount--;
            }
            while (m_queueCount > 0) {
                var tail = (m_queueHead + m_queueCount - 1) % c_window;
                if (m_queueValues[tail] < needed) break;
                m_queueCount--;
            }
            var slot = (m_queueHead + m_queueCount) % c_window;
            m_queuePositions[slot] = position;
            m_queueValues[slot] = needed;
            m_queueCount++;
            return m_queueValues[m_queueHead];
        }

        private void Resum() {
            var sum = 0.0;
            foreach (var held in m_held)
                sum += held;
            m_heldSum = sum;
        }
    }
}
