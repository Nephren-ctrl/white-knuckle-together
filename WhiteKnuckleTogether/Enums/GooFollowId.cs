namespace WhiteKnuckleTogether {
    /// <summary>
    /// Which height the shared death goo keeps up with in a synced co-op run, in place of the single player it follows in
    /// singleplayer. Picked by the host in the lobby. Only living players count.
    /// </summary>
    public enum GooFollowId : byte {
        /// <summary>The average height of every player. The default.</summary>
        Average,
        /// <summary>The lowest player: the goo presses whoever is at the bottom, as it would alone.</summary>
        Lowest,
        /// <summary>The highest player: the goo races to keep up with the leader.</summary>
        Highest
    }
}
