using System;
using System.Collections;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// Runs a coroutine on its own random state. Each step swaps the coroutine's state in, advances it, and swaps the
    /// surrounding state back, so random calls made elsewhere between steps cannot shift the coroutine's sequence, and
    /// the coroutine cannot shift anyone else's. The first step starts from the current state, so a coroutine that seeds
    /// the generator itself (as world generation does) is fully deterministic.
    /// </summary>
    internal sealed class WKTIsolatedRandomEnumerator<TState> : IEnumerator where TState : struct {
        private readonly IEnumerator m_inner;
        private readonly IRandomStateAccess<TState> m_random;
        private TState m_innerState;
        private bool m_hasInnerState;

        public object? Current => m_inner.Current;

        public WKTIsolatedRandomEnumerator(IEnumerator inner, IRandomStateAccess<TState> random) {
            m_inner = inner ?? throw new ArgumentNullException(nameof(inner));
            m_random = random ?? throw new ArgumentNullException(nameof(random));
        }

        public bool MoveNext() {
            var outerState = m_random.State;
            if (m_hasInnerState)
                m_random.State = m_innerState;
            try {
                return m_inner.MoveNext();
            }
            finally {
                m_innerState = m_random.State;
                m_hasInnerState = true;
                m_random.State = outerState;
            }
        }

        public void Reset() => throw new NotSupportedException();
    }
}
