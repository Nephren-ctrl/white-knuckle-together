using WhiteKnuckleTogether.Networking.LiteNetLib;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// One link of a rope on the wire: its direction in the previous link's frame and its length, quantized by
    /// <see cref="WKTRopeCodec"/>. Part of the wire protocol.
    /// </summary>
    public readonly struct WKTRopeLink {
        public const int SIZE = 5;
        /// <summary>The direction's two octahedral coordinates, 12 bits each.</summary>
        public const uint MAX_DIRECTION = (1u << 24) - 1;

        public uint Direction { get; }
        /// <summary>The length as a fraction of the rope's longest possible link, out of <see cref="ushort.MaxValue"/>.</summary>
        public ushort Length { get; }

        public WKTRopeLink(uint direction, ushort length) {
            Direction = direction & MAX_DIRECTION;
            Length = length;
        }

        public void Write(NetDataWriter writer) {
            writer.Put((byte)Direction);
            writer.Put((byte)(Direction >> 8));
            writer.Put((byte)(Direction >> 16));
            writer.Put(Length);
        }

        /// <summary>Every 24 bit direction and 16 bit length is valid, so only truncation fails.</summary>
        public static bool TryRead(NetDataReader reader, out WKTRopeLink link) {
            link = default;
            if (reader.AvailableBytes < SIZE) return false;
            var direction = reader.GetByte() | ((uint)reader.GetByte() << 8) | ((uint)reader.GetByte() << 16);
            link = new WKTRopeLink(direction, reader.GetUShort());
            return true;
        }
    }
}
