namespace WhiteKnuckleTogether {
    /// <summary>The state of a door the host owns, as sent to clients.</summary>
    internal enum WKTGateState : byte {
        Open = 0,
        /// <summary>Closed behind the group, or closed by the level itself.</summary>
        Closed = 1
    }
}
