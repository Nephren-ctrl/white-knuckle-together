namespace WhiteKnuckleTogether {
    /// <summary>What the host does this frame about the lobby's ready countdown (see <see cref="WKTReadyCheck"/>).</summary>
    public enum WKTReadyStep : byte {
        /// <summary>Nothing changes.</summary>
        None,
        /// <summary>Everyone just became ready: the countdown starts.</summary>
        Start,
        /// <summary>Someone is no longer ready, or joined: the countdown stops.</summary>
        Cancel,
        /// <summary>The countdown ran out with everyone still ready: the run starts.</summary>
        Launch
    }
}
