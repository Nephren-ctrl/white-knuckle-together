namespace WhiteKnuckleTogether {
    /// <summary>
    /// Which persistent listeners of one serialized event change the shared world, which only act on the machine that runs
    /// them, and which must never run from the event (a missing target, or a gate the grouping drives instead). Listeners past
    /// <see cref="MAX_TRACKED"/> are treated as world listeners, which only the host runs. Pure, so it can be tested without Unity.
    /// </summary>
    internal readonly struct WKTListenerMasks {
        public const int MAX_TRACKED = 64;

        public ulong World { get; }
        public ulong Local { get; }
        /// <summary>Listeners the event must never run itself.</summary>
        public ulong Skipped { get; }
        /// <summary>How many persistent listeners the event has, including untracked ones.</summary>
        public int Count { get; }

        public bool HasWorld => World != 0 || Count > MAX_TRACKED;
        public bool HasLocal => Local != 0;
        public bool HasSkipped => Skipped != 0;

        private WKTListenerMasks(ulong world, ulong local, ulong skipped, int count) {
            World = world;
            Local = local;
            Skipped = skipped;
            Count = count;
        }

        /// <summary>The masks with listener <paramref name="index"/> added; the count grows to cover it.</summary>
        public WKTListenerMasks With(int index, bool isLocal) {
            var count = index >= Count ? index + 1 : Count;
            if (index >= MAX_TRACKED) return new WKTListenerMasks(World, Local, Skipped, count);
            var bit = 1UL << index;
            return isLocal
                ? new WKTListenerMasks(World & ~bit, Local | bit, Skipped & ~bit, count)
                : new WKTListenerMasks(World | bit, Local & ~bit, Skipped & ~bit, count);
        }

        /// <summary>The masks with listener <paramref name="index"/> counted but never run from the event.</summary>
        public WKTListenerMasks WithSkipped(int index) {
            var count = index >= Count ? index + 1 : Count;
            if (index >= MAX_TRACKED) return new WKTListenerMasks(World, Local, Skipped, count);
            var bit = 1UL << index;
            return new WKTListenerMasks(World & ~bit, Local & ~bit, Skipped | bit, count);
        }

        public bool IsWorld(int index) => index >= MAX_TRACKED ? index < Count : (World & (1UL << index)) != 0;
        public bool IsLocal(int index) => index < MAX_TRACKED && (Local & (1UL << index)) != 0;
    }
}
