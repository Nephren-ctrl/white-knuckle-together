using System;
using System.Collections;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// Runs a coroutine with its own copy of a shared value, such as a field it reads and writes across frames. Each step
    /// swaps the coroutine's copy in and the surrounding value back out, so coroutines running side by side never see each
    /// other's writes.
    /// </summary>
    internal sealed class WKTScopedValueEnumerator<T> : IEnumerator {
        private readonly IEnumerator m_inner;
        private readonly Func<T> m_get;
        private readonly Action<T> m_set;
        private T m_innerValue;

        public object? Current => m_inner.Current;

        public WKTScopedValueEnumerator(IEnumerator inner, Func<T> get, Action<T> set, T initialValue) {
            m_inner = inner ?? throw new ArgumentNullException(nameof(inner));
            m_get = get ?? throw new ArgumentNullException(nameof(get));
            m_set = set ?? throw new ArgumentNullException(nameof(set));
            m_innerValue = initialValue;
        }

        public bool MoveNext() {
            var outer = m_get();
            m_set(m_innerValue);
            try {
                return m_inner.MoveNext();
            }
            finally {
                m_innerValue = m_get();
                m_set(outer);
            }
        }

        public void Reset() => throw new NotSupportedException();
    }
}
