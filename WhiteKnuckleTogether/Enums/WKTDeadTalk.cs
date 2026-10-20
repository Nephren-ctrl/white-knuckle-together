namespace WhiteKnuckleTogether {
    /// <summary>Whether a dead player's voice reaches the living (see <see cref="ModifierId.DeadCanTalk"/>), for the spectator's HUD.</summary>
    internal enum WKTDeadTalk : byte {
        /// <summary>The lobby's Dead Can Talk is off: only the dead hear them.</summary>
        None,
        /// <summary>Holding focus would let the living hear them.</summary>
        CanTalk,
        /// <summary>The living hear them now.</summary>
        Talking
    }
}