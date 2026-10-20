namespace WhiteKnuckleTogether {
    /// <summary>
    /// The campaign's shortcuts, which the host turns on or off for a multiplayer run in the lobby. Sent as one bit each in a
    /// byte, so there can be at most 8. The order is the order the route reaches them.
    /// </summary>
    public enum ShortcutId : byte {
        TangledSink,
        ExpulsionChute,
        RottenDuct,
        HadalDiver
    }
}
