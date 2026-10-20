namespace WhiteKnuckleTogether {
    /// <summary>What a client's trade with Mother earned them (see <see cref="WKTMotherTrades"/>).</summary>
    internal enum WKTMotherRewardKind : byte {
        /// <summary>A perk, by id.</summary>
        Perk,
        /// <summary>A game flag set, by name.</summary>
        Flag,
        /// <summary>She took their item: grubs cannot be eaten for a moment.</summary>
        Given
    }
}
