using System.Collections.Generic;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// Host side: every player's bucking timer for every flier, whether they ride it now or got off. A timer travels with its
    /// rider: a client simulating a flier alone keeps theirs in the game's own module and hands it here when it stops, and gets
    /// it back when it starts again. While a rider is off a flier, their timer recharges by the time that passed, worked out
    /// when it is next needed. Pure, so it can be tested without Unity, and allocation free once warm.
    /// </summary>
    internal sealed class WKTRiderTimers {
        private readonly Dictionary<int, Entry> m_entries = new();
        private readonly List<int> m_scratch = new();

        public int Count => m_entries.Count;

        /// <summary><paramref name="playerId"/> got off the flier: their timer starts recharging.</summary>
        public void OnDismount(ushort handle, byte playerId, double now) {
            var key = Key(handle, playerId);
            if (!m_entries.TryGetValue(key, out var entry) || !entry.Riding) return;
            entry.Riding = false;
            entry.Since = now;
            m_entries[key] = entry;
        }

        /// <summary>
        /// The machine that simulated the flier for <paramref name="playerId"/> handed over their timer as it stood
        /// <paramref name="now"/>. It replaces whatever was kept here.
        /// </summary>
        public void Set(ushort handle, byte playerId, in WKTBuckTimer timer, bool riding, double now) =>
            m_entries[Key(handle, playerId)] = new Entry { Timer = timer, HasTimer = true, Riding = riding, Since = now };

        /// <summary>Holds a rider's timer still until <paramref name="until"/>, while the machine that had it hands it over.</summary>
        public void Await(ushort handle, byte playerId, double until) {
            var key = Key(handle, playerId);
            m_entries.TryGetValue(key, out var entry);
            entry.AwaitUntil = until;
            m_entries[key] = entry;
        }

        /// <summary>
        /// Advances a riding player's timer here. A rider with none starts rested; one that is still being handed over waits.
        /// True if the flier bucks them now.
        /// </summary>
        public bool Tick(ushort handle, byte playerId, float deltaTime, double now, in WKTBuckSettings settings, out bool throwOff) {
            throwOff = false;
            var key = Key(handle, playerId);
            if (!m_entries.TryGetValue(key, out var entry) || !entry.HasTimer) {
                if (entry.AwaitUntil > now) return false;
                entry = new Entry { Timer = WKTBuckTimer.Rested(settings.Wait), Riding = true, HasTimer = true };
            }
            else if (entry.AwaitUntil > now) return false;
            if (!entry.Riding) {
                entry.Timer.Recharge((float)(now - entry.Since), settings);
                entry.Riding = true;
            }
            var bucked = entry.Timer.Tick(deltaTime, settings, out throwOff);
            entry.AwaitUntil = 0;
            m_entries[key] = entry;
            return bucked;
        }

        /// <summary>A player's timer as it stands <paramref name="now"/>, recharged if they are off the flier. False if none is kept.</summary>
        public bool TryGet(ushort handle, byte playerId, double now, in WKTBuckSettings settings, out WKTBuckTimer timer) {
            timer = default;
            if (!m_entries.TryGetValue(Key(handle, playerId), out var entry) || !entry.HasTimer) return false;
            timer = entry.Timer;
            if (!entry.Riding)
                timer.Recharge((float)(now - entry.Since), settings);
            return true;
        }

        /// <summary>The flier is gone.</summary>
        public void Forget(ushort handle) {
            m_scratch.Clear();
            foreach (var key in m_entries.Keys) {
                if (key >> 8 == handle)
                    m_scratch.Add(key);
            }
            foreach (var key in m_scratch)
                m_entries.Remove(key);
        }

        /// <summary>A player left the run: they ride nothing any more.</summary>
        public void ForgetPlayer(byte playerId) {
            m_scratch.Clear();
            foreach (var key in m_entries.Keys) {
                if ((key & 0xFF) == playerId)
                    m_scratch.Add(key);
            }
            foreach (var key in m_scratch)
                m_entries.Remove(key);
        }

        public void Clear() => m_entries.Clear();

        private static int Key(ushort handle, byte playerId) => (handle << 8) | playerId;

        private struct Entry {
            public WKTBuckTimer Timer;
            public bool HasTimer;
            /// <summary>Whether the timer was last advanced riding. Off the flier, it recharges from <see cref="Since"/>.</summary>
            public bool Riding;
            /// <summary>When they got off, or when the timer was handed over off the flier.</summary>
            public double Since;
            public double AwaitUntil;
        }
    }
}
