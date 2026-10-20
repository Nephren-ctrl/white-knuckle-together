namespace WhiteKnuckleTogether {
    /// <summary>Whether a lobby in the lobby browser can be joined, and if not, why.</summary>
    public enum WKTLobbyListingStatus : byte {
        Joinable,
        /// <summary>The host plays another version of the mod.</summary>
        ModVersionMismatch,
        /// <summary>The host plays another version of the game, which can generate a different world.</summary>
        GameVersionMismatch,
        /// <summary>The host is in a run. Joining mid-run is not supported.</summary>
        InRun,
        Full
    }
}
