namespace WhiteKnuckleTogether {
    public enum WKTRejectReason : byte {
        None,
        Malformed,
        ProtocolMismatch,
        GameVersionMismatch,
        ModVersionMismatch,
        LobbyFull,
        AlreadyConnected,
        HandshakeTimeout,
        /// <summary>The host is in a run. Joining mid-run is not supported.</summary>
        RunInProgress,
        /// <summary>The host kicked this player, who may join again.</summary>
        Kicked,
        /// <summary>The host banned this player, who cannot come back for the rest of the session.</summary>
        Banned
    }
}
