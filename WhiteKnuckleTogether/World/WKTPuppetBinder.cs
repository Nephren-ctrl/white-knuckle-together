using System.Collections.Generic;

namespace WhiteKnuckleTogether {
    /// <summary>What <see cref="WKTPuppetBinder{TObject}"/> does to the client's objects.</summary>
    internal interface IWKTPuppetActions<TObject> where TObject : class {
        /// <summary>Turns a local object into the puppet of the host's object and shows it.</summary>
        void Bind(TObject obj, WKTEntityBind bind);
        /// <summary>Spawns a puppet for a host object the client has no copy of. Null if it cannot be spawned.</summary>
        TObject? Spawn(WKTEntityBind bind);
        /// <summary>Hides a local object the host has not claimed: the client must have no world objects of its own.</summary>
        void Hide(TObject obj);
        void Destroy(TObject obj);
        /// <summary>Whether the client has the level with this key loaded and active. Never asked for key zero.</summary>
        bool IsLevelActive(ulong levelKey);
        /// <summary>
        /// Whether a host object can never have a copy of the client's own, such as something a player dropped, so it is spawned
        /// without waiting for one.
        /// </summary>
        bool IsRuntimeOnly(WKTEntityBind bind);
    }

    /// <summary>
    /// Client side of host-authoritative world objects. Every world object the client builds is hidden until the host binds
    /// its key. Each host object is matched to the client's copy with the same key, or spawned when the client has none
    /// once its level has been active for <see cref="SpawnGrace"/> seconds (long enough for the client's own copy to appear).
    /// A local copy appearing after a spawn is a duplicate and is destroyed. A host object known to be dead is never spawned
    /// again: once the client's copy of it goes (gibbed, or removed by its own death), it is forgotten, and when the host
    /// removes it first the copy has <see cref="DeadDespawnGrace"/> seconds to finish its own death. Pure, so it can be tested
    /// without Unity.
    /// </summary>
    internal sealed class WKTPuppetBinder<TObject> where TObject : class {
        private readonly IWKTPuppetActions<TObject> m_actions;
        private readonly Dictionary<ushort, Entry> m_entries = new();
        private readonly Dictionary<ulong, ushort> m_handleByKey = new();
        /// <summary>Local objects not bound to any host object, by key.</summary>
        private readonly Dictionary<ulong, TObject> m_unbound = new();
        private readonly List<ushort> m_pendingScratch = new();
        /// <summary>Copies of dead host objects the host removed, destroyed once their time is up.</summary>
        private readonly List<(TObject Object, double DestroyAt)> m_dying = new();

        public double SpawnGrace { get; }
        /// <summary>How long a dead object's copy may stay after the host removed it, for its own death to play out.</summary>
        public double DeadDespawnGrace { get; set; }
        public int DyingCount => m_dying.Count;
        public int BoundCount { get; private set; }
        public int EntryCount => m_entries.Count;
        public int UnboundCount => m_unbound.Count;

        public WKTPuppetBinder(IWKTPuppetActions<TObject> actions, double spawnGrace) {
            m_actions = actions;
            SpawnGrace = spawnGrace;
        }

        public bool TryGetObject(ushort handle, out TObject? obj) {
            obj = m_entries.TryGetValue(handle, out var entry) ? entry.Object : null;
            return obj != null;
        }

        public bool TryGetBind(ushort handle, out WKTEntityBind? bind) {
            bind = m_entries.TryGetValue(handle, out var entry) ? entry.Bind : null;
            return bind != null;
        }

        /// <summary>The host announced an object. A handle that is reused for another key replaces the old object.</summary>
        public void OnHostBind(WKTEntityBind bind, double now) {
            if (m_entries.TryGetValue(bind.Handle, out var existing)) {
                if (existing.Bind.Key != bind.Key)
                    OnHostDespawn(bind.Handle);
                else {
                    existing.Bind.CopyFrom(bind);
                    if (existing.Object != null)
                        m_actions.Bind(existing.Object, existing.Bind);
                    return;
                }
            }
            // A key the host binds twice under different handles: the newer handle wins.
            if (m_handleByKey.TryGetValue(bind.Key, out var oldHandle))
                OnHostDespawn(oldHandle);
            var entry = new Entry { Bind = new WKTEntityBind(), PendingSince = now };
            entry.Bind.CopyFrom(bind);
            m_entries.Add(bind.Handle, entry);
            m_handleByKey[bind.Key] = bind.Handle;
            if (m_unbound.TryGetValue(bind.Key, out var local)) {
                m_unbound.Remove(bind.Key);
                Attach(entry, local, false);
            }
        }

        /// <summary>
        /// The host's object is gone, so the client's copy goes too: at once, or for a dead object after
        /// <see cref="DeadDespawnGrace"/>.
        /// </summary>
        public void OnHostDespawn(ushort handle, double now = 0) {
            if (!m_entries.TryGetValue(handle, out var entry)) return;
            m_entries.Remove(handle);
            if (m_handleByKey.TryGetValue(entry.Bind.Key, out var mapped) && mapped == handle)
                m_handleByKey.Remove(entry.Bind.Key);
            if (entry.Object == null) return;
            BoundCount--;
            if (entry.Dead && DeadDespawnGrace > 0) {
                m_dying.Add((entry.Object, now + DeadDespawnGrace));
                return;
            }
            m_actions.Destroy(entry.Object);
        }

        /// <summary>The host's object died. Its copy is never replaced once it goes.</summary>
        public void MarkDead(ushort handle) {
            if (m_entries.TryGetValue(handle, out var entry))
                entry.Dead = true;
        }


        /// <summary>
        /// The client expects the host to bind <paramref name="key"/> to <paramref name="obj"/>, such as an item it just
        /// dropped. Unlike <see cref="OnLocalRegister"/>, the object stays shown until then.
        /// </summary>
        public void Expect(ulong key, TObject obj) {
            if (m_handleByKey.TryGetValue(key, out var handle)) {
                var entry = m_entries[handle];
                if (entry.Object == null)
                    Attach(entry, obj, false);
                return;
            }
            m_unbound[key] = obj;
        }

        /// <summary>
        /// Lets go of a host object without destroying the client's copy, which now belongs to the client, such as an item it
        /// picked up. A later despawn of the handle is ignored.
        /// </summary>
        public bool Release(ushort handle) {
            if (!m_entries.TryGetValue(handle, out var entry)) return false;
            m_entries.Remove(handle);
            if (m_handleByKey.TryGetValue(entry.Bind.Key, out var mapped) && mapped == handle)
                m_handleByKey.Remove(entry.Bind.Key);
            if (entry.Object != null)
                BoundCount--;
            return true;
        }

        /// <summary>The client built or enabled a world object with this key.</summary>
        public void OnLocalRegister(ulong key, TObject obj) {
            if (m_handleByKey.TryGetValue(key, out var handle)) {
                var entry = m_entries[handle];
                if (entry.Object == null)
                    Attach(entry, obj, false);
                else if (!ReferenceEquals(entry.Object, obj))
                    m_actions.Destroy(obj);
                return;
            }
            if (m_unbound.TryGetValue(key, out var other) && !ReferenceEquals(other, obj)) {
                // Two local objects with one key: keep the first, the other could never be told apart.
                m_actions.Destroy(obj);
                return;
            }
            m_unbound[key] = obj;
            m_actions.Hide(obj);
        }

        /// <summary>A local object is being destroyed. A bound one leaves its host object waiting for a new copy.</summary>
        public void OnLocalDestroyed(ulong key, TObject obj, double now) {
            if (m_unbound.TryGetValue(key, out var unbound) && ReferenceEquals(unbound, obj)) {
                m_unbound.Remove(key);
                return;
            }
            for (var i = m_dying.Count - 1; i >= 0; i--) {
                if (ReferenceEquals(m_dying[i].Object, obj))
                    m_dying.RemoveAt(i);
            }
            if (!m_handleByKey.TryGetValue(key, out var handle)) return;
            var entry = m_entries[handle];
            if (!ReferenceEquals(entry.Object, obj)) return;
            if (entry.Dead) {
                // Nothing to wait for: the host's object is dead too, and goes away by itself.
                m_entries.Remove(handle);
                m_handleByKey.Remove(key);
                BoundCount--;
                return;
            }
            entry.Object = null;
            entry.Spawned = false;
            entry.PendingSince = now;
            BoundCount--;
        }

        /// <summary>Spawns the host objects the client still has no copy of, once their level has been active long enough.</summary>
        public void Update(double now) {
            for (var i = m_dying.Count - 1; i >= 0; i--) {
                if (m_dying[i].DestroyAt > now) continue;
                var dying = m_dying[i].Object;
                m_dying.RemoveAt(i);
                m_actions.Destroy(dying);
            }
            m_pendingScratch.Clear();
            foreach (var pair in m_entries) {
                if (pair.Value.Object == null)
                    m_pendingScratch.Add(pair.Key);
            }
            foreach (var handle in m_pendingScratch) {
                var entry = m_entries[handle];
                var levelKey = entry.Bind.LevelKey;
                if (levelKey != 0 && !m_actions.IsLevelActive(levelKey)) {
                    entry.PendingSince = now;
                    continue;
                }
                if (now - entry.PendingSince < SpawnGrace && !m_actions.IsRuntimeOnly(entry.Bind)) continue;
                var spawned = m_actions.Spawn(entry.Bind);
                if (spawned == null) {
                    // Retried after another grace period rather than every frame.
                    entry.PendingSince = now;
                    continue;
                }
                // A spawned object usually registers itself while spawning, which already attached it.
                if (entry.Object == null)
                    Attach(entry, spawned, true);
                else
                    entry.Spawned = true;
            }
        }

        /// <summary>Forgets everything, leaving every object as it is. Used when this machine takes over as host.</summary>
        public void Clear() {
            m_entries.Clear();
            m_handleByKey.Clear();
            m_unbound.Clear();
            m_dying.Clear();
            BoundCount = 0;
        }

        private void Attach(Entry entry, TObject obj, bool spawned) {
            entry.Object = obj;
            entry.Spawned = spawned;
            BoundCount++;
            m_actions.Bind(obj, entry.Bind);
        }

        private sealed class Entry {
            public WKTEntityBind Bind = null!;
            public TObject? Object;
            public bool Spawned;
            public bool Dead;
            public double PendingSince;
        }
    }
}
