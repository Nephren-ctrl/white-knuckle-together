using System.Collections.Generic;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// Which players see each object, by its handle: the remote players who reported they have it on screen. Pure, so it can
    /// be tested without Unity. Allocates only the first time an object is seen.
    /// </summary>
    internal sealed class WKTSeerSet {
        private readonly Dictionary<ushort, List<byte>> m_seers = new();

        /// <summary>Records whether <paramref name="player"/> sees the object. True if that changed anything.</summary>
        public bool Set(ushort handle, byte player, bool seen) {
            if (!m_seers.TryGetValue(handle, out var seers)) {
                if (!seen) return false;
                seers = new List<byte>(2);
                m_seers[handle] = seers;
            }
            var index = seers.IndexOf(player);
            if (seen == index >= 0) return false;
            if (seen)
                seers.Add(player);
            else
                seers.RemoveAt(index);
            return true;
        }

        public bool IsSeen(ushort handle) => m_seers.TryGetValue(handle, out var seers) && seers.Count > 0;

        /// <summary>A player died or left: they see nothing any more.</summary>
        public void RemovePlayer(byte player) {
            foreach (var seers in m_seers.Values)
                seers.Remove(player);
        }

        public void Forget(ushort handle) => m_seers.Remove(handle);

        public void Clear() => m_seers.Clear();
    }
}
