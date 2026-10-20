namespace WhiteKnuckleTogether {
    /// <summary>
    /// The messages the local player sent, newest last, to fill back into the input with the history keys. A message equal to
    /// the newest one is not added again, so sending the same thing several times takes one step to reach. Blocked messages
    /// are kept too, so a message the spam limit stopped can be sent again without retyping it.
    /// </summary>
    internal sealed class WKTChatHistory {
        public const int CAPACITY = 20;
        private readonly string[] m_entries = new string[CAPACITY];
        /// <summary>Index of the oldest entry in <see cref="m_entries"/>.</summary>
        private int m_start;
        /// <summary>How far back the input is browsing: 0 is the draft being typed, 1 the newest entry.</summary>
        private int m_cursor;
        private string m_draft = string.Empty;

        public int Count { get; private set; }

        public void Add(string text) {
            ResetCursor();
            if (string.IsNullOrEmpty(text)) return;
            if (Count > 0 && GetFromNewest(0) == text) return;
            if (Count < CAPACITY) {
                m_entries[(m_start + Count) % CAPACITY] = text;
                Count++;
                return;
            }
            m_entries[m_start] = text;
            m_start = (m_start + 1) % CAPACITY;
        }

        /// <summary>
        /// The next older message, keeping <paramref name="current"/> as the draft when leaving it. Stays on the oldest one
        /// once there. Null if there is no history.
        /// </summary>
        public string? Older(string current) {
            if (Count == 0) return null;
            if (m_cursor == 0)
                m_draft = current;
            if (m_cursor < Count)
                m_cursor++;
            return GetFromNewest(m_cursor - 1);
        }

        /// <summary>The next newer message, or the draft past the newest one. Null when already on the draft.</summary>
        public string? Newer() {
            if (m_cursor == 0) return null;
            m_cursor--;
            return m_cursor == 0 ? m_draft : GetFromNewest(m_cursor - 1);
        }

        /// <summary>Back to the draft, such as when the chat closes.</summary>
        public void ResetCursor() {
            m_cursor = 0;
            m_draft = string.Empty;
        }

        /// <summary>The entry <paramref name="age"/> steps back from the newest: 0 is the newest.</summary>
        public string GetFromNewest(int age) => m_entries[(m_start + Count - 1 - age) % CAPACITY];
    }
}
