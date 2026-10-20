namespace WhiteKnuckleTogether {
    /// <summary>How the players of a multiplayer run play together. Picked by the host in the lobby.</summary>
    public enum PlayModeId : byte {
        /// <summary>Everyone climbs the same world together.</summary>
        Coop,
        /// <summary>
        /// Everyone races up the same world. An orb follows the leader and hurts anyone left outside it; there are no revives.
        /// The last one alive, or the first to finish, wins.
        /// </summary>
        Race
    }
}
