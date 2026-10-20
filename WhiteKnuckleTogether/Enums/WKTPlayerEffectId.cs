namespace WhiteKnuckleTogether {
    /// <summary>Something the host's world did to a remote player, which that player's machine applies to its own player.</summary>
    public enum WKTPlayerEffectId : byte {
        Damage,
        Kill,
        DropEverything,
        Lock,
        SetGravity,
        SetGravityMult,
        ScaleVelocityMagnitude,
        AddRotationVelocity,
        DamageGripStrength,
        AddGripStrength,
        SplatterScreenBlood,
        SetGrappled,
        /// <summary>
        /// A flier the player rides bucked them: damage of the amount and type, grip damage of the vector's x, and, with the
        /// flag, thrown off.
        /// </summary>
        Buck,
        /// <summary>
        /// Pushes from a creature the host decides, such as a Vent Thing dragging the player or a Face knocking them back, summed
        /// over a frame: the vector, from a creature (weakened by knockback resistance) with the flag.
        /// </summary>
        AddForce,
        /// <summary>A recycler on the host took an item the player dropped: the amount of roaches it pays (see <see cref="WKTRecyclers"/>).</summary>
        AddRoaches
    }
}
