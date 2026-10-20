using System.Collections.Generic;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// Collects what is spawned while a scope is open. Scopes nest: only closing the outermost one hands the spawned objects
    /// over. Pure, so it can be tested without Unity. Single-threaded.
    /// </summary>
    internal sealed class WKTSpawnScope<T> where T : class {
        private readonly List<T> m_spawned = new();

        public int Depth { get; private set; }
        public int PendingCount => m_spawned.Count;

        public void Enter() => Depth++;

        /// <summary>Closes a scope. True when that was the outermost one, so the spawned objects should be drained.</summary>
        public bool Exit() {
            if (Depth == 0) return false;
            Depth--;
            return Depth == 0;
        }

        /// <summary>Remembers <paramref name="spawned"/> when a scope is open. Ignored otherwise.</summary>
        public void Record(T? spawned) {
            if (Depth > 0 && spawned != null)
                m_spawned.Add(spawned);
        }

        /// <summary>Moves everything spawned into <paramref name="into"/>.</summary>
        public void Drain(List<T> into) {
            into.AddRange(m_spawned);
            m_spawned.Clear();
        }

        public void Clear() {
            Depth = 0;
            m_spawned.Clear();
        }
    }
}
