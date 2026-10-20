namespace WhiteKnuckleTogether {
    /// <summary>
    /// Keys of world objects nested inside another one, such as the barnacle riding a barnacle zombie: the parent object's key
    /// mixed with the names and sibling indices on the way down from it, so every machine that built the same prefab agrees
    /// however the parent got its key (placed in a level, spawned at runtime, spawned on a client from the host's bind).
    /// Pure, so it can be tested without Unity.
    /// </summary>
    internal static class WKTNestedKeys {
        /// <summary>The bit marking objects a client never has a copy of; a nested object always comes with its parent's copy.</summary>
        public const ulong RUNTIME_BIT = 1UL << 63;
        private static readonly ulong s_salt = WKTStableHash.Of("WKT.Nested");

        /// <summary>The path so far, before any step.</summary>
        public static ulong EmptyPath => WKTStableHash.Of(null);

        /// <summary>Adds one level of the path, from a child up toward the parent object.</summary>
        public static ulong Step(ulong path, string? name, int siblingIndex) => WKTStableHash.Mix(WKTStableHash.Mix(path, WKTStableHash.Of(name)), (uint)siblingIndex);

        public static ulong Derive(ulong parentKey, ulong path) => WKTStableHash.Mix(WKTStableHash.Mix(s_salt, parentKey), path) & ~RUNTIME_BIT;
    }
}
