using Steamworks.Data;

namespace WhiteKnuckleTogether {
    internal sealed class WKTSteamConnectionSender : IWKTConnectionSender {
        public static readonly WKTSteamConnectionSender Instance = new();

        private WKTSteamConnectionSender() { }

        public void Send(Connection connection, byte[] data, int length, SendType sendType) =>
            connection.SendMessage(data, 0, length, sendType);

        public void Close(Connection connection, bool linger, string reason) => connection.Close(linger, 0, reason);
    }
}
