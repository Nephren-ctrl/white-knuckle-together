namespace WhiteKnuckleTogether {
    /// <summary>
    /// Hashes that come out the same on every machine and every run, unlike <see cref="string.GetHashCode()"/>. Used to
    /// derive keys and seeds that all players agree on without talking.
    /// </summary>
    internal static class WKTStableHash {
        private const ulong c_fnv_offset = 14695981039346656037UL;
        private const ulong c_fnv_prime = 1099511628211UL;

        /// <summary>FNV-1a over the UTF-16 code units of <paramref name="value"/>. Null hashes like the empty string.</summary>
        public static ulong Of(string? value) {
            var hash = c_fnv_offset;
            if (value == null) return hash;
            foreach (var c in value) {
                hash ^= c;
                hash *= c_fnv_prime;
            }
            return hash;
        }

        /// <summary>Combines two values into one well-mixed key. Order matters: Mix(a, b) differs from Mix(b, a).</summary>
        public static ulong Mix(ulong a, ulong b) => Finalize(a * 0x9E3779B97F4A7C15UL + Finalize(b + 0x632BE59BD9B4E019UL));

        /// <summary>A seed for <c>Random.InitState</c> from a key, using all of its bits.</summary>
        public static int ToSeed(ulong key) => (int)(key ^ (key >> 32));

        /// <summary>The splitmix64 finalizer: every input bit affects every output bit.</summary>
        private static ulong Finalize(ulong x) {
            x = (x ^ (x >> 30)) * 0xBF58476D1CE4E5B9UL;
            x = (x ^ (x >> 27)) * 0x94D049BB133111EBUL;
            return x ^ (x >> 31);
        }
    }
}
