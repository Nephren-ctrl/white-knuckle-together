namespace WhiteKnuckleTogether {
    /// <summary>
    /// Open while a client runs one of a puppet's hit checks against its own player, such as a barnacle tongue reaching. Hits
    /// inside it may only land on the local player: whatever else the game's code would hit is the host's to decide. Scopes
    /// nest. Pure, so it can be tested without Unity. Single-threaded.
    /// </summary>
    internal static class WKTPuppetHitScope {
        public static int Depth { get; private set; }
        public static bool IsOpen => Depth > 0;

        public static void Enter() => Depth++;

        public static void Exit() {
            if (Depth > 0)
                Depth--;
        }

        public static void Reset() => Depth = 0;
    }
}
