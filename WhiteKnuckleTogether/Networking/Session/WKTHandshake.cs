using System;

namespace WhiteKnuckleTogether {
    /// <summary>Decides whether a connecting client may join. Pure, so it can be tested without Steam.</summary>
    public static class WKTHandshake {
        /// <param name="hello">The client's hello message.</param>
        /// <param name="gameVersion">The host's game version.</param>
        /// <param name="modVersion">The host's mod version.</param>
        /// <param name="playerCount">Players already admitted, including the host.</param>
        /// <param name="maxPlayers">Lobby capacity, including the host.</param>
        /// <param name="alreadyConnected">Whether the client's Steam account is already in the session.</param>
        /// <param name="runInProgress">Whether the host is in a multiplayer run.</param>
        /// <param name="banned">Whether the host banned the client's Steam account this session.</param>
        public static WKTRejectReason Validate(in WKTHelloMessage hello, string gameVersion, string modVersion,
                                               int playerCount, int maxPlayers, bool alreadyConnected,
                                               bool runInProgress, bool banned = false) {
            // Protocol first: if it differs, the rest of the message may not even mean the same thing.
            if (hello.ProtocolVersion != WKTNetworkConsts.PROTOCOL_VERSION)
                return WKTRejectReason.ProtocolMismatch;
            if (!string.Equals(hello.ModVersion, modVersion, StringComparison.Ordinal))
                return WKTRejectReason.ModVersionMismatch;
            // Different game builds can generate different worlds from the same seed.
            if (!string.Equals(hello.GameVersion, gameVersion, StringComparison.Ordinal))
                return WKTRejectReason.GameVersionMismatch;
            if (banned)
                return WKTRejectReason.Banned;
            if (alreadyConnected)
                return WKTRejectReason.AlreadyConnected;
            if (runInProgress)
                return WKTRejectReason.RunInProgress;
            if (playerCount >= maxPlayers)
                return WKTRejectReason.LobbyFull;
            return WKTRejectReason.None;
        }
    }
}
