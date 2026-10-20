using System;
using System.Collections.Generic;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// Gives every keyed component its own random stream for each of its methods. Entering a method swaps that stream's
    /// state into the global generator and exiting swaps it back out, so what a component rolls depends only on its key and
    /// how often that method ran, never on what else drew from the generator first. Every machine derives the same keys,
    /// so level contents roll the same everywhere.
    /// <para>
    /// Objects created while a stream is entered are keyed from that stream: the n-th object it creates gets
    /// <see cref="WKTStableHash.Mix"/> of the stream key and n, so spawned objects roll the same too.
    /// </para>
    /// Pure apart from the injected generator, so it can be tested without Unity.
    /// </summary>
    internal sealed class WKTRandomStreams<TState> where TState : struct {
        public const int MAX_DEPTH = 64;
        private readonly IRandomStateAccess<TState> m_random;
        private readonly Func<int, TState> m_createState;
        private readonly Dictionary<int, ulong> m_componentKeys = new(4096);
        private readonly Dictionary<ulong, Stream> m_streams = new(1024);
        private readonly ulong[] m_scopeStack = new ulong[MAX_DEPTH];
        private int m_depth;

        /// <summary>Whether a keyed stream is entered, so objects created now can be keyed from it.</summary>
        public bool IsInScope => m_depth > 0;
        public int KeyCount => m_componentKeys.Count;
        public int StreamCount => m_streams.Count;

        /// <param name="random">The global generator streams are swapped into.</param>
        /// <param name="createState">The generator state a stream starts from for a seed. Must not change the global state.</param>
        public WKTRandomStreams(IRandomStateAccess<TState> random, Func<int, TState> createState) {
            m_random = random ?? throw new ArgumentNullException(nameof(random));
            m_createState = createState ?? throw new ArgumentNullException(nameof(createState));
        }

        public void SetKey(int componentId, ulong key) => m_componentKeys[componentId] = key;

        public bool TryGetKey(int componentId, out ulong key) => m_componentKeys.TryGetValue(componentId, out key);

        public void RemoveKey(int componentId) => m_componentKeys.Remove(componentId);

        /// <summary>
        /// The key for the next object created by the innermost entered stream. Only valid while <see cref="IsInScope"/>.
        /// </summary>
        public ulong NextSpawnKey() {
            if (m_depth == 0) throw new InvalidOperationException("No stream is entered.");
            var streamKey = m_scopeStack[m_depth - 1];
            var stream = m_streams[streamKey];
            stream.SpawnCount++;
            m_streams[streamKey] = stream;
            return WKTStableHash.Mix(streamKey, stream.SpawnCount);
        }

        /// <summary>
        /// Swaps in the stream of <paramref name="componentKey"/>'s method <paramref name="methodSalt"/>, creating it on first
        /// use. Always pair with <see cref="Exit"/>, even when the method throws.
        /// </summary>
        public Token Enter(ulong componentKey, ulong methodSalt) {
            if (m_depth == MAX_DEPTH) return default;
            var streamKey = WKTStableHash.Mix(componentKey, methodSalt);
            var outer = m_random.State;
            if (!m_streams.TryGetValue(streamKey, out var stream)) {
                stream = new Stream { State = m_createState(WKTStableHash.ToSeed(streamKey)) };
                m_streams.Add(streamKey, stream);
            }
            m_random.State = stream.State;
            m_scopeStack[m_depth++] = streamKey;
            return new Token(streamKey, outer, m_depth);
        }

        /// <summary>Saves the stream's progress and restores the state from before <see cref="Enter"/>. Ignores inactive tokens.</summary>
        public void Exit(in Token token) {
            if (!token.IsActive) return;
            if (token.Depth != m_depth) throw new InvalidOperationException($"Streams exited out of order: depth {token.Depth}, expected {m_depth}.");
            var stream = m_streams[token.StreamKey];
            stream.State = m_random.State;
            m_streams[token.StreamKey] = stream;
            m_random.State = token.Outer;
            m_depth--;
        }

        /// <summary>Forgets every key and stream. Must not be called while a stream is entered.</summary>
        public void Clear() {
            if (m_depth != 0) throw new InvalidOperationException("Cannot clear while a stream is entered.");
            m_componentKeys.Clear();
            m_streams.Clear();
        }

        /// <summary>Returned by <see cref="Enter"/>. The default value is inactive, for methods that should not be isolated.</summary>
        public readonly struct Token {
            public readonly ulong StreamKey;
            public readonly TState Outer;
            /// <summary>The stream depth once entered, from 1. Zero for an inactive token.</summary>
            public readonly int Depth;

            public bool IsActive => Depth > 0;

            public Token(ulong streamKey, TState outer, int depth) {
                StreamKey = streamKey;
                Outer = outer;
                Depth = depth;
            }
        }

        private struct Stream {
            public TState State;
            public uint SpawnCount;
        }
    }
}
