namespace WhiteKnuckleTogether {
    /// <summary>
    /// The lobby's ready-up, decided on the host: once every player in the session is ready, a countdown of
    /// <see cref="WKTLobbyConsts.READY_COUNTDOWN"/> seconds starts, and the run starts when it runs out. Anyone unreadying, or
    /// a new player joining, stops it. Pure, so it can be tested without the game.
    /// </summary>
    public static class WKTReadyCheck {
        public static WKTReadyStep Step(int ready, int total, bool counting, float now, float end) {
            var allReady = total > 0 && ready >= total;
            if (!counting) return allReady ? WKTReadyStep.Start : WKTReadyStep.None;
            if (!allReady) return WKTReadyStep.Cancel;
            return now >= end ? WKTReadyStep.Launch : WKTReadyStep.None;
        }

        /// <summary>The whole seconds a countdown shows with <paramref name="left"/> seconds to go: 3, 2, 1, then 0.</summary>
        public static int ShownSeconds(float left) => left <= 0f ? 0 : (int)System.Math.Ceiling(left);
    }
}
