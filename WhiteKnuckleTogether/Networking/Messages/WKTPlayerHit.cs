using WhiteKnuckleTogether.Networking.LiteNetLib;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// Client to host: an enemy hit the client's own player on the client's screen. The client already applied the hit; the
    /// host applies what follows for the enemy, such as a tongue now holding that player. <see cref="Part"/> picks a part of
    /// the enemy, such as which tongue.
    /// </summary>
    public readonly struct WKTPlayerHit {
        public const int SIZE = sizeof(ushort) + sizeof(byte) + sizeof(byte);

        public readonly ushort Handle;
        public readonly WKTPlayerHitKind Kind;
        public readonly byte Part;

        public WKTPlayerHit(ushort handle, WKTPlayerHitKind kind, byte part) {
            Handle = handle;
            Kind = kind;
            Part = part;
        }

        public void Write(NetDataWriter writer) {
            writer.Put(Handle);
            writer.Put((byte)Kind);
            writer.Put(Part);
        }

        /// <summary>Fails on truncation, handle 0 and unknown kinds.</summary>
        public static bool TryRead(NetDataReader reader, out WKTPlayerHit hit) {
            hit = default;
            if (!reader.TryGetUShort(out var handle) || handle == 0 || !reader.TryGetByte(out var kind) || kind > (byte)WKTPlayerHitKind.Triggered) return false;
            if (!reader.TryGetByte(out var part)) return false;
            hit = new WKTPlayerHit(handle, (WKTPlayerHitKind)kind, part);
            return true;
        }
    }
}
