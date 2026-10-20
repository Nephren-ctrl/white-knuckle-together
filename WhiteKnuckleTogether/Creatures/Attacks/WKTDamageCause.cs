namespace WhiteKnuckleTogether {
    /// <summary>
    /// The player whose hit is being applied right now, so what the hit sets off (an entity's damage-effect events, such as a
    /// grub enraging Mother) knows whom it is for. Set around each hit applied here and around an event a client relays to the
    /// host. <see cref="NOBODY"/> outside a player's hit. Scopes nest by saving the previous cause. Pure, so it can be tested
    /// without Unity. Single-threaded.
    /// </summary>
    internal static class WKTDamageCause {
        public const byte NOBODY = WKTEntityAuthority.NOBODY;

        public static byte Current { get; private set; } = NOBODY;

        /// <summary>Makes <paramref name="cause"/> current and returns the previous cause, which <see cref="Exit"/> restores.</summary>
        public static byte Enter(byte cause) {
            var previous = Current;
            Current = cause;
            return previous;
        }

        public static void Exit(byte previous) => Current = previous;

        public static void Reset() => Current = NOBODY;
    }
}
