using WhiteKnuckleTogether.Networking.LiteNetLib;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// A level button's own state (see <see cref="WKTButtonSync"/>): how often it was pressed, whether it can be pressed, and a
    /// toggle button's side. Its events travel on their own; this keeps the button itself alike on every machine.
    /// </summary>
    public readonly struct WKTButtonState {
        public const byte FLAG_HAS_BEEN_PRESSED = 1;
        public const byte FLAG_ACTIVE = 2;
        public const byte FLAG_TOGGLED_ON = 4;
        /// <summary>This state comes from a press just now, which plays its sound and cooldown where it is shown.</summary>
        public const byte FLAG_JUST_PRESSED = 8;
        private const byte c_known_flags = FLAG_HAS_BEEN_PRESSED | FLAG_ACTIVE | FLAG_TOGGLED_ON | FLAG_JUST_PRESSED;

        /// <summary>The button's content key.</summary>
        public ulong Key { get; }
        /// <summary>How many times it was pressed, up to 255.</summary>
        public byte Presses { get; }
        public byte Flags { get; }

        public bool HasBeenPressed => (Flags & FLAG_HAS_BEEN_PRESSED) != 0;
        public bool IsActive => (Flags & FLAG_ACTIVE) != 0;
        public bool IsToggledOn => (Flags & FLAG_TOGGLED_ON) != 0;
        public bool IsJustPressed => (Flags & FLAG_JUST_PRESSED) != 0;

        public WKTButtonState(ulong key, int presses, bool hasBeenPressed, bool active, bool toggledOn, bool justPressed) {
            Key = key;
            Presses = (byte)(presses < 0 ? 0 : presses > byte.MaxValue ? byte.MaxValue : presses);
            Flags = (byte)((hasBeenPressed ? FLAG_HAS_BEEN_PRESSED : 0) | (active ? FLAG_ACTIVE : 0) | (toggledOn ? FLAG_TOGGLED_ON : 0) |
                           (justPressed ? FLAG_JUST_PRESSED : 0));
        }

        private WKTButtonState(ulong key, byte presses, byte flags) {
            Key = key;
            Presses = presses;
            Flags = flags;
        }

        /// <summary>The same state, as kept for players who see it later: no longer a press just now.</summary>
        public WKTButtonState AsSettled() => new(Key, Presses, (byte)(Flags & ~FLAG_JUST_PRESSED));

        public void Write(NetDataWriter writer) {
            writer.Put(Key);
            writer.Put(Presses);
            writer.Put(Flags);
        }

        /// <summary>Fails on truncation, key 0 and unknown flags.</summary>
        public static bool TryRead(NetDataReader reader, out WKTButtonState value) {
            value = default;
            if (!reader.TryGetULong(out var key) || key == 0 || !reader.TryGetByte(out var presses) || !reader.TryGetByte(out var flags) ||
                (flags & ~c_known_flags) != 0) return false;
            value = new WKTButtonState(key, presses, flags);
            return true;
        }
    }
}
