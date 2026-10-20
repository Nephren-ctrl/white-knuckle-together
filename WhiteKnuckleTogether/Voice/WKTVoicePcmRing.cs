using System;
using System.Threading;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// Decoded voice handed from the main thread, which decodes, to Unity's audio thread, which plays. Lock free for exactly
    /// one writer and one reader: only the writer moves the write index and only the reader moves the read index.
    /// </summary>
    internal sealed class WKTVoicePcmRing {
        private readonly float[] m_samples;
        private readonly int m_mask;
        private int m_write;
        private int m_read;

        /// <summary>Samples waiting to be read. Exact for either thread about its own side.</summary>
        public int Count => Volatile.Read(ref m_write) - Volatile.Read(ref m_read);

        /// <param name="capacity">Rounded up to a power of two.</param>
        public WKTVoicePcmRing(int capacity) {
            var size = 1;
            while (size < capacity)
                size <<= 1;
            m_samples = new float[size];
            m_mask = size - 1;
        }

        /// <summary>Writer thread only. Adds as many of the samples as fit, and returns how many that was.</summary>
        public int Write(float[] source, int offset, int count) {
            var write = m_write;
            var free = m_samples.Length - (write - Volatile.Read(ref m_read));
            if (count > free)
                count = free;
            for (var i = 0; i < count; i++)
                m_samples[(write + i) & m_mask] = source[offset + i];
            Volatile.Write(ref m_write, write + count);
            return count;
        }

        /// <summary>
        /// Reader thread only. Fills <paramref name="count"/> samples of <paramref name="destination"/>; whatever is not
        /// buffered yet reads as silence. Returns how many real samples were read.
        /// </summary>
        public int Read(float[] destination, int offset, int count) {
            var read = m_read;
            var available = Volatile.Read(ref m_write) - read;
            var real = Math.Min(count, available);
            for (var i = 0; i < real; i++)
                destination[offset + i] = m_samples[(read + i) & m_mask];
            if (real < count)
                Array.Clear(destination, offset + real, count - real);
            Volatile.Write(ref m_read, read + real);
            return real;
        }
    }
}
