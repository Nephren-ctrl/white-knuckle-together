using System.Collections.Generic;
using Steamworks.Data;
using UnityEngine;
using WhiteKnuckleTogether.Networking.LiteNetLib;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// Creature sounds in a synced co-op run. The game plays an entity's sounds from its own code (barks, alerts, hurt sounds,
    /// attack and state sounds), which only runs on the machine simulating it. That machine plays them and sends them; a puppet
    /// mutes its own calls (its animation events and death replay would only double them) and plays what it receives. Sounds
    /// played in a <see cref="WKTLocalReplayScope"/>, such as from a puppet's allowed ticks, stay local everywhere. Sent only
    /// to players within <see cref="WKTEntityStateSender.RELEVANCE_DISTANCE"/>. Owned by <see cref="WKTWorldSync"/>.
    /// </summary>
    internal sealed class WKTEntitySounds {
        private readonly WKTWorldSync m_sync;
        private readonly WKTNetwork m_network;
        /// <summary>Each clip handler's entity and its index among that entity's handlers, by the handler's instance id.</summary>
        private readonly Dictionary<int, Resolved> m_resolved = new();
        /// <summary>Each entity's clip handlers, in <c>GetComponentsInChildren</c> order, by the identity's instance id.</summary>
        private readonly Dictionary<int, UT_AudioClipHandler[]> m_handlers = new();

        public WKTEntitySounds(WKTWorldSync sync, WKTNetwork network) {
            m_sync = sync;
            m_network = network;
        }

        /// <summary>
        /// A clip handler is about to play or stop a sound. False skips the game's call, for a puppet's own sound. The simulating
        /// machine sends it on.
        /// </summary>
        public static bool BeforeSound(UT_AudioClipHandler handler, WKTEntitySoundKind kind, string sound, float volume, Vector3 position) {
            var sounds = WKTWorldSync.Instance?.Sounds;
            if (sounds == null || handler == null) return true;
            if (WKTMeleeReplay.SuppressesSound(sound)) return false;
            var localScope = WKTLocalReplayScope.IsOpen || WKTPuppetHitScope.IsOpen;
            var resolved = sounds.Resolve(handler);
            var identity = resolved.Identity;
            var replicated = identity != null && identity.Handle != 0 && identity.Entity != null;
            switch (WKTEntitySoundRules.Decide(replicated, replicated && identity!.IsPuppet, localScope)) {
                case WKTSoundRoute.Mute:
                    return false;
                case WKTSoundRoute.PlayAndSend:
                    sounds.Send(handler, identity!, resolved.Index, kind, sound, volume, position);
                    return true;
                default:
                    return true;
            }
        }

        /// <summary>Plays an entity's sound on this machine only, for effects every machine replays by itself.</summary>
        public static void PlayLocal(UT_AudioClipHandler? handler, string sound) {
            if (handler == null) return;
            WKTLocalReplayScope.Enter();
            try {
                handler.PlaySound(sound);
            }
            finally {
                WKTLocalReplayScope.Exit();
            }
        }

        /// <summary>
        /// Host only: plays an entity's sound here and sends it to the players near it except <paramref name="except"/>, who
        /// already played it themselves.
        /// </summary>
        public void PlayAndSendExcept(UT_AudioClipHandler? handler, string sound, WKTPlayer? except) {
            if (handler == null) return;
            PlayLocal(handler, sound);
            var resolved = Resolve(handler);
            if (!m_sync.IsHost || resolved.Identity == null || !WKTEntitySoundRules.TryFind(handler.groups, sound, out var group, out var set)) return;
            SendNear(new WKTEntitySound(resolved.Identity.Handle, resolved.Index, WKTEntitySoundKind.Play, group, set), resolved.Identity.transform.position, except);
        }

        public void Forget(WKTNetIdentity identity) => m_handlers.Remove(identity.GetInstanceID());

        public void Clear() {
            m_resolved.Clear();
            m_handlers.Clear();
        }

        /// <summary>The entity a handler belongs to. Looked up again when the entity it was cached with is gone or was unbound.</summary>
        private Resolved Resolve(UT_AudioClipHandler handler) {
            var id = handler.GetInstanceID();
            if (m_resolved.TryGetValue(id, out var cached) && cached.Identity != null && cached.Identity.Handle != 0) return cached;
            var identity = handler.GetComponentInParent<WKTNetIdentity>(true);
            if (identity == null || identity.Handle == 0) return default;
            var index = System.Array.IndexOf(GetHandlers(identity), handler);
            if (index < 0 || index > byte.MaxValue) return default;
            var resolved = new Resolved(identity, (byte)index);
            m_resolved[id] = resolved;
            return resolved;
        }

        private UT_AudioClipHandler[] GetHandlers(WKTNetIdentity identity) {
            var id = identity.GetInstanceID();
            if (m_handlers.TryGetValue(id, out var handlers)) return handlers;
            handlers = identity.GetComponentsInChildren<UT_AudioClipHandler>(true);
            m_handlers[id] = handlers;
            return handlers;
        }

        private void Send(UT_AudioClipHandler handler, WKTNetIdentity identity, byte index, WKTEntitySoundKind kind, string sound, float volume, Vector3 position) {
            if (!WKTEntitySoundRules.TryFind(handler.groups, sound, out var group, out var set)) return;
            var wirePosition = kind == WKTEntitySoundKind.PlayAt ? WKTWorldOffset.ToWire(position, WKTWorldOffset.Get()) : Vector3.zero;
            var entitySound = new WKTEntitySound(identity.Handle, index, kind, group, set, volume, wirePosition);
            if (m_sync.IsHost)
                SendNear(entitySound, identity.transform.position, null);
            else {
                var writer = m_network.BeginMessage(WKTMessageId.EntitySound);
                entitySound.Write(writer);
                m_network.SendToHost(writer, SendType.Reliable);
            }
        }

        /// <summary>Host only: sends a sound to every remote player near where it played, except <paramref name="except"/>.</summary>
        private void SendNear(in WKTEntitySound sound, Vector3 position, WKTPlayer? except) {
            const float range_squared = WKTEntityStateSender.RELEVANCE_DISTANCE * WKTEntityStateSender.RELEVANCE_DISTANCE;
            NetDataWriter? writer = null;
            for (var i = 0; i < WKTPlayerPositions.RemoteCount; i++) {
                var id = WKTPlayerPositions.GetRemoteId(i);
                if (except != null && except.Id == id) continue;
                if ((WKTPlayerPositions.GetRemotePosition(i) - position).sqrMagnitude > range_squared) continue;
                if (!m_network.TryGetPlayer(id, out var player) || player == null) continue;
                if (writer == null) {
                    writer = m_network.BeginMessage(WKTMessageId.EntitySound);
                    sound.Write(writer);
                }
                m_network.SendToPlayer(player, writer, SendType.Reliable);
            }
        }

        /// <summary>
        /// A sound from the machine simulating an entity. The host passes on a client's to the other players near it; every
        /// machine where the entity is a puppet plays it.
        /// </summary>
        public void OnSound(WKTPlayer sender, NetDataReader reader) {
            if (!WKTEntitySound.TryRead(reader, out var sound) || !m_sync.TryGetIdentity(sound.Handle, out var identity) || identity!.Entity == null) return;
            if (m_sync.IsHost) {
                // Only the client simulating the entity speaks for it.
                if (sender.IsHost || identity.Owner != sender.Id) return;
                SendNear(sound, identity.transform.position, sender);
            }
            else if (!sender.IsHost) return;
            if (identity.IsPuppet)
                Play(identity, sound);
        }

        /// <summary>Repeats a sound on a puppet, through the same audio set calls the handler's own methods make.</summary>
        private void Play(WKTNetIdentity identity, in WKTEntitySound sound) {
            var handlers = GetHandlers(identity);
            if (sound.Handler >= handlers.Length || handlers[sound.Handler] == null) return;
            var handler = handlers[sound.Handler];
            handler.Initialize();
            var audioSet = WKTEntitySoundRules.Get(handler.groups, sound.Group, sound.Set);
            if (audioSet == null) return;
            switch (sound.Kind) {
                case WKTEntitySoundKind.Play:
                    audioSet.Play(1f);
                    break;
                case WKTEntitySoundKind.PlayVolume:
                    audioSet.Play(sound.Volume);
                    break;
                case WKTEntitySoundKind.PlayAt:
                    audioSet.PlayAtPoint(1f, WKTWorldOffset.FromWire(sound.Position, WKTWorldOffset.Get()));
                    break;
                case WKTEntitySoundKind.Stop:
                    audioSet.StopSound();
                    break;
            }
        }

        private readonly struct Resolved {
            public readonly WKTNetIdentity? Identity;
            public readonly byte Index;

            public Resolved(WKTNetIdentity identity, byte index) {
                Identity = identity;
                Index = index;
            }
        }
    }
}
