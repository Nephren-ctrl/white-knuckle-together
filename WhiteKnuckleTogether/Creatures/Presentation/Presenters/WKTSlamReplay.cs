using WhiteKnuckleTogether.Networking.LiteNetLib;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// A creature's wall slams, as a counter with the last slam's strength. The simulating machine counts them up; a puppet
    /// replays one whenever the counter it receives moves on, so a lost state only delays a slam. Several slams between two
    /// states replay as one.
    /// </summary>
    internal struct WKTSlamReplay {
        public byte Count;
        public float Strength;
        public bool Flag;
        private byte m_shown;
        private bool m_started;

        /// <summary>Simulating machine: a slam happened.</summary>
        public void Record(float strength, bool flag) {
            Count++;
            Strength = strength;
            Flag = flag;
        }

        public readonly void Write(NetDataWriter writer) {
            writer.Put(Count);
            writer.Put(Strength);
            writer.Put(Flag);
        }

        public bool Read(NetDataReader reader) {
            if (!reader.TryGetByte(out var count) || !reader.TryGetFloat(out var strength) || !reader.TryGetBool(out var flag) || !float.IsFinite(strength)) return false;
            Count = count;
            Strength = strength;
            Flag = flag;
            // The first state only sets where counting starts: slams before this machine saw the creature are not replayed.
            if (m_started) return true;
            m_started = true;
            m_shown = count;
            return true;
        }

        /// <summary>Puppet: whether a slam is due to be shown, marking it shown.</summary>
        public bool TakeDue() {
            if (!m_started || m_shown == Count) return false;
            m_shown = Count;
            return true;
        }
    }
}
