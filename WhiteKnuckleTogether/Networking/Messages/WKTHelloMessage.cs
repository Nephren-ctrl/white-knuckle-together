using WhiteKnuckleTogether.Networking.LiteNetLib;

namespace WhiteKnuckleTogether {
    /// <summary>The first message a client sends after connecting. The server validates it before admitting the player.</summary>
    public readonly struct WKTHelloMessage {
        public ushort ProtocolVersion { get; }
        public string GameVersion { get; }
        public string ModVersion { get; }

        public WKTHelloMessage(ushort protocolVersion, string gameVersion, string modVersion) {
            ProtocolVersion = protocolVersion;
            GameVersion = gameVersion;
            ModVersion = modVersion;
        }

        public void Write(NetDataWriter writer) {
            writer.Put(ProtocolVersion);
            writer.Put(GameVersion, WKTNetworkConsts.MAX_VERSION_LENGTH);
            writer.Put(ModVersion, WKTNetworkConsts.MAX_VERSION_LENGTH);
        }

        /// <summary>Reads the payload after the message id. Returns false on truncated or invalid data instead of throwing.</summary>
        public static bool TryRead(NetDataReader reader, out WKTHelloMessage message) {
            message = default;
            if (!reader.TryGetUShort(out var protocolVersion)) return false;
            if (!WKTReaderUtility.TryGetBoundedString(reader, WKTNetworkConsts.MAX_VERSION_LENGTH, out var gameVersion)) return false;
            if (!WKTReaderUtility.TryGetBoundedString(reader, WKTNetworkConsts.MAX_VERSION_LENGTH, out var modVersion)) return false;
            message = new WKTHelloMessage(protocolVersion, gameVersion, modVersion);
            return true;
        }
    }
}
