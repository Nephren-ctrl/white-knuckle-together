using WhiteKnuckleTogether.Networking.LiteNetLib;

namespace WhiteKnuckleTogether {
    /// <summary>The wire form of a player, used by the Welcome and PlayerJoined messages.</summary>
    public readonly struct WKTPlayerEntry {
        public const int SIZE = sizeof(byte) + sizeof(ulong) + sizeof(bool);

        public byte Id { get; }
        public ulong SteamId { get; }
        public bool IsHost { get; }

        public WKTPlayerEntry(byte id, ulong steamId, bool isHost) {
            Id = id;
            SteamId = steamId;
            IsHost = isHost;
        }

        public void Write(NetDataWriter writer) {
            writer.Put(Id);
            writer.Put(SteamId);
            writer.Put(IsHost);
        }

        public static bool TryRead(NetDataReader reader, out WKTPlayerEntry entry) {
            entry = default;
            if (reader.AvailableBytes < SIZE) return false;
            entry = new WKTPlayerEntry(reader.GetByte(), reader.GetULong(), reader.GetBool());
            return true;
        }
    }
}
