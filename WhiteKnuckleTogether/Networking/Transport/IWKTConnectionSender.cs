using Steamworks.Data;

namespace WhiteKnuckleTogether {
    /// <summary>What <see cref="WKTServer"/> does to its connections. Lets tests run the server without Steam.</summary>
    internal interface IWKTConnectionSender {
        void Send(Connection connection, byte[] data, int length, SendType sendType);
        void Close(Connection connection, bool linger, string reason);
    }
}
