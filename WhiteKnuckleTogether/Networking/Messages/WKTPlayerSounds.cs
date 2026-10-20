using System;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// The last <see cref="CAPACITY"/> sounds a player made, newest first, and the wrapping number of the newest. Every
    /// state carries them, so states can be lost: a receiver compares <see cref="Newest"/> with the last number it played
    /// (see <see cref="CountSince"/>) and plays each sound once, when the state that brought it is shown. Unmanaged and
    /// immutable; <see cref="Push"/> returns a copy.
    /// </summary>
    public readonly struct WKTPlayerSounds : IEquatable<WKTPlayerSounds> {
        /// <summary>Four sounds per state: more than a player makes in the three states a receiver may miss in a row.</summary>
        public const int CAPACITY = 4;
        internal const int ENTRY_BITS = 16;
        /// <summary>A number this far or further ahead of the last one played is behind it instead, from before a reset.</summary>
        private const int c_max_ahead = 128;
        /// <summary>One sound per 16 bits, the newest lowest: its id in the low byte, its material in the high one.</summary>
        private readonly ulong m_entries;

        /// <summary>The number of the newest sound. Wraps around.</summary>
        public byte Newest { get; }
        /// <summary>How many sounds are kept, at most <see cref="CAPACITY"/>.</summary>
        public int Count { get; }

        /// <summary>Entries at <paramref name="count"/> and above are dropped. Does not validate the ids.</summary>
        internal WKTPlayerSounds(byte newest, int count, ulong entries) {
            Newest = newest;
            Count = count;
            m_entries = count >= CAPACITY ? entries : entries & ((1UL << (count * ENTRY_BITS)) - 1);
        }

        /// <summary>The same sounds with <paramref name="sound"/> as the newest, numbered one higher. The oldest drops out when full.</summary>
        public WKTPlayerSounds Push(in WKTPlayerSound sound) =>
            new((byte)(Newest + 1), Count < CAPACITY ? Count + 1 : CAPACITY, m_entries << ENTRY_BITS | Pack(sound));

        /// <param name="age">0 is the newest, <see cref="Count"/> - 1 the oldest.</param>
        public WKTPlayerSound Get(int age) {
            if (age < 0 || age >= Count) throw new ArgumentOutOfRangeException(nameof(age), age, "No sound is kept at this age.");
            var entry = m_entries >> (age * ENTRY_BITS);
            return new WKTPlayerSound((WKTPlayerSoundId)(byte)entry, (byte)(entry >> 8));
        }

        /// <summary>
        /// How many of the kept sounds came after the one numbered <paramref name="lastPlayed"/>: those with an age below
        /// the result. Zero when nothing was played yet (negative) and when <see cref="Newest"/> is behind it.
        /// </summary>
        public int CountSince(int lastPlayed) {
            if (lastPlayed < 0) return 0;
            var ahead = (byte)(Newest - lastPlayed);
            if (ahead >= c_max_ahead) return 0;
            return ahead < Count ? ahead : Count;
        }

        public bool Equals(WKTPlayerSounds other) => Newest == other.Newest && Count == other.Count && m_entries == other.m_entries;

        public override bool Equals(object? obj) => obj is WKTPlayerSounds other && Equals(other);

        public override int GetHashCode() => (m_entries.GetHashCode() * 397 ^ Newest) * 397 ^ Count;

        public override string ToString() => $"{Count} sounds up to {Newest}";

        /// <summary>One sound as an entry at age 0. Shift it left by <see cref="ENTRY_BITS"/> per age.</summary>
        internal static ulong Pack(in WKTPlayerSound sound) => (ulong)sound.Material << 8 | (byte)sound.Id;
    }
}
