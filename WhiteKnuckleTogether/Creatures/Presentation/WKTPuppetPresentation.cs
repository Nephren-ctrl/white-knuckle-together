using System;
using System.Collections.Generic;
using Steamworks.Data;
using UnityEngine;
using WhiteKnuckleTogether.Networking.LiteNetLib;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// The presenters (<see cref="WKTPuppetPresenterBase"/>) of a synced co-op run's world entities. Each entity of a covered
    /// type gets one when it registers. The machine simulating it sends its state when it changes and once a second, to
    /// players within <see cref="WKTEntityStateSender.RELEVANCE_DISTANCE"/>; a client's goes through the host. Every machine
    /// where the entity is a puppet reads that state and runs the presenter each frame and physics step. Owned by
    /// <see cref="WKTWorldSync"/>.
    /// </summary>
    internal sealed class WKTPuppetPresentation {
        /// <summary>How often an unchanged state is sent again, in seconds, so a lost packet is made up for.</summary>
        public const double KEYFRAME_INTERVAL = 1.0;
        private static readonly Dictionary<Type, Func<GameEntity, WKTPuppetPresenterBase>?> s_factories = new();
        private readonly WKTWorldSync m_sync;
        private readonly WKTNetwork m_network;
        private readonly WKTSendClock m_clock = new();
        private readonly List<Entry> m_entries = new();
        private readonly Dictionary<int, Entry> m_byIdentity = new();
        private readonly NetDataWriter m_scratch = new();
        private readonly NetDataReader m_stateReader = new();

        public WKTPuppetPresentation(WKTWorldSync sync, WKTNetwork network) {
            m_sync = sync;
            m_network = network;
        }

        /// <summary>The presenter type a creature type gets, if any, looking up its base types too. Cached per type.</summary>
        public static Func<GameEntity, WKTPuppetPresenterBase>? GetFactory(Type type) {
            if (s_factories.TryGetValue(type, out var factory)) return factory;
            for (var current = type; current != null && current != typeof(GameEntity) && factory == null; current = current.BaseType) {
                foreach (var (entityType, create) in WKTPresenterTable.Factories) {
                    if (entityType != current) continue;
                    factory = create;
                    break;
                }
            }
            s_factories[type] = factory;
            return factory;
        }

        /// <summary>A world object registered: covered creatures get their presenter.</summary>
        public void Register(WKTNetIdentity identity) {
            var entity = identity.Entity;
            if (entity == null || m_byIdentity.ContainsKey(identity.GetInstanceID())) return;
            var factory = GetFactory(entity.GetType());
            if (factory == null) return;
            var entry = new Entry(identity, factory(entity));
            m_entries.Add(entry);
            m_byIdentity[identity.GetInstanceID()] = entry;
        }

        /// <summary>The presenter of a world entity, if it has one of that type.</summary>
        public T? Get<T>(Component entity) where T : WKTPuppetPresenterBase =>
            entity.TryGetComponent<WKTNetIdentity>(out var identity) && m_byIdentity.TryGetValue(identity.GetInstanceID(), out var entry) ? entry.Presenter as T : null;

        /// <summary>This machine took over simulating a creature it showed as a puppet.</summary>
        public void OnTakenOver(WKTNetIdentity identity) {
            if (!m_byIdentity.TryGetValue(identity.GetInstanceID(), out var entry) || entry.Failed) return;
            try {
                entry.Presenter.OnTakenOver();
            }
            catch (Exception exception) {
                Fail(entry, exception);
            }
        }

        public void Forget(WKTNetIdentity identity) {
            if (!m_byIdentity.Remove(identity.GetInstanceID(), out var entry)) return;
            m_entries.Remove(entry);
        }

        public void Clear() {
            m_entries.Clear();
            m_byIdentity.Clear();
            m_clock.Reset();
        }

        /// <summary>Runs the puppets' presenters and, on a send tick, sends the states of the creatures simulated here.</summary>
        public void Update(double now) {
            var deltaTime = Time.deltaTime;
            foreach (var entry in m_entries) {
                if (!entry.IsPuppet) continue;
                try {
                    entry.Presenter.PuppetUpdate(deltaTime);
                }
                catch (Exception exception) {
                    Fail(entry, exception);
                }
            }
            if (!m_clock.TryAdvance(now, out _)) return;
            foreach (var entry in m_entries)
                SendIfChanged(entry, now);
        }

        public void FixedUpdate() {
            var fixedDeltaTime = Time.fixedDeltaTime;
            foreach (var entry in m_entries) {
                if (!entry.IsPuppet) continue;
                try {
                    entry.Presenter.PuppetFixedUpdate(fixedDeltaTime);
                }
                catch (Exception exception) {
                    Fail(entry, exception);
                }
            }
        }

        private static void Fail(Entry entry, Exception exception) {
            entry.Failed = true;
            WKT.Instance.Logger.LogError($"{entry.Presenter.GetType().Name} failed on {(entry.Identity != null ? entry.Identity.name : "a destroyed object")} " +
                                         $"and stops showing it: {exception}");
        }

        private void SendIfChanged(Entry entry, double now) {
            var identity = entry.Identity;
            if (entry.Failed || !entry.Presenter.HasState || identity == null || identity.Handle == 0 || identity.IsPuppet || !identity.gameObject.activeInHierarchy) return;
            if (!m_sync.IsHost && identity.Owner != m_network.LocalPlayer?.Id) return;
            m_scratch.Reset();
            try {
                entry.Presenter.WriteState(m_scratch);
            }
            catch (Exception exception) {
                Fail(entry, exception);
                return;
            }
            var length = m_scratch.Length;
            if (length > WKTPuppetPresenterBase.MAX_STATE_SIZE) {
                if (entry.WarnedSize) return;
                entry.WarnedSize = true;
                WKT.Instance.Logger.LogWarning($"{entry.Presenter.GetType().Name} wrote {length} bytes for {identity.name}, over {WKTPuppetPresenterBase.MAX_STATE_SIZE}; not sent.");
                return;
            }
            if (!entry.State.Record(m_scratch.Data, 0, length, now, KEYFRAME_INTERVAL)) return;
            if (m_sync.IsHost) {
                SendNear(identity, entry, null);
                return;
            }
            var writer = Begin(identity.Handle, entry);
            m_network.SendToHost(writer, SendType.Unreliable);
        }

        private NetDataWriter Begin(ushort handle, Entry entry) {
            var writer = m_network.BeginMessage(WKTMessageId.EntityPresentation);
            writer.Put(handle);
            writer.Put((byte)entry.State.Length);
            writer.Put(entry.State.Last, 0, entry.State.Length);
            return writer;
        }

        /// <summary>Host only: sends an entity's last state to every remote player near it, except <paramref name="except"/>.</summary>
        private void SendNear(WKTNetIdentity identity, Entry entry, WKTPlayer? except) {
            const float range_squared = WKTEntityStateSender.RELEVANCE_DISTANCE * WKTEntityStateSender.RELEVANCE_DISTANCE;
            var position = identity.transform.position;
            NetDataWriter? writer = null;
            for (var i = 0; i < WKTPlayerPositions.RemoteCount; i++) {
                var id = WKTPlayerPositions.GetRemoteId(i);
                if (except != null && except.Id == id) continue;
                if ((WKTPlayerPositions.GetRemotePosition(i) - position).sqrMagnitude > range_squared) continue;
                if (!m_network.TryGetPlayer(id, out var player) || player == null) continue;
                writer ??= Begin(identity.Handle, entry);
                m_network.SendToPlayer(player, writer, SendType.Unreliable);
            }
        }

        /// <summary>
        /// A state from the machine simulating an entity. A puppet shows it; the host passes on a client's to the other players
        /// near it.
        /// </summary>
        public void OnPresentation(WKTPlayer sender, NetDataReader reader) {
            if (!reader.TryGetUShort(out var handle) || !reader.TryGetByte(out var length) || reader.AvailableBytes < length) return;
            var end = reader.Position + length;
            if (!m_sync.TryGetIdentity(handle, out var identity) || !m_byIdentity.TryGetValue(identity!.GetInstanceID(), out var entry)) return;
            if (m_sync.IsHost) {
                // Only the client simulating the entity speaks for it.
                if (sender.IsHost || identity.Owner != sender.Id) return;
                if (entry.State.Record(reader.RawData, reader.Position, length, Time.realtimeSinceStartupAsDouble, KEYFRAME_INTERVAL))
                    SendNear(identity, entry, sender);
            }
            else if (!sender.IsHost) return;
            if (!identity.IsPuppet) return;
            m_stateReader.SetSource(reader.RawData, reader.Position, end);
            entry.Presenter.ReadState(m_stateReader);
        }

        private sealed class Entry {
            public readonly WKTNetIdentity Identity;
            public readonly WKTPuppetPresenterBase Presenter;
            public readonly WKTStateCache State = new(WKTPuppetPresenterBase.MAX_STATE_SIZE);
            public bool WarnedSize;
            /// <summary>Set once the presenter threw: it is not run again, so one broken creature cannot stop the others.</summary>
            public bool Failed;

            public bool IsPuppet => !Failed && Identity != null && Identity.IsPuppet && Identity.Handle != 0;

            public Entry(WKTNetIdentity identity, WKTPuppetPresenterBase presenter) {
                Identity = identity;
                Presenter = presenter;
            }
        }
    }
}
