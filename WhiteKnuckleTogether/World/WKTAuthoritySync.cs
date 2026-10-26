using System;
using System.Collections.Generic;
using Steamworks.Data;
using UnityEngine;
using WhiteKnuckleTogether.Networking.LiteNetLib;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// Which machine simulates each world object of a synced co-op run. The host decides, with <see cref="WKTAuthorityTable{THandle}"/>:
    /// <list type="bullet">
    /// <item>a prop or ridden creature one player holds is theirs;</item>
    /// <item>one several players hold is the host's;</item>
    /// <item>an enemy (see <see cref="WKTEngagement"/>) belongs to the living player it is engaged with;</item>
    /// <item>everything else is the host's.</item>
    /// </list>
    /// The owning machine runs the object's own logic and streams its state to the host, which shows it as a puppet and hands
    /// that state on to everyone else. Dead and departed players lose everything they had. Owned by <see cref="WKTWorldSync"/>.
    /// </summary>
    internal sealed class WKTAuthoritySync {
        public const byte HOST = WKTEntityAuthority.HOST;
        public const byte NOBODY = WKTEntityAuthority.NOBODY;
        /// <summary>How long an enemy stays with a player before a closer one or its own AI may take it.</summary>
        public const double MIN_HOLD = 1.5;
        /// <summary>How long after one player's hit took an enemy over before another's hit may.</summary>
        public const double DAMAGE_COOLDOWN = 0.5;
        /// <summary>How long the host waits for an owning client's states before taking its objects back.</summary>
        public const double OWNER_TIMEOUT = 3.0;
        /// <summary>How long a client keeps simulating a prop it let go of, while the host's states for it arrive.</summary>
        public const double HANDBACK = 0.35;
        private const SendType c_state_send_type = SendType.Unreliable | SendType.NoNagle;
        private readonly WKTWorldSync m_sync;
        private readonly WKTNetwork m_network;
        private readonly WKTAuthorityTable<ushort> m_table;
        private readonly List<WKTAuthorityTable<ushort>.Change> m_changes = new();
        /// <summary>Client only: objects this client owns, whose states it streams to the host.</summary>
        private readonly HashSet<ushort> m_owned = new();
        /// <summary>Client only: objects it let go of and still simulates until the time given.</summary>
        private readonly Dictionary<ushort, double> m_handbacks = new();
        /// <summary>Client only: owners for objects not bound here yet.</summary>
        private readonly Dictionary<ushort, byte> m_pendingOwners = new();
        /// <summary>Host only: the newest state each owning client sent, handed on to everyone else.</summary>
        private readonly Dictionary<ushort, OwnerState> m_latest = new();
        private readonly WKTEntityStateSender m_ownerSender = new(true);
        private readonly WKTSendClock m_ownerClock = new();
        private readonly float[] m_animatorValues = new float[WKTEntityState.MAX_ANIMATOR_VALUES];
        private readonly int[] m_animatorStates = new int[WKTEntityState.MAX_ANIMATOR_LAYERS];
        private readonly List<ushort> m_handleScratch = new();
        private readonly Func<NetDataWriter> m_beginOwnerPacket;
        private readonly Action<NetDataWriter> m_sendOwnerPacket;
        private readonly Action<byte, bool> m_onDeathChanged;
        private WKTDeathSync? m_deaths;

        public WKTAuthoritySync(WKTWorldSync sync, WKTNetwork network) {
            m_sync = sync;
            m_network = network;
            m_table = new WKTAuthorityTable<ushort>(IsEligible, MIN_HOLD, DAMAGE_COOLDOWN, OWNER_TIMEOUT);
            m_beginOwnerPacket = () => m_network.BeginMessage(WKTMessageId.OwnerStates);
            m_sendOwnerPacket = writer => m_network.SendToHost(writer, c_state_send_type);
            m_onDeathChanged = OnDeathChanged;
        }

        private byte LocalId => m_network.LocalPlayer?.Id ?? HOST;

        /// <summary>Whether this machine runs <paramref name="identity"/>'s own logic right now.</summary>
        public bool SimulatesHere(WKTNetIdentity identity) =>
            identity.Owner == HOST ? m_sync.IsHost : identity.Owner == LocalId || m_handbacks.ContainsKey(identity.Handle);

        /// <summary>Client only: whether this client owns the object, so the host's states for it are ignored.</summary>
        public bool IsOwnedHere(ushort handle) => m_owned.Contains(handle);

        /// <summary>Host only: the newest state the owning client sent for <paramref name="handle"/>.</summary>
        public bool TryGetLatest(ushort handle, out OwnerState state) => m_latest.TryGetValue(handle, out state);

        public void Dispose() {
            if (m_deaths != null)
                m_deaths.DeathChanged -= m_onDeathChanged;
            m_deaths = null;
        }

        #region Ticks

        /// <summary>Host only: takes back objects from silent owners, follows enemies' engagement and applies every change.</summary>
        public void HostTick(double now) {
            WatchDeaths();
            m_table.HostId = LocalId;
            if (m_table.Count > 0) {
                m_changes.Clear();
                m_table.Expire(now, m_changes);
                ApplyChanges(m_changes, now);
            }
            foreach (var identity in m_sync.HostIdentities) {
                if (identity == null || identity.Entity == null || identity.Entity.dead || !identity.gameObject.activeInHierarchy ||
                    !WKTEngagement.Supports(identity.Entity)) continue;
                PollEngagement(identity, now);
            }
        }

        /// <summary>Client only: streams the objects it owns, and turns objects it let go of back into puppets.</summary>
        public void ClientTick(double now) {
            WatchDeaths();
            if (m_handbacks.Count > 0) {
                m_handleScratch.Clear();
                foreach (var pair in m_handbacks) {
                    if (now >= pair.Value)
                        m_handleScratch.Add(pair.Key);
                }
                foreach (var handle in m_handleScratch) {
                    m_handbacks.Remove(handle);
                    if (m_sync.TryGetIdentity(handle, out var identity) && !SimulatesHere(identity!))
                        WKTWorldSync.MakePuppet(identity!);
                }
            }
            if (m_owned.Count == 0 || !m_ownerClock.TryAdvance(now, out var sequence)) return;
            var offset = WKTWorldOffset.Get();
            var localId = LocalId;
            m_ownerSender.BeginTick();
            foreach (var handle in m_owned) {
                if (!m_sync.TryGetIdentity(handle, out var identity) || !identity!.gameObject.activeInHierarchy) continue;
                SubmitOwned(identity, offset, localId, now);
            }
            if (m_ownerSender.PendingCount > 0)
                m_ownerSender.Flush(Vector3.zero, sequence, m_beginOwnerPacket, m_sendOwnerPacket, relevance: float.PositiveInfinity);
        }

        private void SubmitOwned(WKTNetIdentity identity, float offset, byte localId, double now) {
            var objectTransform = identity.transform;
            var pose = new WKTEntityPose(WKTWorldOffset.ToWire(objectTransform.position, offset), objectTransform.rotation);
            var entity = identity.Entity;
            var body = identity.Body;
            var engaged = entity != null && WKTEngagement.Supports(entity) ? WKTEngagement.PlayerOf(WKTEngagement.ReadTarget(entity), localId) : NOBODY;
            var motion = body != null && !body.isKinematic
                ? new WKTEntityMotion(Vector3.ClampMagnitude(body.linearVelocity, WKTWire.MAX_SPEED), Vector3.ClampMagnitude(body.angularVelocity, WKTWire.MAX_SPEED), engaged)
                : new WKTEntityMotion(Vector3.zero, Vector3.zero, engaged);
            var animatorCount = WKTWorldSync.ReadAnimator(identity.Animator, m_animatorValues);
            var stateCount = WKTWorldSync.ReadAnimatorStates(identity.Animator, m_animatorStates);
            m_ownerSender.Submit(identity.Handle, pose, entity != null ? entity.health : 0f, entity != null && entity.dead, m_animatorValues.AsSpan(0, animatorCount), now,
                m_animatorStates.AsSpan(0, stateCount), WKTEnemyHits.GetAiBits(entity), motion);
        }

        /// <summary>Host only: follows what a host-simulated enemy goes for, and moves an engaged one to a clearly closer player.</summary>
        private void PollEngagement(WKTNetIdentity identity, double now) {
            var handle = identity.Handle;
            var hostId = LocalId;
            var engaged = m_table.EngagedOf(handle);
            if (identity.Owner == HOST) {
                var seen = WKTEngagement.PlayerOf(WKTEngagement.ReadTarget(identity.Entity!), hostId);
                if (seen != engaged) {
                    Engage(handle, seen, seen == NOBODY ? WKTEngageReason.Lost : WKTEngageReason.Acquired, now);
                    engaged = m_table.EngagedOf(handle);
                }
            }
            if (engaged == NOBODY) return;
            var current = engaged == hostId ? WKTTargets.LOCAL : engaged;
            var nearest = WKTTargets.Choose(identity.transform.position, current);
            if (nearest == current || !MayEngage(identity.Entity, nearest)) return;
            Engage(handle, nearest == WKTTargets.LOCAL ? hostId : nearest, WKTEngageReason.Nearest, now);
        }

        /// <summary>Whether the enemy may go to that player at all, such as a swimmer to a player out of its water.</summary>
        private static bool MayEngage(GameEntity? entity, byte target) {
            Vector3 position;
            if (target == WKTTargets.LOCAL) {
                var local = ENT_Player.playerObject;
                if (local == null) return true;
                position = local.transform.position;
            }
            else if (!WKTPlayerPositions.TryGetRemotePosition(target, out position)) return true;
            return WKTSwimmers.MayEngage(entity, position);
        }

        #endregion

        #region Host decisions

        /// <summary>
        /// Host only: an enemy's engaged player should change. Moves its authority if the table agrees. Only enemies whose
        /// target is read per type can follow a player; the rest, such as barnacles, stay with the host whoever hits them.
        /// </summary>
        public void Engage(ushort handle, byte playerId, WKTEngageReason reason, double now) {
            if (!m_sync.IsHost || !m_sync.TryGetIdentity(handle, out var identity) || !WKTEngagement.Supports(identity!.Entity)) return;
            // A tick latched onto its victim stays theirs: a hit on it reaches their machine, which knocks it off.
            if (WKTEngagement.IsLatched(identity.Entity)) return;
            m_table.HostId = LocalId;
            if (m_table.SetEngaged(handle, playerId, reason, now, out var owner))
                ApplyChange(handle, owner, now);
        }

        /// <summary>The local player grabbed an object. A client takes it over at once and tells the host.</summary>
        public void OnLocalGrab(WKTNetIdentity identity, byte part, Vector3 anchor) {
            var handle = identity.Handle;
            if (handle == 0) return;
            var now = Time.realtimeSinceStartupAsDouble;
            if (m_sync.IsHost) {
                m_table.HostId = LocalId;
                if (m_table.Grab(handle, LocalId, now, out var owner))
                    ApplyChange(handle, owner, now);
                return;
            }
            // Optimistic: the host may still give it to someone else, or keep it if others hold it too.
            m_handbacks.Remove(handle);
            if (identity.IsPuppet) {
                WKTWorldSync.ReleasePuppet(identity);
                m_sync.ClearBuffer(handle);
            }
            if (identity.Owner == HOST || identity.Owner == LocalId)
                m_owned.Add(handle);
            var writer = m_network.BeginMessage(WKTMessageId.EntityGrab);
            new WKTEntityGrab(handle, part, anchor).Write(writer);
            m_network.SendToHost(writer, SendType.Reliable);
        }

        /// <summary>The local player let go of an object with every hand. A client tells the host how it was moving.</summary>
        public void OnLocalRelease(WKTNetIdentity identity) {
            var handle = identity.Handle;
            if (handle == 0) return;
            var now = Time.realtimeSinceStartupAsDouble;
            if (m_sync.IsHost) {
                if (m_table.Release(handle, LocalId, now, out var owner))
                    ApplyChange(handle, owner, now);
                return;
            }
            var writer = m_network.BeginMessage(WKTMessageId.EntityRelease);
            GetRelease(identity).Write(writer);
            m_network.SendToHost(writer, SendType.Reliable);
        }

        private static WKTEntityRelease GetRelease(WKTNetIdentity identity) {
            var objectTransform = identity.transform;
            var body = identity.Body;
            var moving = body != null && !body.isKinematic;
            return new WKTEntityRelease(identity.Handle, new WKTEntityPose(WKTWorldOffset.ToWire(objectTransform.position, WKTWorldOffset.Get()), objectTransform.rotation),
                moving ? Vector3.ClampMagnitude(body!.linearVelocity, WKTWire.MAX_SPEED) : Vector3.zero,
                moving ? Vector3.ClampMagnitude(body!.angularVelocity, WKTWire.MAX_SPEED) : Vector3.zero);
        }

        public void OnEntityGrab(WKTPlayer sender, NetDataReader reader) {
            if (!m_sync.IsHost || sender.IsHost || !WKTEntityGrab.TryRead(reader, out var grab)) return;
            if (!m_sync.TryGetIdentity(grab.Handle, out var identity) || identity!.Entity == null || identity.Entity.dead) {
                SendCurrent(sender, grab.Handle);
                return;
            }
            var now = Time.realtimeSinceStartupAsDouble;
            m_table.HostId = LocalId;
            if (m_table.Grab(grab.Handle, sender.Id, now, out var owner))
                ApplyChange(grab.Handle, owner, now);
            else
                SendCurrent(sender, grab.Handle);
            m_sync.SharedHolds.OnGrab(grab.Handle, sender.Id, grab.Anchor);
        }

        public void OnEntityRelease(WKTPlayer sender, NetDataReader reader) {
            if (!m_sync.IsHost || sender.IsHost || !WKTEntityRelease.TryRead(reader, out var release)) return;
            m_sync.SharedHolds.OnRelease(release.Handle, sender.Id);
            var now = Time.realtimeSinceStartupAsDouble;
            var wasOwner = m_table.OwnerOf(release.Handle) == sender.Id;
            if (!m_table.Release(release.Handle, sender.Id, now, out var owner)) return;
            if (wasOwner && m_latest.TryGetValue(release.Handle, out var latest))
                // The throw carries on from where the thrower let go.
                m_latest[release.Handle] = latest.With(release.Pose, new WKTEntityMotion(release.Velocity, release.AngularVelocity, latest.Motion.Engaged));
            ApplyChange(release.Handle, owner, now);
        }

        /// <summary>Host only: a batch of states from a client for the objects it simulates. Only its own objects' entries count.</summary>
        public void OnOwnerStates(WKTPlayer sender, NetDataReader reader) {
            if (!m_sync.IsHost || sender.IsHost || !WKTEntityState.TryReadHeader(reader, out var sequence, out var count)) return;
            var now = Time.realtimeSinceStartupAsDouble;
            for (var i = 0; i < count; i++) {
                if (!WKTEntityState.TryRead(reader, out var state, m_animatorValues, m_animatorStates)) return;
                if (!m_table.Heard(state.Handle, sender.Id, now) || !m_sync.TryGetIdentity(state.Handle, out var identity) || !identity!.IsPuppet) continue;
                m_sync.ApplyRemoteState(identity, sequence, state, m_animatorValues.AsSpan(0, state.Has(WKTEntityState.FLAG_ANIMATOR) ? state.AnimatorCount : 0),
                    m_animatorStates.AsSpan(0, state.Has(WKTEntityState.FLAG_ANIMATOR_STATES) ? state.StateCount : 0), now);
                m_latest.TryGetValue(state.Handle, out var latest);
                m_latest[state.Handle] = latest.With(state);
                if (state.Has(WKTEntityState.FLAG_ENGAGED) && identity.Entity != null && WKTEngagement.Supports(identity.Entity) && state.Engaged != sender.Id)
                    // A client's enemy only ever sees its own player: anything else means it lost them.
                    Engage(state.Handle, NOBODY, WKTEngageReason.Lost, now);
            }
        }

        private void ApplyChanges(List<WKTAuthorityTable<ushort>.Change> changes, double now) {
            foreach (var change in changes)
                ApplyChange(change.Handle, change.Owner, now);
            changes.Clear();
        }

        /// <summary>Host only: an object's owner changed. Hands it over here and tells everyone, with its last known motion.</summary>
        private void ApplyChange(ushort handle, byte owner, double now) {
            if (!m_sync.TryGetIdentity(handle, out var identity)) {
                m_table.Forget(handle);
                return;
            }
            var authority = GetHandoff(identity!, owner, m_table.EngagedOf(handle));
            var previous = identity!.Owner;
            if (owner == HOST)
                m_latest.Remove(handle);
            Apply(identity, authority, now);
            var writer = m_network.BeginMessage(WKTMessageId.EntityAuthority);
            authority.Write(writer);
            m_network.Broadcast(writer, SendType.Reliable);
            m_sync.SharedHolds.OnOwnerChanged(identity, previous, owner);
        }

        /// <summary>Host only: where the object is and how it moves, from its owning client's newest state or the host's own copy.</summary>
        private WKTEntityAuthority GetHandoff(WKTNetIdentity identity, byte owner, byte engaged) {
            var health = identity.Entity != null ? identity.Entity.health : 0f;
            if (identity.Owner != HOST && m_latest.TryGetValue(identity.Handle, out var latest) && latest.HasPose)
                return new WKTEntityAuthority(identity.Handle, owner, latest.Pose, latest.Motion.Velocity, latest.Motion.AngularVelocity, health, engaged);
            var objectTransform = identity.transform;
            var body = identity.Body;
            var moving = body != null && !body.isKinematic;
            return new WKTEntityAuthority(identity.Handle, owner,
                new WKTEntityPose(WKTWorldOffset.ToWire(objectTransform.position, WKTWorldOffset.Get()), objectTransform.rotation),
                moving ? Vector3.ClampMagnitude(body!.linearVelocity, WKTWire.MAX_SPEED) : Vector3.zero,
                moving ? Vector3.ClampMagnitude(body!.angularVelocity, WKTWire.MAX_SPEED) : Vector3.zero, health, engaged);
        }

        /// <summary>Host only: tells one client who owns an object, so a refused optimistic grab is undone.</summary>
        private void SendCurrent(WKTPlayer player, ushort handle) {
            if (!m_sync.TryGetIdentity(handle, out var identity)) return;
            var writer = m_network.BeginMessage(WKTMessageId.EntityAuthority);
            GetHandoff(identity!, m_table.OwnerOf(handle), m_table.EngagedOf(handle)).Write(writer);
            m_network.SendToPlayer(player, writer, SendType.Reliable);
        }

        /// <summary>Host only: tells a player who (re)connected about every object a client owns.</summary>
        public void SendAll(WKTPlayer player) {
            foreach (var identity in m_sync.HostIdentities) {
                if (identity == null || identity.Owner == HOST) continue;
                var writer = m_network.BeginMessage(WKTMessageId.EntityAuthority);
                GetHandoff(identity, identity.Owner, m_table.EngagedOf(identity.Handle)).Write(writer);
                m_network.SendToPlayer(player, writer, SendType.Reliable);
            }
        }

        #endregion

        #region Applying

        public void OnEntityAuthority(WKTPlayer sender, NetDataReader reader) {
            if (m_sync.IsHost || !sender.IsHost || !WKTEntityAuthority.TryRead(reader, out var authority)) return;
            if (!m_sync.TryGetIdentity(authority.Handle, out var identity)) {
                if (authority.Owner == HOST)
                    m_pendingOwners.Remove(authority.Handle);
                else
                    m_pendingOwners[authority.Handle] = authority.Owner;
                return;
            }
            Apply(identity!, authority, Time.realtimeSinceStartupAsDouble);
        }

        /// <summary>Client only: a host object was bound here. It takes the owner the host gave it before it was bound.</summary>
        public void OnBound(WKTNetIdentity identity) {
            // A bind made it a puppet again, so whatever this client simulated of it before is over.
            identity.Owner = HOST;
            m_owned.Remove(identity.Handle);
            m_handbacks.Remove(identity.Handle);
            if (!m_pendingOwners.Remove(identity.Handle, out var owner)) return;
            identity.Owner = owner;
            if (owner != LocalId) return;
            WKTWorldSync.ReleasePuppet(identity);
            m_owned.Add(identity.Handle);
        }

        /// <summary>
        /// Makes <paramref name="identity"/> owned by <paramref name="authority"/>'s owner here: the owner takes it over from the
        /// handed over motion and points it at its engaged player; everyone else shows it as a puppet.
        /// </summary>
        private void Apply(WKTNetIdentity identity, in WKTEntityAuthority authority, double now) {
            var handle = identity.Handle;
            var wasHere = SimulatesHere(identity);
            identity.Owner = authority.Owner;
            m_sync.ClearBuffer(handle);
            var local = LocalId;
            var here = authority.Owner == HOST ? m_sync.IsHost : authority.Owner == local;
            if (here) {
                m_handbacks.Remove(handle);
                if (!m_sync.IsHost)
                    m_owned.Add(handle);
                if (identity.IsPuppet) {
                    WKTWorldSync.ReleasePuppet(identity);
                    TakeOver(identity, authority);
                    WKTBearTraps.OnTakenOver(identity);
                    m_sync.Presentation.OnTakenOver(identity);
                }
                if (identity.Entity != null && WKTEngagement.Supports(identity.Entity))
                    WKTEngagement.SetTarget(identity.Entity, TargetFor(authority.Engaged, local));
                return;
            }
            m_owned.Remove(handle);
            if (wasHere && !m_sync.IsHost)
                m_sync.SharedHolds.OnStoppedSimulating(identity);
            if (identity.IsPuppet) return;
            // A client keeps simulating a prop it just let go of until the host's states for it arrive, so it does not stop dead.
            if (wasHere && !m_sync.IsHost && identity.Prop != null)
                m_handbacks[handle] = now + HANDBACK;
            else
                WKTWorldSync.MakePuppet(identity);
        }

        private static void TakeOver(WKTNetIdentity identity, in WKTEntityAuthority authority) {
            identity.transform.SetPositionAndRotation(WKTWorldOffset.FromWire(authority.Pose.Position, WKTWorldOffset.Get()), authority.Pose.Rotation);
            var body = identity.Body;
            if (body != null && !body.isKinematic) {
                body.linearVelocity = authority.Velocity;
                body.angularVelocity = authority.AngularVelocity;
            }
            if (identity.Entity != null && !identity.Entity.dead)
                identity.Entity.health = authority.Health;
        }

        /// <summary>What an enemy taken over here should go for: the local player, or on the host a remote player's stand-in.</summary>
        private Transform? TargetFor(byte engaged, byte localId) {
            if (engaged == NOBODY) return null;
            if (engaged == localId) {
                var local = ENT_Player.playerObject;
                return local != null && !local.dead ? local.transform : null;
            }
            return m_sync.IsHost && WKTPlayerProxy.TryGet(engaged, out var proxy) ? proxy!.transform : null;
        }

        #endregion

        #region Lifetime

        /// <summary>
        /// Host only: the host's player took an object another machine simulates, such as a ruby roach into their bag. The host
        /// simulates it from now on, with no engagement left to hand it back.
        /// </summary>
        public void Reclaim(ushort handle) {
            if (!m_sync.IsHost) return;
            m_table.Forget(handle);
            ApplyChange(handle, HOST, Time.realtimeSinceStartupAsDouble);
        }

        /// <summary>The object is gone or dead: nobody owns it any more, and its death plays out on every machine.</summary>
        public void Forget(ushort handle) {
            m_table.Forget(handle);
            m_latest.Remove(handle);
            m_owned.Remove(handle);
            m_handbacks.Remove(handle);
            m_pendingOwners.Remove(handle);
            m_ownerSender.Forget(handle);
        }

        /// <summary>Host only: a player left, so the host takes back everything they had.</summary>
        public void OnPlayerLeft(byte playerId) {
            if (!m_sync.IsHost) return;
            var now = Time.realtimeSinceStartupAsDouble;
            m_sync.SharedHolds.OnPlayerGone(playerId);
            m_sync.Ropes.OnPlayerGone(playerId);
            m_changes.Clear();
            m_table.OnPlayerGone(playerId, now, m_changes);
            ApplyChanges(m_changes, now);
        }

        private void WatchDeaths() {
            var deaths = WKTPlayerDeaths.Sync;
            if (deaths == m_deaths) return;
            if (m_deaths != null)
                m_deaths.DeathChanged -= m_onDeathChanged;
            m_deaths = deaths;
            if (m_deaths != null)
                m_deaths.DeathChanged += m_onDeathChanged;
        }

        /// <summary>A dead player owns nothing: a dying client stops simulating at once, and the host takes their objects back.</summary>
        private void OnDeathChanged(byte playerId, bool dead) {
            if (!dead) return;
            if (playerId == LocalId && !m_sync.IsHost) {
                foreach (var handle in m_owned) {
                    if (m_sync.TryGetIdentity(handle, out var identity))
                        WKTWorldSync.MakePuppet(identity!);
                }
                m_owned.Clear();
                m_handbacks.Clear();
            }
            OnPlayerLeft(playerId);
        }

        /// <summary>The host changed: the new host simulates everything again, and each client goes back to showing puppets.</summary>
        public void OnHostChanged() {
            if (!m_sync.IsHost) {
                foreach (var handle in m_owned) {
                    if (m_sync.TryGetIdentity(handle, out var identity))
                        WKTWorldSync.MakePuppet(identity!);
                }
                foreach (var handle in m_handbacks.Keys) {
                    if (m_sync.TryGetIdentity(handle, out var identity))
                        WKTWorldSync.MakePuppet(identity!);
                }
            }
            foreach (var identity in UnityEngine.Object.FindObjectsByType<WKTNetIdentity>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                identity.Owner = HOST;
            m_owned.Clear();
            m_handbacks.Clear();
            m_pendingOwners.Clear();
            m_latest.Clear();
            m_table.Clear();
            m_table.HostId = LocalId;
            m_ownerSender.Clear();
        }

        private bool IsEligible(byte playerId) => m_network.TryGetPlayer(playerId, out var player) && player != null && !WKTPlayerDeaths.IsDead(playerId);

        #endregion

        /// <summary>Host only: the newest pose, motion and AI bits an owning client sent for one object.</summary>
        public readonly struct OwnerState {
            public readonly WKTEntityPose Pose;
            public readonly WKTEntityMotion Motion;
            public readonly byte AiBits;
            public readonly bool HasPose;

            private OwnerState(in WKTEntityPose pose, in WKTEntityMotion motion, byte aiBits, bool hasPose) {
                Pose = pose;
                Motion = motion;
                AiBits = aiBits;
                HasPose = hasPose;
            }

            public OwnerState With(in WKTEntityState state) {
                var pose = state.Has(WKTEntityState.FLAG_POSE) ? state.Pose : Pose;
                var velocity = state.Has(WKTEntityState.FLAG_VELOCITY) ? state.Velocity : Motion.Velocity;
                var angular = state.Has(WKTEntityState.FLAG_VELOCITY) ? state.AngularVelocity : Motion.AngularVelocity;
                var engaged = state.Has(WKTEntityState.FLAG_ENGAGED) ? state.Engaged : Motion.Engaged;
                var aiBits = state.Has(WKTEntityState.FLAG_AI) ? state.AiBits : AiBits;
                return new OwnerState(pose, new WKTEntityMotion(velocity, angular, engaged), aiBits, HasPose || state.Has(WKTEntityState.FLAG_POSE));
            }

            public OwnerState With(in WKTEntityPose pose, in WKTEntityMotion motion) => new(pose, motion, AiBits, true);
        }
    }
}
