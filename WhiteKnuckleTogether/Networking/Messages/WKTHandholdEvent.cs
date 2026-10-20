using WhiteKnuckleTogether.Networking.LiteNetLib;

namespace WhiteKnuckleTogether {
    /// <summary>Something a timed handhold did that everyone sees, with <see cref="ModifierId.ShareTimedHandholds"/> on.</summary>
    public readonly struct WKTHandholdEvent {
        /// <summary>A breakable handhold broke.</summary>
        public const byte BROKE = 0;
        /// <summary>The handhold's <see cref="Module"/>th module, a timer, ran out.</summary>
        public const byte TIMER_FINISHED = 1;
        public const int SIZE = sizeof(ulong) + sizeof(byte) * 2;

        public readonly ulong Key;
        public readonly byte Kind;
        public readonly byte Module;

        public WKTHandholdEvent(ulong key, byte kind, byte module = 0) {
            Key = key;
            Kind = kind;
            Module = module;
        }

        public void Write(NetDataWriter writer) {
            writer.Put(Key);
            writer.Put(Kind);
            writer.Put(Module);
        }

        /// <summary>Fails on truncation, key 0 and unknown kinds.</summary>
        public static bool TryRead(NetDataReader reader, out WKTHandholdEvent handholdEvent) {
            handholdEvent = default;
            if (!reader.TryGetULong(out var key) || key == 0 || !reader.TryGetByte(out var kind) || kind > TIMER_FINISHED ||
                !reader.TryGetByte(out var module)) return false;
            handholdEvent = new WKTHandholdEvent(key, kind, module);
            return true;
        }
    }
}
