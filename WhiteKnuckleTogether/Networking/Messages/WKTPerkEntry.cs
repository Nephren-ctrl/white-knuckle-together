namespace WhiteKnuckleTogether {
    /// <summary>One perk a player has, and how many times it is stacked.</summary>
    internal readonly struct WKTPerkEntry {
        public string Id { get; }
        public int Stack { get; }

        public WKTPerkEntry(string id, int stack) {
            Id = id;
            Stack = stack;
        }
    }
}
