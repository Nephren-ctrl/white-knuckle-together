using System;
using System.Collections.Generic;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// Blocks every achievement, meta-progression, unlock, stat, save, and leaderboard sink while a multiplayer run is
    /// in progress. Sticky by design: it stays engaged after the network stops and is only released once the run's
    /// in-memory progression has been discarded (see <see cref="WKTProgressionGuard"/>), because the game saves on scene
    /// unload and application quit, which can happen after the session is gone. Never engages in singleplayer.
    /// </summary>
    internal static class WKTProgressionLock {
        private static readonly HashSet<string> s_reportedSinks = new();

        public static bool IsLocked { get; private set; }
        /// <summary>Receives lock state changes and the first blocked call of each sink per engagement.</summary>
        public static Action<string>? Log { get; set; }

        public static void Engage() {
            if (IsLocked) return;
            IsLocked = true;
            s_reportedSinks.Clear();
            Log?.Invoke("Progression lock engaged. Nothing can be unlocked, saved, or submitted until it is released.");
        }

        public static void Release() {
            if (!IsLocked) return;
            IsLocked = false;
            Log?.Invoke($"Progression lock released after blocking {s_reportedSinks.Count} distinct sink(s).");
        }

        /// <summary>The single decision point for every sink patch. Returns false when the original must not run.</summary>
        public static bool ShouldRunOriginal(string sinkName) {
            if (!IsLocked) return true;
            if (s_reportedSinks.Add(sinkName))
                Log?.Invoke($"Blocked progression sink {sinkName}.");
            return false;
        }
    }
}
