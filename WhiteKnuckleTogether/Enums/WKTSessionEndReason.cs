namespace WhiteKnuckleTogether {
    /// <summary>Why a session ended without the local player choosing to leave.</summary>
    public enum WKTSessionEndReason : byte {
        None,
        /// <summary>The host left or crashed, and nobody could take over.</summary>
        HostDisconnected,
        Rejected
    }
}
