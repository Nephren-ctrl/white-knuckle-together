namespace WhiteKnuckleTogether {
    /// <summary>
    /// Open while this machine runs an effect every machine runs, such as an explosion going off from a death or item every
    /// machine has. Each player's own copy hurts them, so the host's never reaches remote players through their stand-ins.
    /// What it does to world entities (damage, pushes) is the host's copy's to do, which reaches the machine simulating each
    /// of them, so on a client it only touches the local player. Scopes nest. Pure, so it can be tested without Unity.
    /// Single-threaded.
    /// </summary>
    internal static class WKTSharedEffectScope {
        public static int Depth { get; private set; }
        public static bool IsOpen => Depth > 0;

        /// <summary>Whether a hit or push on a world entity (not the local player) is skipped here.</summary>
        public static bool SkipsEntities(bool isHost) => IsOpen && !isHost;

        public static void Enter() => Depth++;

        public static void Exit() {
            if (Depth > 0)
                Depth--;
        }

        public static void Reset() => Depth = 0;
    }
}
