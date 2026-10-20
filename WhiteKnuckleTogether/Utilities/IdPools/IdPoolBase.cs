using System;
using System.Collections.Generic;

namespace WhiteKnuckleTogether {
    public abstract class IdPoolBase<T> where T : struct {
        private readonly Queue<T> m_pool = new();
        private T m_current;

        public T GetNext() {
            if (IsMaxValue(m_current)) {
                if (m_pool.Count > 0)
                    return m_pool.Dequeue();
                throw new Exception("Id pool ran out of ids. This probably shouldn't happen.");
            }
            var value = m_current;
            Increment(ref m_current);
            return value;
        }

        public void Recycle(T value) => m_pool.Enqueue(value);

        public void Reset() {
            m_pool.Clear();
            m_current = GetMinimum();
        }

        /// <summary>Starts over, handing out ids after <paramref name="highestUsed"/>, such as when taking over ids someone else gave out.</summary>
        public void ResetAfter(T highestUsed) {
            m_pool.Clear();
            m_current = highestUsed;
            if (IsMaxValue(m_current)) return;
            Increment(ref m_current);
        }

        protected abstract T GetMinimum();

        protected abstract void Increment(ref T value);

        protected abstract bool IsMaxValue(T value);
    }
}