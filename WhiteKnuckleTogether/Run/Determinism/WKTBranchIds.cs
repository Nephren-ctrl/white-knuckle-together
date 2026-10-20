using System.Collections.Generic;
using UnityEngine;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// <c>UT_BranchGenerator.Generate</c> (shortcuts, the ladder) names each branch it builds by appending
    /// <c>Time.frameCount</c> to the branch's id, which differs on every machine. Branch ids seed the branch's generation and
    /// key its levels (<see cref="WKTContentSeeding"/>), so in a multiplayer run the suffix comes from the generator's content
    /// key and how many times that generator built before instead: the same on every machine, and still unique.
    /// </summary>
    internal static class WKTBranchIds {
        private static readonly Dictionary<ulong, uint> s_counts = new();

        /// <summary>The suffix for a branch <paramref name="generator"/> builds now: the frame count outside multiplayer runs.</summary>
        public static int GetSuffix(UT_BranchGenerator generator) {
            if (!WKTContentSeeding.IsActive || !WKTContentSeeding.TryGetKey(generator, out var key)) return Time.frameCount;
            s_counts.TryGetValue(key, out var count);
            s_counts[key] = count + 1;
            return GetSuffix(key, count);
        }

        /// <summary>A positive suffix from a generator's key and its count of branches built before.</summary>
        public static int GetSuffix(ulong generatorKey, uint count) => (int)(WKTStableHash.Mix(generatorKey, count) & int.MaxValue);

        /// <summary>Forgets the counts, for a new run.</summary>
        public static void Clear() => s_counts.Clear();
    }
}
