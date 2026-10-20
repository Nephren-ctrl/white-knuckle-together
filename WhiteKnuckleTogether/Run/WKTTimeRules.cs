namespace WhiteKnuckleTogether {
    /// <summary>
    /// Time in a multiplayer run. Players share one world in real time, so nothing slows or freezes time on one machine:
    /// hit freezes, slow-time buffs and items, the console's time scale and death slow-motion all run at full speed. Time only
    /// stops while this machine loads (the game's own freeze) or while the run waits for another machine to load
    /// (<see cref="WKTLoadingSync"/>). Pure, so it can be tested without Unity.
    /// </summary>
    internal static class WKTTimeRules {
        /// <summary>The time scale a multiplayer run uses, from the game's base time scale and whether the run is held.</summary>
        public static float Resolve(float baseTimeScale, bool held) => held || baseTimeScale <= 0f ? 0f : 1f;
    }
}
