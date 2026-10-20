using WhiteKnuckleTogether.Networking.LiteNetLib;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// A placed piton was hammered, or is gone. Sent by the machine where it happened to the host, which passes it on. A hammer
    /// hit is replayed everywhere, sound, effects and all, so the piton sinks in for everyone alike.
    /// </summary>
    public readonly struct WKTPitonChange {
        /// <summary>Hit by a hammer for <see cref="Amount"/>.</summary>
        public const byte HAMMERED = 0;
        /// <summary>It broke loose or was taken out of the wall. Whoever caused it already has the piton item, or the item it dropped.</summary>
        public const byte REMOVED = 1;
        /// <summary>The most one hit may hammer in. Rejects garbage without limiting any real hammer.</summary>
        public const float MAX_AMOUNT = 10f;
        public const int SIZE = sizeof(uint) + sizeof(byte) + sizeof(float);

        public readonly uint Id;
        public readonly byte Change;
        public readonly float Amount;

        public WKTPitonChange(uint id, byte change, float amount = 0f) {
            Id = id;
            Change = change;
            Amount = amount;
        }

        public void Write(NetDataWriter writer) {
            writer.Put(Id);
            writer.Put(Change);
            writer.Put(Amount);
        }

        /// <summary>Fails on truncation, id 0, unknown changes and amounts outside [0, <see cref="MAX_AMOUNT"/>].</summary>
        public static bool TryRead(NetDataReader reader, out WKTPitonChange change) {
            change = default;
            if (!reader.TryGetUInt(out var id) || id == 0 || !reader.TryGetByte(out var kind) || kind > REMOVED) return false;
            if (!reader.TryGetFloat(out var amount) || !(amount >= 0f && amount <= MAX_AMOUNT)) return false;
            change = new WKTPitonChange(id, kind, amount);
            return true;
        }
    }
}
