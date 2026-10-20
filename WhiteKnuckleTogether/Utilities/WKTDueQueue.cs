using System.Collections.Generic;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// Items that become due at a time, taken in the order they were added once due. Used to run a host enemy's attack on a
    /// client when its puppet shows the pose the host attacked from. Pure and allocation free after warm-up.
    /// </summary>
    internal sealed class WKTDueQueue<T> {
        private readonly List<(T Item, double Due)> m_items = new();

        public int Count => m_items.Count;

        public void Add(T item, double due) => m_items.Add((item, due));

        /// <summary>Moves every item due at <paramref name="now"/> into <paramref name="due"/>, oldest first.</summary>
        public void TakeDue(double now, List<T> due) {
            var kept = 0;
            for (var i = 0; i < m_items.Count; i++) {
                var entry = m_items[i];
                if (entry.Due <= now)
                    due.Add(entry.Item);
                else
                    m_items[kept++] = entry;
            }
            m_items.RemoveRange(kept, m_items.Count - kept);
        }

        public void Clear() => m_items.Clear();
    }
}
