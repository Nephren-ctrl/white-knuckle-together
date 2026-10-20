using WhiteKnuckleTogether.Networking.LiteNetLib;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// Handles one message type. The reader is positioned after the message id and is only valid during the call.
    /// </summary>
    /// <param name="sender">The connected player that sent the message. On clients this is always the host.</param>
    public delegate void WKTMessageHandler(WKTPlayer sender, NetDataReader reader);
}
