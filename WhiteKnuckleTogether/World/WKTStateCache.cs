using System;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// The last state sent for one object, to send a new one only when it changed or a keyframe is due. Holds up to the
    /// capacity it was made with, without allocating after construction. Pure, so it can be tested without Unity.
    /// </summary>
    internal sealed class WKTStateCache {
        private readonly byte[] m_last;
        private double m_lastSent = double.NegativeInfinity;

        /// <summary>The length of the last state kept, or -1 before the first.</summary>
        public int Length { get; private set; } = -1;
        public byte[] Last => m_last;

        public WKTStateCache(int capacity) => m_last = new byte[capacity];

        /// <summary>
        /// Keeps <paramref name="length"/> bytes of <paramref name="data"/> from <paramref name="offset"/> as the state to send, if
        /// they differ from the last state or <paramref name="keyframeInterval"/> seconds passed since it was last due. False when
        /// nothing is due, or the state is longer than the capacity.
        /// </summary>
        public bool Record(byte[] data, int offset, int length, double now, double keyframeInterval) {
            if (length < 0 || length > m_last.Length) return false;
            var same = length == Length && data.AsSpan(offset, length).SequenceEqual(m_last.AsSpan(0, length));
            if (same && now - m_lastSent < keyframeInterval) return false;
            if (!same) {
                Buffer.BlockCopy(data, offset, m_last, 0, length);
                Length = length;
            }
            m_lastSent = now;
            return true;
        }
    }
}
