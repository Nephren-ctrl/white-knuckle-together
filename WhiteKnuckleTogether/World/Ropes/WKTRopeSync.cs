using System;
using System.Collections.Generic;
using HarmonyLib;
using Steamworks.Data;
using UnityEngine;
using WhiteKnuckleTogether.Networking.LiteNetLib;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// Ropes and chains (<c>ENV_Rope</c>) in a synced co-op run. One machine simulates each rope, decided by the host with
    /// <see cref="WKTAuthorityTable{THandle}"/>:
    /// <list type="bullet">
    /// <item>the one living player holding it;</item>
    /// <item>the host, when several players or nobody hold it.</item>
    /// </list>
    /// That machine streams the rope link by link (<see cref="WKTRopeCodec"/>) while it moves, and every so often while it
    /// rests; everyone else shows it as a puppet whose own simulation is skipped. A player holding a rope the host simulates
    /// sends their swing and grab push for the host to apply. A client that takes hold of a rope takes it over at once, and
    /// one that lets go keeps simulating it for <see cref="HANDBACK"/>, so it never stops dead. A rope with no state yet is
    /// simulated where it is, so it never shows collapsed. Ropes are told apart by their level content key, or for a rope a
    /// thrown rebar left by its placement (<see cref="PreKey"/>). Owned by <see cref="WKTWorldSync"/>.
    /// </summary>
    internal sealed class WKTRopeSync {
        public const byte HOST = WKTEntityAuthority.HOST;
        public const double HANDBACK = WKTAuthoritySync.HANDBACK;
        /// <summary>How long a let go waits for the local player to take hold again, as when moving hand over hand.</summary>
        public const double RELEASE_GRACE = 0.15;
        /// <summary>How long a rope counts as moving after someone takes hold or lets go, whatever it does.</summary>
        public const double AWAKE_AFTER_HOLD = 1.0;
        /// <summary>How often a resting rope is sent anyway, for anyone who missed its last moving state.</summary>
        public const double KEYFRAME_INTERVAL = 2.0;
        /// <summary>How long a remote holder's swing is applied after it was last heard.</summary>
        public const double SWING_STALE = 0.25;
        /// <summary>A node moving further than this since the last send means the rope still moves.</summary>
        public const float MOVE_THRESHOLD = 0.001f;
        /// <summary>The end moving faster than this means the rope still moves.</summary>
        public const float SPEED_THRESHOLD = 0.05f;
        /// <summary>Below this the swing sound pauses, as the game's does when the end stops.</summary>
        private const float c_silent_speed = 1e-3f;
        /// <summary>Keeps every unreliable rope packet to one network packet, so losing one fragment never loses the rest.</summary>
        private const int c_max_packet = 1100;
        private const SendType c_state_send_type = SendType.Unreliable | SendType.NoNagle;
        private static readonly AccessTools.FieldRef<CL_Handhold_Rope, ENV_Rope> s_handholdRope = AccessTools.FieldRefAccess<CL_Handhold_Rope, ENV_Rope>("rope");
        private static readonly Action<ENV_Rope> s_renderRope = AccessTools.MethodDelegate<Action<ENV_Rope>>(AccessTools.Method(typeof(ENV_Rope), "RenderRope"));
        private static readonly HashSet<string> s_unsynced = new();
        private static readonly List<ENV_Rope> s_ropeScratch = new();
        /// <summary>Set while this class pushes a rope itself, so the push is not taken for the local player's.</summary>
        private static bool s_applyingPush;
        /// <summary>Set while the local player takes hold of a rope, whose push then is a grab's one-off push.</summary>
        private static bool s_grabbing;
        private readonly WKTWorldSync m_sync;
        private readonly WKTNetwork m_network;
        private readonly WKTAuthorityTable<ulong> m_table;
        private readonly List<WKTAuthorityTable<ulong>.Change> m_changes = new();
        private readonly Dictionary<ulong, WKTSyncedRope> m_byKey = new();
        private readonly Dictionary<ENV_Rope, WKTSyncedRope> m_byRope = new();
        private readonly Dictionary<Rigidbody, WKTSyncedRope> m_byBody = new();
        private readonly List<WKTSyncedRope> m_unkeyed = new();
        /// <summary>Keys for ropes built from a placement, by instance id, taken when they start.</summary>
        private readonly Dictionary<int, ulong> m_preKeys = new();
        /// <summary>Client only: owners for ropes not built here yet.</summary>
        private readonly Dictionary<ulong, byte> m_pendingOwners = new();
        private readonly WKTSendClock m_clock = new();
        private readonly WKTRopeLink[] m_links = new WKTRopeLink[WKTRopeState.MAX_LINKS];
        private readonly Vector3[] m_nodes = new Vector3[WKTRopeCodec.MAX_NODES];

        public WKTRopeSync(WKTWorldSync sync, WKTNetwork network) {
            m_sync = sync;
            m_network = network;
            m_table = new WKTAuthorityTable<ulong>(IsEligible, WKTAuthoritySync.MIN_HOLD, WKTAuthoritySync.DAMAGE_COOLDOWN, WKTAuthoritySync.OWNER_TIMEOUT);
        }

        private byte LocalId => m_network.LocalPlayer?.Id ?? HOST;

        /// <summary>
        /// What a rope's end moves at, for the game's own grab and jump pushes: as shown for a puppet, whose kinematic body
        /// does not move by itself.
        /// </summary>
        public static Vector3 GetVelocity(Rigidbody body) {
            var sync = WKTWorldSync.Instance;
            if (sync != null && sync.Ropes.m_byBody.TryGetValue(body, out var rope) && rope.IsPuppet) return rope.ShownVelocity;
            return body.linearVelocity;
        }

        /// <summary>Opens or closes the local player's taking hold of a rope, inside which its push is the grab's one-off push.</summary>
        public static void SetGrabbing(bool grabbing) => s_grabbing = grabbing;

        #region Registration

        /// <summary>A rope started: it is tracked from now on, and synced once it has a key.</summary>
        public void Register(ENV_Rope rope) {
            if (m_byRope.ContainsKey(rope)) return;
            var nodeCount = rope.nodes.Count;
            if (nodeCount < 2 || nodeCount > WKTRopeCodec.MAX_NODES || rope.GetTargetRigidbody() == null) {
                if (s_unsynced.Add(rope.name))
                    WKT.Instance.Logger.LogWarning($"Rope {rope.name} has {nodeCount} nodes and cannot be synced. Each player sees their own.");
                return;
            }
            var entry = new WKTSyncedRope(rope, nodeCount);
            m_byRope[rope] = entry;
            m_byBody[entry.Target!] = entry;
            if (!TryKey(entry))
                m_unkeyed.Add(entry);
        }

        /// <summary>Every rope that started before the run's sync did.</summary>
        public void RegisterExisting() {
            foreach (var rope in UnityEngine.Object.FindObjectsByType<ENV_Rope>(FindObjectsInactive.Include, FindObjectsSortMode.None)) {
                if (rope.nodes.Count > 0)
                    Register(rope);
            }
        }

        /// <summary>The ropes in <paramref name="root"/>, built from a placement, get the placement's keys once they start.</summary>
        public void PreKey(GameObject root, uint placedId) {
            root.GetComponentsInChildren(true, s_ropeScratch);
            for (var i = 0; i < s_ropeScratch.Count; i++)
                m_preKeys[s_ropeScratch[i].GetInstanceID()] = WKTPlacedRopeRegistry.GetRopeKey(placedId, i);
            s_ropeScratch.Clear();
        }

        public void Unregister(ENV_Rope rope) {
            if (!m_byRope.Remove(rope, out var entry)) return;
            m_preKeys.Remove(rope.GetInstanceID());
            if (entry.Target != null)
                m_byBody.Remove(entry.Target);
            m_unkeyed.Remove(entry);
            if (entry.Key == 0 || !m_byKey.TryGetValue(entry.Key, out var keyed) || keyed != entry) return;
            m_byKey.Remove(entry.Key);
            m_table.Forget(entry.Key);
        }

        private bool TryKey(WKTSyncedRope entry) {
            var rope = entry.Rope;
            if (!m_preKeys.Remove(rope.GetInstanceID(), out var key) && !WKTContentSeeding.TryGetKey(rope, out key)) return false;
            if (m_byKey.TryGetValue(key, out var existing) && existing.Rope != null) {
                if (s_unsynced.Add(rope.name))
                    WKT.Instance.Logger.LogWarning($"Rope {rope.name} has the same key as {existing.Rope.name} and cannot be synced.");
                return true;
            }
            entry.Key = key;
            m_byKey[key] = entry;
            if (m_pendingOwners.Remove(key, out var owner))
                entry.Owner = owner;
            return true;
        }

        private void KeyPending() {
            for (var i = m_unkeyed.Count - 1; i >= 0; i--) {
                var entry = m_unkeyed[i];
                if (entry.Rope == null || TryKey(entry))
                    m_unkeyed.RemoveAt(i);
            }
        }

        #endregion

        #region Ticks

        public void Update(double now) {
            if (m_unkeyed.Count > 0)
                KeyPending();
            foreach (var entry in m_byKey.Values) {
                if (entry.ReleaseAt <= now)
                    FinishRelease(entry, now);
                if (entry.HandbackUntil <= 0 || now < entry.HandbackUntil) continue;
                entry.HandbackUntil = 0;
                UpdatePuppet(entry);
            }
            if (!m_clock.TryAdvance(now, out var sequence)) return;
            if (m_sync.IsHost && m_table.Count > 0) {
                m_changes.Clear();
                m_table.Expire(now, m_changes);
                ApplyChanges(now);
            }
            SendStates(sequence, now);
            if (!m_sync.IsHost)
                SendSwings(now);
        }

        /// <summary>
        /// Before a rope's own physics step. A puppet shows the newest state instead and skips it; on the host, a rope remote
        /// players hold is pushed by their swings first.
        /// </summary>
        public bool BeforeFixedUpdate(ENV_Rope rope) {
            if (!m_byRope.TryGetValue(rope, out var entry) || CL_GameManager.gMan == null || CL_GameManager.gMan.isPaused) return true;
            var now = Time.realtimeSinceStartupAsDouble;
            if (!entry.IsPuppet) {
                if (m_sync.IsHost)
                    ApplyRemoteSwings(entry, now);
                return true;
            }
            if (entry.Buffer.TrySample(now, entry.Shown, out var velocity))
                entry.ShownVelocity = velocity;
            Show(entry);
            return false;
        }

        /// <summary>A push on a rope. On a puppet the local player's push goes to the host instead of the kinematic end.</summary>
        public bool BeforeAddVelocity(ENV_Rope rope, Vector3 push) {
            if (s_applyingPush || !m_byRope.TryGetValue(rope, out var entry) || !entry.IsPuppet) return true;
            if (s_grabbing) {
                var writer = m_network.BeginMessage(WKTMessageId.RopeForce);
                new WKTRopeForce(entry.Key, push, WKTRopeForce.FLAG_IMPULSE).Write(writer);
                m_network.SendToHost(writer, SendType.Reliable);
            }
            else {
                entry.LocalSwing = push;
                entry.LocalSwingTime = Time.realtimeSinceStartupAsDouble;
            }
            return false;
        }

        #endregion

        #region Sending

        private void SendStates(ushort sequence, double now) {
            NetDataWriter? writer = null;
            var countAt = 0;
            var count = 0;
            foreach (var entry in m_byKey.Values) {
                if (!Owns(entry) || entry.Rope == null || !entry.Rope.isActiveAndEnabled) continue;
                var velocity = Capture(entry);
                if (!IsAwake(entry, velocity, now) && now - entry.LastSent < KEYFRAME_INTERVAL) continue;
                entry.Rebuilt.CopyTo(entry.Sent, 0);
                entry.LastSent = now;
                if (writer != null && (count == byte.MaxValue || writer.Length + WKTRopeState.GetEntrySize(entry.Links.Length) > c_max_packet)) {
                    FlushStates(writer, countAt, count, null);
                    writer = null;
                }
                if (writer == null) {
                    writer = BeginStates(sequence, out countAt);
                    count = 0;
                }
                WKTRopeState.WriteEntry(writer, entry.Key, velocity, entry.Links);
                count++;
            }
            if (writer != null)
                FlushStates(writer, countAt, count, null);
        }

        private NetDataWriter BeginStates(ushort sequence, out int countAt) {
            var writer = m_network.BeginMessage(WKTMessageId.RopeStates);
            writer.Put(sequence);
            countAt = writer.Length;
            writer.Put((byte)0);
            return writer;
        }

        private void FlushStates(NetDataWriter writer, int countAt, int count, WKTPlayer? except) {
            writer.Data[countAt] = (byte)count;
            if (m_sync.IsHost)
                m_network.Broadcast(writer, c_state_send_type, except);
            else
                m_network.SendToHost(writer, c_state_send_type);
        }

        /// <summary>
        /// Whether the rope still moves. A rope its holder simulates always counts, so a client holding a still rope keeps
        /// being heard and keeps it.
        /// </summary>
        private static bool IsAwake(WKTSyncedRope entry, Vector3 velocity, double now) {
            if (entry.LocalHolding || velocity.sqrMagnitude > SPEED_THRESHOLD * SPEED_THRESHOLD || now - entry.LastHoldChange < AWAKE_AFTER_HOLD) return true;
            for (var i = 1; i < entry.NodeCount; i++) {
                if ((entry.Rebuilt[i] - entry.Sent[i]).sqrMagnitude > MOVE_THRESHOLD * MOVE_THRESHOLD) return true;
            }
            return false;
        }

        /// <summary>Client only: the local player's swing on each rope the host simulates for them.</summary>
        private void SendSwings(double now) {
            foreach (var entry in m_byKey.Values) {
                if (!entry.LocalHolding || !entry.IsPuppet || now - entry.LocalSwingTime >= SWING_STALE || entry.LocalSwing.sqrMagnitude <= 0f) continue;
                var writer = m_network.BeginMessage(WKTMessageId.RopeForce);
                new WKTRopeForce(entry.Key, entry.LocalSwing, 0).Write(writer);
                m_network.SendToHost(writer, c_state_send_type);
            }
        }

        /// <summary>Encodes the rope as simulated here into its links and rebuilt nodes. Returns its end's velocity.</summary>
        private static Vector3 Capture(WKTSyncedRope entry) {
            var rope = entry.Rope;
            var ropeTransform = rope.transform;
            var anchor = ropeTransform.position;
            var inverse = Quaternion.Inverse(ropeTransform.rotation);
            var nodes = rope.nodes;
            for (var i = 0; i < entry.NodeCount; i++)
                entry.Local[i] = inverse * (nodes[i].position - anchor);
            WKTRopeCodec.Encode(entry.Local, entry.MaxLength, entry.Links, entry.Rebuilt);
            var target = entry.Target;
            return target != null && !target.isKinematic ? target.linearVelocity : entry.ShownVelocity;
        }

        /// <summary>
        /// Writes the rope's state as this machine has it: the owning client's newest on the host, else its own. Its shape is
        /// left in <see cref="WKTSyncedRope.Rebuilt"/>; returns its end's velocity.
        /// </summary>
        private Vector3 WriteCurrent(NetDataWriter writer, WKTSyncedRope entry) {
            Vector3 velocity;
            if (m_sync.IsHost && entry.Owner != HOST && entry.HasLatest) {
                WKTRopeCodec.Encode(entry.Latest, entry.MaxLength, entry.Links, entry.Rebuilt);
                velocity = entry.LatestVelocity;
            }
            else if (!entry.IsPuppet && entry.Rope != null)
                velocity = Capture(entry);
            else {
                WKTRopeCodec.Encode(entry.Shown, entry.MaxLength, entry.Links, entry.Rebuilt);
                velocity = entry.ShownVelocity;
            }
            WKTRopeState.WriteEntry(writer, entry.Key, velocity, entry.Links);
            return velocity;
        }

        #endregion

        #region Receiving

        public void OnRopeStates(WKTPlayer sender, NetDataReader reader) {
            if (!reader.TryGetUShort(out var sequence) || !reader.TryGetByte(out var count)) return;
            var isHost = m_sync.IsHost;
            if (isHost == sender.IsHost) return;
            var now = Time.realtimeSinceStartupAsDouble;
            NetDataWriter? relay = null;
            var countAt = 0;
            var relayed = 0;
            for (var i = 0; i < count; i++) {
                if (!WKTRopeState.TryReadEntry(reader, out var key, out var velocity, m_links, out var linkCount)) break;
                m_byKey.TryGetValue(key, out var entry);
                if (isHost) {
                    // Only the owner's states count, and are handed on to everyone else.
                    if (!m_table.Heard(key, sender.Id, now)) continue;
                    relay ??= BeginStates(sequence, out countAt);
                    WKTRopeState.WriteEntry(relay, key, velocity, m_links.AsSpan(0, linkCount));
                    relayed++;
                }
                if (entry == null || entry.Rope == null || linkCount != entry.NodeCount - 1 || Owns(entry)) continue;
                var nodes = m_nodes.AsSpan(0, entry.NodeCount);
                WKTRopeCodec.Decode(m_links.AsSpan(0, linkCount), entry.MaxLength, nodes);
                entry.Buffer.Add(sequence, nodes, velocity, now);
                if (isHost) {
                    nodes.CopyTo(entry.Latest);
                    entry.LatestVelocity = velocity;
                    entry.HasLatest = true;
                }
                if (entry.HasShown) continue;
                nodes.CopyTo(entry.Shown);
                entry.ShownVelocity = velocity;
                entry.HasShown = true;
                UpdatePuppet(entry);
            }
            if (relay != null && relayed > 0)
                FlushStates(relay, countAt, relayed, sender);
        }

        /// <summary>Host only: a client took hold of a rope or let go of it.</summary>
        public void OnRopeHold(WKTPlayer sender, NetDataReader reader) {
            if (!m_sync.IsHost || sender.IsHost || !reader.TryGetByte(out var hold) ||
                !WKTRopeState.TryReadEntry(reader, out var key, out var velocity, m_links, out var linkCount) || !m_byKey.TryGetValue(key, out var entry)) return;
            var now = Time.realtimeSinceStartupAsDouble;
            m_table.HostId = LocalId;
            if (hold == WKTRopeState.HOLD_GRABBED) {
                entry.LastHoldChange = now;
                if (m_table.Grab(key, sender.Id, now, out var owner))
                    ApplyChange(entry, owner, now);
                else
                    SendCurrent(sender, entry);
                return;
            }
            ClearRemoteSwing(entry, sender.Id);
            var wasOwner = m_table.OwnerOf(key) == sender.Id;
            if (!m_table.Release(key, sender.Id, now, out var newOwner)) return;
            entry.LastHoldChange = now;
            if (wasOwner && linkCount == entry.NodeCount - 1) {
                // The swing carries on from where the holder let go.
                WKTRopeCodec.Decode(m_links.AsSpan(0, linkCount), entry.MaxLength, entry.Latest);
                entry.LatestVelocity = velocity;
                entry.HasLatest = true;
            }
            ApplyChange(entry, newOwner, now);
        }

        /// <summary>Client only: who simulates a rope now, and the state to carry on from.</summary>
        public void OnRopeAuthority(WKTPlayer sender, NetDataReader reader) {
            if (m_sync.IsHost || !sender.IsHost || !reader.TryGetByte(out var owner) ||
                !WKTRopeState.TryReadEntry(reader, out var key, out var velocity, m_links, out var linkCount)) return;
            if (!m_byKey.TryGetValue(key, out var entry) || entry.Rope == null) {
                if (owner == HOST)
                    m_pendingOwners.Remove(key);
                else
                    m_pendingOwners[key] = owner;
                return;
            }
            if (linkCount != entry.NodeCount - 1) return;
            var nodes = m_nodes.AsSpan(0, entry.NodeCount);
            WKTRopeCodec.Decode(m_links.AsSpan(0, linkCount), entry.MaxLength, nodes);
            Apply(entry, owner, nodes, velocity, Time.realtimeSinceStartupAsDouble);
        }

        /// <summary>Host only: a push from a remote player holding a rope the host simulates.</summary>
        public void OnRopeForce(WKTPlayer sender, NetDataReader reader) {
            if (!m_sync.IsHost || sender.IsHost || !WKTRopeForce.TryRead(reader, out var force) || !m_byKey.TryGetValue(force.Key, out var entry) ||
                entry.Rope == null || !m_table.IsHeldBy(force.Key, sender.Id)) return;
            if (force.IsImpulse) {
                if (!entry.IsPuppet)
                    Push(entry, force.Force);
                return;
            }
            var slots = entry.RemoteSwings;
            var free = -1;
            for (var i = 0; i < slots.Length; i++) {
                if (slots[i].Player == sender.Id && slots[i].Time > 0) {
                    free = i;
                    break;
                }
                if (free < 0 && slots[i].Time <= 0)
                    free = i;
            }
            if (free < 0) return;
            slots[free] = new WKTSyncedRope.RemoteSwing { Player = sender.Id, Force = force.Force, Time = Time.realtimeSinceStartupAsDouble };
        }

        #endregion

        #region Holds

        /// <summary>The local player takes hold of a link of a rope. A client takes the rope over at once unless another player has it.</summary>
        public void OnLocalGrab(CL_Handhold_Rope handhold) {
            var rope = s_handholdRope(handhold);
            if (rope == null || !m_byRope.TryGetValue(rope, out var entry)) return;
            entry.ReleaseAt = double.PositiveInfinity;
            if (entry.LocalHolding) return;
            var now = Time.realtimeSinceStartupAsDouble;
            entry.LocalHolding = true;
            entry.LastHoldChange = now;
            if (entry.Key != 0)
                AnnounceGrab(entry, now);
        }

        private void AnnounceGrab(WKTSyncedRope entry, double now) {
            if (m_sync.IsHost) {
                m_table.HostId = LocalId;
                if (m_table.Grab(entry.Key, LocalId, now, out var owner))
                    ApplyChange(entry, owner, now);
                return;
            }
            // Optimistic: the host may still keep it, if others hold it too.
            if (entry.Owner == HOST || entry.Owner == LocalId) {
                entry.Owner = LocalId;
                entry.HandbackUntil = 0;
                entry.Buffer.Clear();
                UpdatePuppet(entry);
            }
            SendHold(entry, WKTRopeState.HOLD_GRABBED);
        }

        /// <summary>The local player let go of a link of a rope. It counts once they hold none of its links for a moment.</summary>
        public void OnLocalRelease(CL_Handhold_Rope handhold) {
            var rope = s_handholdRope(handhold);
            if (rope == null || !m_byRope.TryGetValue(rope, out var entry) || !entry.LocalHolding || IsHeldHere(rope)) return;
            entry.ReleaseAt = Time.realtimeSinceStartupAsDouble + RELEASE_GRACE;
        }

        private void FinishRelease(WKTSyncedRope entry, double now) {
            entry.ReleaseAt = double.PositiveInfinity;
            if (entry.Rope == null || IsHeldHere(entry.Rope)) return;
            entry.LocalHolding = false;
            entry.LastHoldChange = now;
            if (entry.Key == 0) return;
            if (m_sync.IsHost) {
                if (m_table.Release(entry.Key, LocalId, now, out var owner))
                    ApplyChange(entry, owner, now);
                return;
            }
            SendHold(entry, 0);
        }

        private void SendHold(WKTSyncedRope entry, byte hold) {
            var writer = m_network.BeginMessage(WKTMessageId.RopeHold);
            writer.Put(hold);
            WriteCurrent(writer, entry);
            m_network.SendToHost(writer, SendType.Reliable);
        }

        /// <summary>Whether the local player has a hand on any link of <paramref name="rope"/>.</summary>
        private static bool IsHeldHere(ENV_Rope rope) {
            foreach (var node in rope.nodes) {
                if (node.mesh != null && node.mesh.TryGetComponent<CL_Handhold_Rope>(out var link) && link.GetHolding()) return true;
            }
            return false;
        }

        #endregion

        #region Authority

        private void ApplyChanges(double now) {
            foreach (var change in m_changes) {
                if (m_byKey.TryGetValue(change.Handle, out var entry))
                    ApplyChange(entry, change.Owner, now);
                else
                    m_table.Forget(change.Handle);
            }
            m_changes.Clear();
        }

        /// <summary>Host only: a rope's owner changed. Hands it over here and tells everyone, with its newest state.</summary>
        private void ApplyChange(WKTSyncedRope entry, byte owner, double now) {
            var writer = m_network.BeginMessage(WKTMessageId.RopeAuthority);
            writer.Put(owner);
            var velocity = WriteCurrent(writer, entry);
            Apply(entry, owner, entry.Rebuilt, velocity, now);
            if (owner == HOST)
                entry.HasLatest = false;
            m_network.Broadcast(writer, SendType.Reliable);
        }

        /// <summary>Host only: tells one client who has a rope and how it hangs, so a refused grab is undone.</summary>
        private void SendCurrent(WKTPlayer player, WKTSyncedRope entry) {
            var writer = m_network.BeginMessage(WKTMessageId.RopeAuthority);
            writer.Put(m_table.OwnerOf(entry.Key));
            WriteCurrent(writer, entry);
            m_network.SendToPlayer(player, writer, SendType.Reliable);
        }

        /// <summary>Host only: tells a player who (re)connected who has every rope and how it hangs.</summary>
        public void SendAll(WKTPlayer player) {
            foreach (var entry in m_byKey.Values) {
                if (entry.Rope != null)
                    SendCurrent(player, entry);
            }
        }

        /// <summary>
        /// Makes <paramref name="owner"/> simulate the rope, carrying on from <paramref name="shape"/>. A client that simulated
        /// it keeps doing so for <see cref="HANDBACK"/> while the new owner's states arrive.
        /// </summary>
        private void Apply(WKTSyncedRope entry, byte owner, ReadOnlySpan<Vector3> shape, Vector3 velocity, double now) {
            var wasOwner = Owns(entry);
            entry.Owner = owner;
            entry.Buffer.Clear();
            shape.CopyTo(entry.Shown);
            entry.ShownVelocity = velocity;
            entry.HasShown = true;
            if (Owns(entry))
                entry.HandbackUntil = 0;
            else if (wasOwner && !m_sync.IsHost && !entry.IsPuppet)
                entry.HandbackUntil = now + HANDBACK;
            UpdatePuppet(entry);
        }

        /// <summary>Host only: a player died or left, so they hold nothing any more.</summary>
        public void OnPlayerGone(byte playerId) {
            if (!m_sync.IsHost) return;
            var now = Time.realtimeSinceStartupAsDouble;
            foreach (var entry in m_byKey.Values)
                ClearRemoteSwing(entry, playerId);
            m_changes.Clear();
            m_table.OnPlayerGone(playerId, now, m_changes);
            ApplyChanges(now);
        }

        /// <summary>The host changed: the new host simulates every rope again, and holders tell it what they hold.</summary>
        public void OnHostChanged() {
            m_table.Clear();
            m_table.HostId = LocalId;
            m_pendingOwners.Clear();
            var now = Time.realtimeSinceStartupAsDouble;
            foreach (var entry in m_byKey.Values) {
                if (!entry.IsPuppet && entry.Rope != null && entry.Rope.isActiveAndEnabled) {
                    // What it shows here is where it is: a client that simulated it holds it there until the new host's states arrive.
                    entry.ShownVelocity = Capture(entry);
                    entry.Rebuilt.CopyTo(entry.Shown, 0);
                    entry.HasShown = true;
                }
                entry.Owner = HOST;
                entry.HandbackUntil = 0;
                entry.HasLatest = false;
                entry.Buffer.Clear();
                for (var i = 0; i < entry.RemoteSwings.Length; i++)
                    entry.RemoteSwings[i] = default;
                UpdatePuppet(entry);
            }
            foreach (var entry in m_byKey.Values) {
                if (entry.LocalHolding && entry.Rope != null)
                    AnnounceGrab(entry, now);
            }
        }

        private bool Owns(WKTSyncedRope entry) => entry.Owner == HOST ? m_sync.IsHost : entry.Owner == LocalId;

        private bool IsEligible(byte playerId) => m_network.TryGetPlayer(playerId, out var player) && player != null && !WKTPlayerDeaths.IsDead(playerId);

        #endregion

        #region Puppets

        /// <summary>A rope is a puppet when another machine simulates it, this one has a shape for it and no handback runs.</summary>
        private void UpdatePuppet(WKTSyncedRope entry) {
            if (entry.Rope == null) return;
            var puppet = entry.Key != 0 && entry.HasShown && entry.HandbackUntil <= 0 && !Owns(entry);
            if (puppet == entry.IsPuppet) return;
            if (puppet)
                MakePuppet(entry);
            else
                ReleasePuppet(entry);
        }

        private static void MakePuppet(WKTSyncedRope entry) {
            entry.IsPuppet = true;
            var target = entry.Target;
            if (target == null) return;
            entry.TargetWasKinematic = target.isKinematic;
            target.isKinematic = true;
        }

        /// <summary>Simulates the rope here again, from the shape it showed and the end's shown velocity.</summary>
        private static void ReleasePuppet(WKTSyncedRope entry) {
            entry.IsPuppet = false;
            Show(entry);
            foreach (var node in entry.Rope.nodes)
                node.velocity = Vector3.zero;
            var target = entry.Target;
            if (target == null) return;
            target.isKinematic = entry.TargetWasKinematic;
            if (target.isKinematic) return;
            target.linearVelocity = entry.ShownVelocity;
            target.angularVelocity = Vector3.zero;
        }

        /// <summary>Puts the rope's nodes, end and links where <see cref="WKTSyncedRope.Shown"/> says, as the game draws them.</summary>
        private static void Show(WKTSyncedRope entry) {
            var rope = entry.Rope;
            var ropeTransform = rope.transform;
            var anchor = ropeTransform.position;
            var rotation = ropeTransform.rotation;
            var nodes = rope.nodes;
            for (var i = 0; i < entry.NodeCount; i++) {
                var position = anchor + rotation * entry.Shown[i];
                var node = nodes[i];
                node.position = position;
                node.previousPosition = position;
            }
            if (rope.target != null)
                rope.target.position = nodes[entry.NodeCount - 1].position;
            s_renderRope(rope);
            var speed = entry.ShownVelocity.magnitude;
            UpdateSwingAudio(rope, rope.swingAudio01, speed, true);
            UpdateSwingAudio(rope, rope.swingAudio02, speed, false);
        }

        /// <summary>The game's swing sound, from the end's shown speed.</summary>
        private static void UpdateSwingAudio(ENV_Rope rope, AudioSource? source, float speed, bool pitched) {
            if (source == null) return;
            if (speed < c_silent_speed) {
                if (source.isPlaying)
                    source.Pause();
            }
            else if (!source.isPlaying)
                source.Play();
            var level = speed * rope.swingAudioVelocityMagnitude;
            source.volume = Mathf.Clamp01(level) * rope.swingAudioVolume;
            if (pitched)
                source.pitch = Mathf.Clamp(level, rope.swingAudioPitchMin, rope.swingAudioPitchMax);
        }

        /// <summary>Host only: each fresh swing of a remote player holding the rope, once per physics step as the game pushes.</summary>
        private static void ApplyRemoteSwings(WKTSyncedRope entry, double now) {
            var slots = entry.RemoteSwings;
            for (var i = 0; i < slots.Length; i++) {
                if (slots[i].Time <= 0) continue;
                if (now - slots[i].Time >= SWING_STALE) {
                    slots[i] = default;
                    continue;
                }
                Push(entry, slots[i].Force);
            }
        }

        private static void ClearRemoteSwing(WKTSyncedRope entry, byte playerId) {
            var slots = entry.RemoteSwings;
            for (var i = 0; i < slots.Length; i++) {
                if (slots[i].Player == playerId)
                    slots[i] = default;
            }
        }

        private static void Push(WKTSyncedRope entry, Vector3 push) {
            s_applyingPush = true;
            try {
                entry.Rope.AddVelocity(push);
            }
            finally {
                s_applyingPush = false;
            }
        }

        #endregion
    }
}
