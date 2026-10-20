namespace WhiteKnuckleTogether {
    /// <summary>
    /// Set while a puppet's melee hit check is replayed against the local player. The swing's end event and miss sound come
    /// from the machine simulating it already, so the replay keeps them quiet here; what a hit on the local player sets off
    /// still plays. Events are matched by reference, so this stays pure and can be tested without Unity. Single-threaded.
    /// </summary>
    internal static class WKTMeleeReplay {
        private static object? s_endEvent;
        private static string? s_missSound;

        public static bool IsOpen { get; private set; }

        public static void Enter(object? endEvent, string? missSound) {
            IsOpen = true;
            s_endEvent = endEvent;
            s_missSound = missSound;
        }

        public static void Exit() {
            IsOpen = false;
            s_endEvent = null;
            s_missSound = null;
        }

        public static bool SuppressesEvent(object unityEvent) => IsOpen && s_endEvent != null && ReferenceEquals(unityEvent, s_endEvent);

        public static bool SuppressesSound(string? sound) => IsOpen && !string.IsNullOrEmpty(sound) && sound == s_missSound;
    }
}
