namespace WhiteKnuckleTogether {
    /// <summary>
    /// Open while code runs that every machine runs for itself, such as a puppet's allowed ticks or effects the mod replays.
    /// What an entity does inside it (its sounds, its own events, pushes it gets) stays on this machine: a puppet plays it,
    /// and the simulating machine does not send it. Scopes nest. Pure, so it can be tested without Unity. Single-threaded.
    /// </summary>
    internal static class WKTLocalReplayScope {
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
