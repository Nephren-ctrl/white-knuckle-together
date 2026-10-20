using System.Collections.Generic;
using Steamworks.Data;
using UnityEngine;
using UnityEngine.Events;
using WhiteKnuckleTogether.Networking.LiteNetLib;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// An entity's own UnityEvents in a synced co-op run: its death, attack, hunt, stun and activation events, which the game
    /// invokes from code that only runs on the machine simulating it. Each listener is either the world's (spawners, other
    /// entities, the level), which only the host runs, or local (the entity's own body, sound, particles, lights and the
    /// player's screen), which every machine showing it runs (see <see cref="WKTWorldWiring.IsEntityLocalTarget"/>).
    /// <list type="bullet">
    /// <item>Simulated by the host: runs whole there; clients near it run the local part.</item>
    /// <item>Simulated by a client: the local part runs there; the host runs the whole event and passes it on to the others.</item>
    /// <item>A puppet never invokes its events by itself, such as from its death replay or animation events.</item>
    /// <item>Inside a local scope (a hit replayed against the local player, a puppet's allowed ticks), only the local part runs, unsent.</item>
    /// </list>
    /// Events are numbered per entity: the entity's own serialized UnityEvent fields, then its attack module's, then each of
    /// that module's attacks', then its damage effects' (<c>GameEntity.damageEffects</c>), in declaration order, which every
    /// copy of a prefab shares. Only plain and transform events are covered. A client relaying an event to the host sends the
    /// player whose hit set it off (<see cref="WKTDamageCause"/>), which the host runs it under.
    /// <para>
    /// A creature's own buttons (<c>CL_Button</c> on its body, such as an explosive roach or a grub sister to pet) are pressed
    /// on the presser's machine but run whole on the machine simulating the creature, which a press elsewhere is sent to.
    /// What they do then shows everywhere through its pose, animator, sounds and events.
    /// </para>
    /// Owned by <see cref="WKTWorldSync"/>.
    /// </summary>
    internal sealed class WKTEntityEvents {
        private static readonly List<CL_Button> s_buttons = new();
        private readonly WKTWorldSync m_sync;
        private readonly WKTNetwork m_network;
        private readonly Dictionary<UnityEventBase, EventRef> m_byEvent = new();
        /// <summary>Each entity's events by index, by the identity's instance id.</summary>
        private readonly Dictionary<int, List<EventRef>> m_byIdentity = new();
        private readonly Dictionary<UnityEventBase, EventRef> m_byInteraction = new();
        /// <summary>Each creature's button events by index, by the identity's instance id.</summary>
        private readonly Dictionary<int, List<EventRef>> m_interactionsByIdentity = new();
        private readonly List<UnityEventBase> m_scratch = new();
        private bool m_applying;
        private bool m_runningInteraction;

        public WKTEntityEvents(WKTWorldSync sync, WKTNetwork network) {
            m_sync = sync;
            m_network = network;
        }

        /// <summary>
        /// An event is about to be invoked by the game. False skips the game's call, when only part of it runs here or none of
        /// it. The simulating machine sends it on.
        /// </summary>
        public static bool BeforeInvoke(UnityEventBase unityEvent, Transform? argument) {
            var events = WKTWorldSync.Instance?.Events;
            if (events == null) return true;
            if (WKTMeleeReplay.SuppressesEvent(unityEvent)) return false;
            if (!events.m_runningInteraction && events.m_byInteraction.TryGetValue(unityEvent, out var interaction))
                return events.BeforeInteraction(interaction);
            if (events.m_applying || !events.m_byEvent.TryGetValue(unityEvent, out var reference)) return true;
            var identity = reference.Identity;
            if (identity == null || identity.Handle == 0) return true;
            var localScope = WKTLocalReplayScope.IsOpen || WKTPuppetHitScope.IsOpen;
            if (identity.IsPuppet) {
                if (localScope)
                    events.Run(reference, argument, false);
                return false;
            }
            if (localScope) return true;
            if (events.m_sync.IsHost) {
                events.SendNear(reference, null);
                return true;
            }
            var writer = events.m_network.BeginMessage(WKTMessageId.EntityUnityEvent);
            writer.Put(identity.Handle);
            writer.Put(reference.Index);
            writer.Put(WKTDamageCause.Current);
            events.m_network.SendToHost(writer, SendType.Reliable);
            events.Run(reference, argument, false);
            return false;
        }

        /// <summary>Numbers an entity's events and sorts their listeners, once, when it becomes a world object.</summary>
        public void Index(WKTNetIdentity identity) {
            var entity = identity.Entity;
            if (entity == null || m_byIdentity.ContainsKey(identity.GetInstanceID())) return;
            m_scratch.Clear();
            Collect(entity, m_scratch);
            if (WKTAttackModules.GetField(entity.GetType())?.GetValue(entity) is { } module) {
                Collect(module, m_scratch);
                if (module is DenizenAttackModule_Basic { attacks: { } attacks }) {
                    foreach (var attack in attacks)
                        Collect(attack, m_scratch);
                }
            }
            if (entity.damageEffects != null) {
                foreach (var effect in entity.damageEffects) {
                    if (effect?.damageEvent != null)
                        m_scratch.Add(effect.damageEvent);
                }
            }
            var references = new List<EventRef>(m_scratch.Count);
            for (var i = 0; i < m_scratch.Count && i <= byte.MaxValue; i++) {
                var unityEvent = m_scratch[i];
                var reference = new EventRef(identity, (byte)i, unityEvent, Sort(unityEvent, identity.transform));
                references.Add(reference);
                // An event shared between objects keeps its first owner.
                m_byEvent.TryAdd(unityEvent, reference);
            }
            m_byIdentity[identity.GetInstanceID()] = references;
            m_scratch.Clear();
            IndexInteractions(identity, entity);
        }

        /// <summary>A creature's own buttons' events, in hierarchy order, which every copy of a prefab shares.</summary>
        private void IndexInteractions(WKTNetIdentity identity, GameEntity entity) {
            if (entity is not Denizen) return;
            identity.GetComponentsInChildren(true, s_buttons);
            foreach (var button in s_buttons) {
                if (button != null && button.GetComponentInParent<GameEntity>(true) == entity)
                    Collect(button, m_scratch);
            }
            s_buttons.Clear();
            if (m_scratch.Count == 0) return;
            var references = new List<EventRef>(m_scratch.Count);
            for (var i = 0; i < m_scratch.Count && i <= byte.MaxValue; i++) {
                var reference = new EventRef(identity, (byte)i, m_scratch[i], default);
                references.Add(reference);
                m_byInteraction.TryAdd(m_scratch[i], reference);
            }
            m_interactionsByIdentity[identity.GetInstanceID()] = references;
            m_scratch.Clear();
        }

        public void Forget(WKTNetIdentity identity) {
            Forget(identity, m_byIdentity, m_byEvent);
            Forget(identity, m_interactionsByIdentity, m_byInteraction);
        }

        private static void Forget(WKTNetIdentity identity, Dictionary<int, List<EventRef>> byIdentity, Dictionary<UnityEventBase, EventRef> byEvent) {
            if (!byIdentity.Remove(identity.GetInstanceID(), out var references)) return;
            foreach (var reference in references) {
                if (byEvent.TryGetValue(reference.Event, out var registered) && registered.Identity == identity)
                    byEvent.Remove(reference.Event);
            }
        }

        public void Clear() {
            m_byEvent.Clear();
            m_byIdentity.Clear();
            m_byInteraction.Clear();
            m_interactionsByIdentity.Clear();
            m_applying = false;
            m_runningInteraction = false;
        }

        /// <summary>
        /// A creature's button was pressed here. It runs here if this machine simulates the creature; otherwise it is sent to
        /// the one that does: a client's through the host, the host's to the owning client.
        /// </summary>
        private bool BeforeInteraction(in EventRef reference) {
            var identity = reference.Identity;
            if (identity == null || identity.Handle == 0 || m_sync.Authority.SimulatesHere(identity)) return true;
            var writer = m_network.BeginMessage(WKTMessageId.EntityInteraction);
            writer.Put(identity.Handle);
            writer.Put(reference.Index);
            if (!m_sync.IsHost)
                m_network.SendToHost(writer, SendType.Reliable);
            else if (m_network.TryGetPlayer(identity.Owner, out var owner) && owner != null)
                m_network.SendToPlayer(owner, writer, SendType.Reliable);
            return false;
        }

        /// <summary>A press on a creature's button from another machine, run here if this machine simulates it, or passed on by the host.</summary>
        public void OnInteraction(WKTPlayer sender, NetDataReader reader) {
            if (!reader.TryGetUShort(out var handle) || !reader.TryGetByte(out var index) || !m_sync.TryGetIdentity(handle, out var identity) ||
                !m_interactionsByIdentity.TryGetValue(identity!.GetInstanceID(), out var references) || index >= references.Count) return;
            if (m_sync.IsHost == sender.IsHost) return;
            var reference = references[index];
            if (!m_sync.Authority.SimulatesHere(identity)) {
                if (!m_sync.IsHost || identity.Owner == sender.Id || !m_network.TryGetPlayer(identity.Owner, out var owner) || owner == null) return;
                var writer = m_network.BeginMessage(WKTMessageId.EntityInteraction);
                writer.Put(handle);
                writer.Put(index);
                m_network.SendToPlayer(owner, writer, SendType.Reliable);
                return;
            }
            if (reference.Event is not UnityEvent plain) return;
            m_runningInteraction = true;
            try {
                plain.Invoke();
            }
            finally {
                m_runningInteraction = false;
            }
        }

        /// <summary>The plain and transform UnityEvents in an object's serialized fields, in declaration order.</summary>
        private static void Collect(object owner, List<UnityEventBase> events) {
            foreach (var field in WKTUnityEventFields.Get(owner.GetType())) {
                if (field.GetValue(owner) is UnityEventBase unityEvent and (UnityEvent or UnityEvent<Transform>))
                    events.Add(unityEvent);
            }
        }

        private static WKTListenerMasks Sort(UnityEventBase unityEvent, Transform root) {
            var masks = default(WKTListenerMasks);
            var count = unityEvent.GetPersistentEventCount();
            for (var i = 0; i < count; i++) {
                var target = unityEvent.GetPersistentTarget(i);
                if (target == null) {
                    masks = masks.WithSkipped(i);
                    continue;
                }
                var targetTransform = target switch {
                    Component component => component.transform,
                    GameObject gameObject => gameObject.transform,
                    _ => null
                };
                var inside = targetTransform != null && targetTransform.IsChildOf(root);
                masks = masks.With(i, WKTWorldWiring.IsEntityLocalTarget(target.GetType(), inside));
            }
            return masks;
        }

        /// <summary>Runs an event's local part here, or the whole of it.</summary>
        private void Run(in EventRef reference, Transform? argument, bool whole) {
            var masks = reference.Masks;
            if (!whole && !masks.HasLocal) return;
            m_applying = true;
            try {
                if (whole && !masks.HasSkipped) {
                    switch (reference.Event) {
                        case UnityEvent<Transform> withTransform:
                            withTransform.Invoke(argument!);
                            break;
                        case UnityEvent plain:
                            plain.Invoke();
                            break;
                    }
                    return;
                }
                if (whole && masks.HasWorld)
                    WKTUnityEventFields.InvokeFiltered(reference.Event, argument, masks, true);
                WKTUnityEventFields.InvokeFiltered(reference.Event, argument, masks, false);
            }
            finally {
                m_applying = false;
            }
        }

        /// <summary>Host only: sends an event to every remote player near its entity, except <paramref name="except"/>.</summary>
        private void SendNear(in EventRef reference, WKTPlayer? except) {
            if (!reference.Masks.HasLocal) return;
            const float range_squared = WKTEntityStateSender.RELEVANCE_DISTANCE * WKTEntityStateSender.RELEVANCE_DISTANCE;
            var position = reference.Identity.transform.position;
            NetDataWriter? writer = null;
            for (var i = 0; i < WKTPlayerPositions.RemoteCount; i++) {
                var id = WKTPlayerPositions.GetRemoteId(i);
                if (except != null && except.Id == id) continue;
                if ((WKTPlayerPositions.GetRemotePosition(i) - position).sqrMagnitude > range_squared) continue;
                if (!m_network.TryGetPlayer(id, out var player) || player == null) continue;
                if (writer == null) {
                    writer = m_network.BeginMessage(WKTMessageId.EntityUnityEvent);
                    writer.Put(reference.Identity.Handle);
                    writer.Put(reference.Index);
                }
                m_network.SendToPlayer(player, writer, SendType.Reliable);
            }
        }

        /// <summary>
        /// An event from the machine simulating an entity. The host runs a client's whole and passes it on to the other players
        /// near it; a client runs the local part on its puppet.
        /// </summary>
        public void OnEvent(WKTPlayer sender, NetDataReader reader) {
            if (!reader.TryGetUShort(out var handle) || !reader.TryGetByte(out var index) || !m_sync.TryGetIdentity(handle, out var identity) ||
                !m_byIdentity.TryGetValue(identity!.GetInstanceID(), out var references) || index >= references.Count) return;
            var reference = references[index];
            if (m_sync.IsHost) {
                // Only the client simulating the entity speaks for it.
                if (!reader.TryGetByte(out var cause) || sender.IsHost || identity.Owner != sender.Id) return;
                if (cause != WKTDamageCause.NOBODY && !m_network.TryGetPlayer(cause, out _))
                    cause = WKTDamageCause.NOBODY;
                var previous = WKTDamageCause.Enter(cause);
                try {
                    Run(reference, null, true);
                }
                finally {
                    WKTDamageCause.Exit(previous);
                }
                SendNear(reference, sender);
                return;
            }
            if (sender.IsHost && identity.IsPuppet)
                Run(reference, null, false);
        }

        private readonly struct EventRef {
            public readonly WKTNetIdentity Identity;
            public readonly byte Index;
            public readonly UnityEventBase Event;
            public readonly WKTListenerMasks Masks;

            public EventRef(WKTNetIdentity identity, byte index, UnityEventBase unityEvent, WKTListenerMasks masks) {
                Identity = identity;
                Index = index;
                Event = unityEvent;
                Masks = masks;
            }
        }
    }
}
