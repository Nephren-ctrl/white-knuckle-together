using System;
using System.Collections;

namespace WhiteKnuckleTogether {
    /// <summary>Runs a coroutine unchanged, calling a callback right after each of its steps in the same frame.</summary>
    internal sealed class WKTObservedEnumerator : IEnumerator {
        private readonly IEnumerator m_inner;
        private readonly Action m_afterStep;

        public object? Current => m_inner.Current;

        public WKTObservedEnumerator(IEnumerator inner, Action afterStep) {
            m_inner = inner ?? throw new ArgumentNullException(nameof(inner));
            m_afterStep = afterStep ?? throw new ArgumentNullException(nameof(afterStep));
        }

        /// <summary>Advances the coroutine, then calls the callback, also after the final step. Not called if the step throws.</summary>
        public bool MoveNext() {
            var hasNext = m_inner.MoveNext();
            m_afterStep();
            return hasNext;
        }

        public void Reset() => throw new NotSupportedException();
    }
}
