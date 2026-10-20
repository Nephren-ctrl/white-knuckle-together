namespace WhiteKnuckleTogether {
    /// <summary>
    /// A sound a player's body makes, which everyone else hears from where that body is. Values are part of the wire
    /// protocol, so existing values must never be reordered. Bump <see cref="WKTNetworkConsts.PROTOCOL_VERSION"/>
    /// when changing them, and keep <see cref="WKTPlayerSound.MAX_ID"/> the last one.
    /// </summary>
    public enum WKTPlayerSoundId : byte {
        None = 0,
        Jump = 1,
        DoubleJump = 2,
        JumpBoost = 3,
        LandSoft = 4,
        LandMedium = 5,
        LandHeavy = 6,
        /// <summary>A landing on a material that has landing sounds of its own, such as water.</summary>
        LandMaterial = 7,
        Mantle = 8,
        /// <summary>A hand slipping off because its grip ran out.</summary>
        FatigueDrop = 9,
        Footstep = 10,
        /// <summary>A hand taking hold of a handhold, with the handhold's material: chains sound like chains.</summary>
        Grab = 11,
        /// <summary>A hand letting go of a handhold or a held button.</summary>
        Release = 12,
        /// <summary>A crouch charging a jump boost (Roach Mode and other jump boost perks).</summary>
        JumpBoostPrime = 13
    }
}
