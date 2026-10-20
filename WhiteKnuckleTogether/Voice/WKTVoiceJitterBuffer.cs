using System;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// Puts one speaker's voice packets back in order and spaces them out evenly despite network jitter. Playback starts once
    /// a few frames are buffered, then takes one frame per pull. A missing frame is rebuilt from the next packet's error
    /// correction data if that packet is already here, or concealed otherwise. Running dry grows the buffering, and long
    /// stretches without trouble shrink it again. After a long silence the stream starts over, as when the speaker stops
    /// talking.
    /// </summary>
    internal sealed class WKTVoiceJitterBuffer {
        public const int CAPACITY = 32;
        public const int MIN_DELAY_FRAMES = 2;
        public const int START_DELAY_FRAMES = 3;
        public const int MAX_DELAY_FRAMES = 8;
        /// <summary>Pulls with nothing left to play before the stream counts as stopped, 200 ms.</summary>
        public const int STOP_AFTER_EMPTY_PULLS = 10;
        /// <summary>Frames played without running dry before the buffering shrinks by one frame, 10 s.</summary>
        public const int SHRINK_AFTER_FRAMES = 500;
        private readonly byte[][] m_packets = new byte[CAPACITY][];
        private readonly int[] m_lengths = new int[CAPACITY];
        private readonly ushort[] m_sequences = new ushort[CAPACITY];
        private readonly bool[] m_filled = new bool[CAPACITY];
        private int m_filledCount;
        private ushort m_next;
        private bool m_playing;
        private int m_emptyPulls;
        private int m_smoothFrames;

        /// <summary>How many frames are buffered before playback starts.</summary>
        public int DelayFrames { get; private set; } = START_DELAY_FRAMES;
        public bool IsPlaying => m_playing;
        public int BufferedCount => m_filledCount;

        public WKTVoiceJitterBuffer() {
            for (var i = 0; i < CAPACITY; i++)
                m_packets[i] = new byte[WKTVoiceConsts.MAX_PACKET_BYTES];
        }

        /// <summary>Stores a received packet. Late, duplicate and oversized packets are dropped. False if it was dropped.</summary>
        public bool Insert(ushort sequence, byte[] data, int offset, int length) {
            if (length <= 0 || length > WKTVoiceConsts.MAX_PACKET_BYTES) return false;
            if (m_playing) {
                var ahead = Delta(sequence, m_next);
                if (ahead < 0) return false;
                // Too far ahead to wait for the frames in between: the stream jumped, so follow it.
                if (ahead >= CAPACITY)
                    Restart();
            }
            var slot = sequence % CAPACITY;
            if (m_filled[slot]) {
                if (m_sequences[slot] == sequence) return false;
                // An older packet still waiting in this slot can never play now.
                m_filledCount--;
            }
            Buffer.BlockCopy(data, offset, m_packets[slot], 0, length);
            m_lengths[slot] = length;
            m_sequences[slot] = sequence;
            m_filled[slot] = true;
            m_filledCount++;
            return true;
        }

        /// <summary>
        /// What to play next, one frame per call. <paramref name="packet"/> and <paramref name="length"/> are the packet to
        /// decode for <see cref="WKTVoiceFrameKind.Packet"/>, or the next packet whose error correction rebuilds this frame for
        /// <see cref="WKTVoiceFrameKind.FromNext"/>.
        /// </summary>
        public WKTVoiceFrameKind Pull(out byte[] packet, out int length) {
            packet = Array.Empty<byte>();
            length = 0;
            if (!m_playing && !TryStart()) return WKTVoiceFrameKind.Silence;
            var slot = m_next % CAPACITY;
            if (IsWaiting(slot, m_next)) {
                packet = m_packets[slot];
                length = m_lengths[slot];
                Take(slot);
                return Advance(WKTVoiceFrameKind.Packet);
            }
            if (m_filledCount == 0) {
                if (++m_emptyPulls == 1)
                    Grow();
                if (m_emptyPulls >= STOP_AFTER_EMPTY_PULLS)
                    Restart();
                // Keeps the decoder's concealment going over a short gap; after that, silence until the stream restarts.
                return m_playing ? WKTVoiceFrameKind.Lost : WKTVoiceFrameKind.Silence;
            }
            var nextSequence = (ushort)(m_next + 1);
            var nextSlot = nextSequence % CAPACITY;
            if (IsWaiting(nextSlot, nextSequence)) {
                packet = m_packets[nextSlot];
                length = m_lengths[nextSlot];
                return Advance(WKTVoiceFrameKind.FromNext);
            }
            return Advance(WKTVoiceFrameKind.Lost);
        }

        /// <summary>How far <paramref name="a"/> is ahead of <paramref name="b"/>, across the wrap of the 16 bit counter.</summary>
        public static int Delta(ushort a, ushort b) => (short)(a - b);

        private bool IsWaiting(int slot, ushort sequence) => m_filled[slot] && m_sequences[slot] == sequence;

        private void Take(int slot) {
            m_filled[slot] = false;
            m_filledCount--;
        }

        private WKTVoiceFrameKind Advance(WKTVoiceFrameKind kind) {
            m_next++;
            m_emptyPulls = 0;
            if (++m_smoothFrames >= SHRINK_AFTER_FRAMES) {
                m_smoothFrames = 0;
                if (DelayFrames > MIN_DELAY_FRAMES)
                    DelayFrames--;
            }
            return kind;
        }

        private bool TryStart() {
            if (m_filledCount < DelayFrames) return false;
            // Plays from the oldest buffered packet.
            var found = false;
            ushort oldest = 0;
            for (var i = 0; i < CAPACITY; i++) {
                if (!m_filled[i]) continue;
                if (!found || Delta(m_sequences[i], oldest) < 0)
                    oldest = m_sequences[i];
                found = true;
            }
            m_next = oldest;
            m_playing = true;
            m_emptyPulls = 0;
            return true;
        }

        private void Grow() {
            m_smoothFrames = 0;
            if (DelayFrames < MAX_DELAY_FRAMES)
                DelayFrames++;
        }

        private void Restart() {
            Array.Clear(m_filled, 0, CAPACITY);
            m_filledCount = 0;
            m_playing = false;
            m_emptyPulls = 0;
        }
    }
}
