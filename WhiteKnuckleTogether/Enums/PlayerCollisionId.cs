namespace WhiteKnuckleTogether {
    /// <summary>How players bump into each other (see <see cref="WKTPlayerCollision"/>). Picked by the host in the lobby.</summary>
    public enum PlayerCollisionId : byte {
        /// <summary>Players pass through each other.</summary>
        None,
        /// <summary>Overlapping players drift apart over a moment, so they can still squeeze past each other.</summary>
        Soft,
        /// <summary>Players cannot overlap: each is a solid body to the others.</summary>
        Hard
    }
}
