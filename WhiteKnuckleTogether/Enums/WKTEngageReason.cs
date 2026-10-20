namespace WhiteKnuckleTogether {
    /// <summary>Why an enemy's engaged player changes, which decides how soon <see cref="WKTAuthorityTable{THandle}"/> lets it.</summary>
    internal enum WKTEngageReason : byte {
        /// <summary>
        /// The simulating machine's AI picked a target. Held back like <see cref="Nearest"/>, so an enemy that just lost a
        /// player does not hand itself straight back to them. Its first target applies at once.
        /// </summary>
        Acquired,
        /// <summary>The simulating machine's AI has no target any more. Always applies at once.</summary>
        Lost,
        /// <summary>Another player is clearly closer. Applies once the current one was held for the minimum hold time.</summary>
        Nearest,
        /// <summary>A player damaged the enemy. Applies unless another damage takeover happened within the damage cooldown.</summary>
        Damage
    }
}
