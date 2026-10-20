namespace WhiteKnuckleTogether {
    /// <summary>What accepting a Steam invite does. See <see cref="WKTLobbyJoinRequest"/>.</summary>
    public enum WKTLobbyJoinAction : byte {
        /// <summary>Already in that lobby.</summary>
        Ignore,
        /// <summary>Not on the main menu: join once back there, so a run is never put onto the network.</summary>
        Defer,
        Join,
        /// <summary>In another session: leave it, handing over host if needed, then join.</summary>
        LeaveThenJoin
    }
}
