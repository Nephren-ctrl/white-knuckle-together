namespace WhiteKnuckleTogether {
    internal enum WKTPatchCategory : byte {
        /// <summary>Blocks a method that persists or awards progression while <see cref="WKTProgressionLock"/> is engaged.</summary>
        ProgressionSink,
        /// <summary>Gates progression checks the game itself performs.</summary>
        ProgressionGate,
        /// <summary>Restricts unlocked content to the session's shared progression while <see cref="WKTSharedProgressionOverride"/> is active.</summary>
        ProgressionOverride,
        /// <summary>Starts multiplayer runs and keeps them identical on every machine while <see cref="WKT.Network"/> is in a run.</summary>
        RunSync,
        /// <summary>Lets the host of a synced co-op run simulate the world around every player, not only itself.</summary>
        HostSimulation,
        /// <summary>Makes the host own enemies, items, props and world events in a synced co-op run, with clients showing its copy.</summary>
        WorldSync
    }
}
