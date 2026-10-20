namespace WhiteKnuckleTogether {
    /// <summary>What an enemy did to a client's own player, as reported to the host in a <see cref="WKTPlayerHit"/>.</summary>
    public enum WKTPlayerHitKind : byte {
        /// <summary>An attack landed. Nothing changes for the enemy beyond what the host already decided.</summary>
        Damaged,
        /// <summary>The enemy took hold of the player, such as a tongue grabbing them. The first claim wins.</summary>
        Grabbed,
        /// <summary>The player got free of a hold on their own screen, such as by breaking the tongue or climbing away.</summary>
        Released,
        /// <summary>
        /// The player broke part of the enemy on their own screen, such as a tongue, whatever it held. Damage is the
        /// attacker's to decide, so the enemy's own machine breaks it too.
        /// </summary>
        Broken,
        /// <summary>
        /// The player set off a creature by touching it on their own screen, such as a gasbag popping or a roach squished
        /// underfoot. The creature already died there; the host kills it for everyone else.
        /// </summary>
        Triggered
    }
}
