namespace WhiteKnuckleTogether {
    /// <summary>Collects captured samples arriving in pieces of any size into whole Opus frames.</summary>
    internal sealed class WKTVoiceFrameBuffer {
        private readonly float[] m_samples;
        private int m_count;

        public int Count => m_count;

        public WKTVoiceFrameBuffer(int capacity) => m_samples = new float[capacity];

        /// <summary>Adds samples. Past the capacity the oldest are dropped, which only happens if frames are not read for a while.</summary>
        public void Write(float[] source, int offset, int count) {
            if (count > m_samples.Length) {
                offset += count - m_samples.Length;
                count = m_samples.Length;
            }
            var overflow = m_count + count - m_samples.Length;
            if (overflow > 0) {
                System.Array.Copy(m_samples, overflow, m_samples, 0, m_count - overflow);
                m_count -= overflow;
            }
            System.Array.Copy(source, offset, m_samples, m_count, count);
            m_count += count;
        }

        /// <summary>Takes the oldest whole frame of <see cref="WKTVoiceConsts.FRAME_SAMPLES"/> into <paramref name="frame"/>, if there is one.</summary>
        public bool TryReadFrame(float[] frame) {
            const int frame_samples = WKTVoiceConsts.FRAME_SAMPLES;
            if (m_count < frame_samples) return false;
            System.Array.Copy(m_samples, 0, frame, 0, frame_samples);
            m_count -= frame_samples;
            System.Array.Copy(m_samples, frame_samples, m_samples, 0, m_count);
            return true;
        }

        public void Clear() => m_count = 0;
    }
}
