using System.Collections.Generic;
using HarmonyLib;
using Steamworks.Data;
using UnityEngine;
using WhiteKnuckleTogether.Networking.LiteNetLib;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// Objects several players hold at once, which the host simulates (see <see cref="WKTAuthoritySync"/>). Remote players'
    /// hands are never put into the game's own hold lists, whose code assumes every hand is the local player's. Instead:
    /// <list type="bullet">
    /// <item>The host pulls a shared prop toward each remote holder's hold point the way the game pulls a prop toward the
    /// local player's, and adds that to its own player's pull: holders pulling together move it faster, against each other
    /// they cancel out. A prop held by others never lifts a holder, so nobody rides a prop someone else carries.</item>
    /// <item>A flier several players ride is steered toward the average of where each rider steers it, from each rider's
    /// camera and move input (<see cref="WKTRiderInput"/>), the way the game steers it for one rider.</item>
    /// <item>Every rider of a bucking flier is bucked on their own timer. The machine simulating a flier keeps its own
    /// player's in the game's module; the host keeps every remote rider's (<see cref="WKTRiderTimers"/>) and bucks them with
    /// a player effect. A timer goes with its rider whenever the flier changes hands (<see cref="WKTRiderBuck"/>), and every
    /// buck is shown and heard everywhere.</item>
    /// </list>
    /// Owned by <see cref="WKTWorldSync"/>.
    /// </summary>
    internal sealed class WKTSharedHolds {
        private const SendType c_input_send_type = SendType.Unreliable | SendType.NoNagle;
        private static readonly AccessTools.FieldRef<HandholdModule, CL_Handhold> s_moduleHandhold = AccessTools.FieldRefAccess<HandholdModule, CL_Handhold>("handhold");
        private static readonly AccessTools.FieldRef<CL_Prop, List<ENT_Player.Hand>> s_handsHolding = AccessTools.FieldRefAccess<CL_Prop, List<ENT_Player.Hand>>("handsHolding");
        /// <summary>How long the host waits for a client's timer when it takes a flier over, before starting one of its own.</summary>
        private const double c_timer_handover = 1.0;
        private const string c_buck_sound = "denizen:buck";
        private static readonly AccessTools.FieldRef<HandholdModule_FlierSteering, float> s_buckTime = AccessTools.FieldRefAccess<HandholdModule_FlierSteering, float>("buckTime");
        private static readonly AccessTools.FieldRef<HandholdModule_FlierSteering, int> s_buckHits =
            AccessTools.FieldRefAccess<HandholdModule_FlierSteering, int>("currentBuckDropHits");
        private static readonly int s_hanging = Animator.StringToHash("Hanging");
        private static readonly int s_buck = Animator.StringToHash("Buck");
        private static readonly List<CL_Handhold> s_handholdScratch = new();
        private readonly WKTWorldSync m_sync;
        private readonly WKTNetwork m_network;
        /// <summary>Host only: the remote players holding each object, by handle.</summary>
        private readonly Dictionary<ushort, Holders> m_holds = new();
        private readonly Stack<Holders> m_pool = new();
        /// <summary>Host only: every player's bucking timer for every flier.</summary>
        private readonly WKTRiderTimers m_timers = new();
        private readonly List<ushort> m_handleScratch = new();
        /// <summary>Host only: held creatures whose grab the host's copy was given for remote holders (see <see cref="UpdateHeldCreatures"/>).</summary>
        private readonly HashSet<ushort> m_heldHere = new();
        private readonly List<ushort> m_heldScratch = new();
        private readonly WKTSendClock m_inputClock = new();
        /// <summary>Client only: the flier the local player rides while another machine simulates it.</summary>
        private HandholdModule_FlierSteering? m_ridden;
        private ushort m_riddenHandle;
        private byte m_jumps;

        public WKTSharedHolds(WKTWorldSync sync, WKTNetwork network) {
            m_sync = sync;
            m_network = network;
        }

        /// <summary>Sets a body's velocity unless it is kinematic, as a puppet held here is: its motion comes from elsewhere.</summary>
        public static void SetVelocity(Rigidbody body, Vector3 velocity) {
            if (!body.isKinematic)
                body.linearVelocity = velocity;
        }

        #region Holders

        /// <summary>Host only: a remote player grabbed an object, at <paramref name="anchor"/> in its own space.</summary>
        public void OnGrab(ushort handle, byte playerId, Vector3 anchor) {
            if (!m_holds.TryGetValue(handle, out var holders)) {
                holders = m_pool.Count > 0 ? m_pool.Pop() : new Holders();
                m_holds.Add(handle, holders);
            }
            holders.Set(playerId, anchor);
        }

        /// <summary>Host only: a remote player let go of an object. Their bucking timer starts recharging.</summary>
        public void OnRelease(ushort handle, byte playerId) {
            m_timers.OnDismount(handle, playerId, Time.realtimeSinceStartupAsDouble);
            if (!m_holds.TryGetValue(handle, out var holders) || !holders.Remove(playerId) || holders.Count > 0) return;
            ReturnHolders(handle);
        }

        /// <summary>Host only: a player died or left, so they hold nothing.</summary>
        public void OnPlayerGone(byte playerId) {
            m_timers.ForgetPlayer(playerId);
            m_handleScratch.Clear();
            foreach (var pair in m_holds) {
                if (pair.Value.Remove(playerId) && pair.Value.Count == 0)
                    m_handleScratch.Add(pair.Key);
            }
            foreach (var handle in m_handleScratch)
                ReturnHolders(handle);
        }

        /// <summary>The object is gone or dead.</summary>
        public void Forget(ushort handle) {
            if (m_riddenHandle == handle)
                StopRiding();
            m_timers.Forget(handle);
            ReturnHolders(handle);
        }

        public void Clear() {
            foreach (var holders in m_holds.Values) {
                holders.Clear();
                m_pool.Push(holders);
            }
            m_holds.Clear();
            m_heldHere.Clear();
            m_timers.Clear();
            StopRiding();
        }

        private void ReturnHolders(ushort handle) {
            if (!m_holds.Remove(handle, out var holders)) return;
            holders.Clear();
            m_pool.Push(holders);
        }

        #endregion

        #region Props

        /// <summary>Host: pulls the shared props it simulates toward their remote holders. Client: sends how it steers what it rides.</summary>
        public void Update(double now) {
            if (!m_sync.IsHost) {
                SendRiderInput(now);
                return;
            }
            UpdateHeldCreatures();
            if (m_holds.Count == 0) return;
            foreach (var pair in m_holds) {
                if (!m_sync.TryGetIdentity(pair.Key, out var identity) || identity!.IsPuppet || identity.Owner != WKTAuthoritySync.HOST ||
                    identity.Prop is not { isActiveAndEnabled: true } prop) continue;
                PullProp(prop, identity.Body, pair.Value);
            }
        }

        /// <summary>
        /// Host only. A creature players can pick up (a sprider, a bloodbug, a drone) changes what it does while held through
        /// its prop's grab and drop events: it is picked up, or switches to its held attack. Those run on the holder's own
        /// machine. While the host simulates one that only remote players hold, its copy gets the same grab, and the drop once
        /// they all let go or it moves to another machine, so it never roams or attacks in their hands.
        /// </summary>
        private void UpdateHeldCreatures() {
            foreach (var pair in m_holds) {
                if (pair.Value.Count == 0 || m_heldHere.Contains(pair.Key) || !TryGetHeldCreature(pair.Key, true, out var prop) || IsHeldHere(prop!)) continue;
                m_heldHere.Add(pair.Key);
                prop!.onGrabEvent?.Invoke();
            }
            if (m_heldHere.Count == 0) return;
            m_heldScratch.Clear();
            foreach (var handle in m_heldHere) {
                var simulated = TryGetHeldCreature(handle, true, out var prop);
                // The local player's own grab gives it the same state, which their own drop then ends.
                if (simulated && IsHeldHere(prop!)) {
                    m_heldScratch.Add(handle);
                    continue;
                }
                if (simulated && m_holds.TryGetValue(handle, out var holders) && holders.Count > 0) continue;
                if (simulated || TryGetHeldCreature(handle, false, out prop))
                    prop!.onDropEvent?.Invoke();
                m_heldScratch.Add(handle);
            }
            foreach (var handle in m_heldScratch)
                m_heldHere.Remove(handle);
            m_heldScratch.Clear();
        }

        /// <summary>The prop half of a creature players can pick up, simulated by the host when <paramref name="simulatedHere"/>.</summary>
        private bool TryGetHeldCreature(ushort handle, bool simulatedHere, out CL_Prop? prop) {
            prop = null;
            if (!m_sync.TryGetIdentity(handle, out var identity) || identity!.Prop == null || identity.Prop == identity.Entity || !identity.Prop.isActiveAndEnabled) return false;
            if (simulatedHere && (identity.IsPuppet || identity.Owner != WKTAuthoritySync.HOST)) return false;
            prop = identity.Prop;
            return true;
        }

        private static bool IsHeldHere(CL_Prop prop) => s_handsHolding(prop) is { Count: > 0 };

        /// <summary>
        /// Gives a shared prop every holder's pull added together, the host's own player's included, and each remote holder's
        /// force at their anchor. Its weight counts once, as for one holder.
        /// </summary>
        private static void PullProp(CL_Prop prop, Rigidbody? body, Holders holders) {
            if (body == null || body.isKinematic) return;
            var propTransform = prop.transform;
            var mass = Mathf.Max(body.mass, 0.01f);
            var deltaTime = Time.deltaTime;
            var hostHolding = s_handsHolding(prop).Count > 0;
            // The game already moved it by the host player's pull, its weight included.
            var pulls = hostHolding ? body.linearVelocity - Vector3.down * mass : Vector3.zero;
            var any = hostHolding;
            for (var i = 0; i < holders.Count; i++) {
                if (!WKTPlayerProxy.TryGet(holders.PlayerAt(i), out var proxy) || proxy!.Remote == null || proxy.dead) continue;
                var head = proxy.Remote.Head;
                var anchorWorld = propTransform.TransformPoint(holders.AnchorAt(i));
                WKTHoldMath.PropVelocity(head.position, head.forward, prop.holdDistance, head.TransformVector(prop.holdOffset), anchorWorld,
                    propTransform.position, mass, deltaTime, out var force, out var pull);
                if (prop.stuck) {
                    prop.AddBreakingVelocity(pull / mass);
                    continue;
                }
                pulls += WKTHoldMath.PullVelocity(pull, mass);
                any = true;
                body.AddForceAtPosition(force, anchorWorld);
            }
            if (any && !prop.stuck)
                body.linearVelocity = WKTHoldMath.SharedVelocity(pulls, mass);
        }

        /// <summary>
        /// Whether a prop the local player holds is held by others too: then it hangs from them, and cannot lift the local
        /// player (see <see cref="WKTHoldMath.HangForce"/>). A client shows such a prop as a puppet, which the host simulates
        /// for all its holders; the host knows its remote holders.
        /// </summary>
        public bool IsHeldByOthers(WKTNetIdentity identity) =>
            identity.Handle != 0 && (m_sync.IsHost ? m_holds.TryGetValue(identity.Handle, out var holders) && holders.Count > 0 : identity.IsPuppet);

        #endregion

        #region Riders

        /// <summary>Host only: a rider's steering for a flier several players ride.</summary>
        public void OnRiderInput(WKTPlayer sender, NetDataReader reader) {
            if (!m_sync.IsHost || sender.IsHost || !WKTRiderInput.TryRead(reader, out var input)) return;
            if (m_holds.TryGetValue(input.Handle, out var holders))
                holders.SetInput(sender.Id, input);
        }

        /// <summary>
        /// After a flier's steering ran here: on the host, a flier remote players ride is steered toward where all of its
        /// riders steer it, the host's own player too if it rides, and bucks each remote rider on their own timer. Once a frame
        /// per flier, however many handholds steer it.
        /// </summary>
        public void AfterFlierSteering(HandholdModule_FlierSteering module) {
            var flier = module.basicFlier;
            if (!m_sync.IsHost || flier == null || flier.IsDead() || WKTWorldSync.IsPuppet(flier) || !flier.TryGetComponent<WKTNetIdentity>(out var identity) ||
                !m_holds.TryGetValue(identity.Handle, out var holders) || holders.LastFrame == Time.frameCount) return;
            holders.LastFrame = Time.frameCount;
            var settings = new WKTHoldMath.SteerSettings(module.steerHorizontalForce, module.steerDownwardsForce, module.steerUpwardsForce, module.gravityForce,
                module.placementDistance, module.placementHeight, module.forceMult);
            var offset = WKTWorldOffset.Get();
            var target = Vector3.zero;
            var cameras = Vector3.zero;
            var riders = 0;
            var handhold = s_moduleHandhold(module);
            var local = ENT_Player.playerObject;
            if (handhold != null && handhold.GetHolding() && local != null && local.cam != null) {
                var cam = local.cam.transform;
                var move = InputManager.GetVector("Move").vector;
                target += WKTHoldMath.SteerTarget(settings, cam.position, cam.forward, cam.right, new Vector2(move.x, move.y));
                cameras += cam.position;
                riders++;
            }
            for (var i = 0; i < holders.Count; i++) {
                if (!holders.TryGetInput(i, out var input)) continue;
                var cameraPosition = WKTWorldOffset.FromWire(input.CameraPosition, offset);
                target += WKTHoldMath.SteerTarget(settings, cameraPosition, input.CameraForward, WKTHoldMath.RightOf(input.CameraForward), input.Move);
                cameras += cameraPosition;
                riders++;
                Buck(module, identity.Handle, holders.PlayerAt(i));
                var jumps = (byte)(input.Jumps - holders.JumpsAt(i));
                if (jumps == 0) continue;
                holders.SetJumps(i, input.Jumps);
                flier.AddForce(Vector3.up * (module.jumpBoost * jumps));
            }
            if (riders == 0) return;
            target /= riders;
            cameras /= riders;
            var flierTransform = flier.transform;
            flier.SetTargetPosition(target);
            flier.movementModule?.SetLookVector(cameras - flierTransform.position);
            var facing = Vector3.ProjectOnPlane(cameras - flierTransform.position, Vector3.up);
            if (facing.sqrMagnitude > 1e-6f)
                flierTransform.rotation = Quaternion.LookRotation(facing);
            if (module.animator != null)
                module.animator.SetBool(s_hanging, true);
            flier.SetHome();
        }

        /// <summary>Advances one remote rider's bucking timer, and bucks them on their own machine when it runs out.</summary>
        private void Buck(HandholdModule_FlierSteering module, ushort handle, byte playerId) {
            if (!module.canBuck || WKTPlayerDeaths.IsDead(playerId)) return;
            if (!m_timers.Tick(handle, playerId, Time.deltaTime, Time.realtimeSinceStartupAsDouble, SettingsOf(module), out var throwOff)) return;
            WKTTargets.SendEffect(playerId,
                new WKTPlayerEffect(WKTPlayerEffectId.Buck, module.buckDamage, vector: new Vector3(module.buckStaminaDamage, 0f, 0f), flag: throwOff,
                    text: module.attackDamageTag));
            ShowBuck(module);
            SendBucked(null, handle);
        }

        private static WKTBuckSettings SettingsOf(HandholdModule_FlierSteering module) =>
            new(module.buckWait, module.buckRate, module.maxBuckDropHits, module.buckRechargeSpeed);

        #endregion

        #region Bucking

        /// <summary>
        /// A puppet flier's steering is skipped here, but its timer for the local player still recharges while they are off
        /// it, as the game's does, so it is right if this machine simulates the flier next.
        /// </summary>
        public static void RechargePuppet(HandholdModule_FlierSteering module, bool localHolding) {
            if (!module.canBuck || localHolding) return;
            ref var time = ref s_buckTime(module);
            if (time < module.buckWait)
                time += Time.deltaTime * module.buckRechargeSpeed;
            s_buckHits(module) = 0;
        }

        /// <summary>How many times the game's own timer bucked the local player, to tell when it bucks again.</summary>
        public static int GetLocalHits(HandholdModule_FlierSteering module) => s_buckHits(module);

        /// <summary>The game's own steering bucked the local player off a flier simulated here: everyone else sees and hears it.</summary>
        public void OnLocalGameBuck(HandholdModule_FlierSteering module) {
            var flier = module.basicFlier;
            if (flier == null || !flier.TryGetComponent<WKTNetIdentity>(out var identity) || identity.Handle == 0) return;
            SendBucked(null, identity.Handle);
        }

        /// <summary>
        /// Host only: a flier changed hands. The host waits for the timer of a client it took the flier from, and hands a client
        /// that now simulates a flier it rides its timer kept here.
        /// </summary>
        public void OnOwnerChanged(WKTNetIdentity identity, byte previous, byte owner) {
            if (!m_sync.IsHost || identity.Entity == null || FindSteering(identity.Entity) is not { } module) return;
            var now = Time.realtimeSinceStartupAsDouble;
            if (owner == WKTAuthoritySync.HOST) {
                if (previous != WKTAuthoritySync.HOST)
                    m_timers.Await(identity.Handle, previous, now + c_timer_handover);
                return;
            }
            if (!m_timers.TryGet(identity.Handle, owner, now, SettingsOf(module), out var timer) || !m_network.TryGetPlayer(owner, out var player) || player == null)
                return;
            var writer = m_network.BeginMessage(WKTMessageId.RiderBuck);
            new WKTRiderBuck(identity.Handle, timer.Time, (byte)Mathf.Clamp(timer.Hits, 0, byte.MaxValue)).Write(writer);
            m_network.SendToPlayer(player, writer, SendType.Reliable);
        }

        /// <summary>Client only: this client stopped simulating a flier, so its player's timer goes to the host, which simulates it next.</summary>
        public void OnStoppedSimulating(WKTNetIdentity identity) {
            if (m_sync.IsHost || identity.Handle == 0 || identity.Entity == null || FindSteering(identity.Entity) is not { } module) return;
            var writer = m_network.BeginMessage(WKTMessageId.RiderBuck);
            new WKTRiderBuck(identity.Handle, Mathf.Clamp(s_buckTime(module), -WKTRiderBuck.MAX_TIME, WKTRiderBuck.MAX_TIME),
                (byte)Mathf.Clamp(s_buckHits(module), 0, byte.MaxValue)).Write(writer);
            m_network.SendToHost(writer, SendType.Reliable);
        }

        /// <summary>A rider's timer, handed over: on the host from a client, on a client from the host as it takes the flier over.</summary>
        public void OnRiderBuck(WKTPlayer sender, NetDataReader reader) {
            if (m_sync.IsHost == sender.IsHost || !WKTRiderBuck.TryRead(reader, out var buck) || !m_sync.TryGetIdentity(buck.Handle, out var identity) ||
                identity!.Entity == null || FindSteering(identity.Entity) is not { } module) return;
            if (m_sync.IsHost) {
                var riding = m_holds.TryGetValue(buck.Handle, out var holders) && holders.Contains(sender.Id);
                m_timers.Set(buck.Handle, sender.Id, new WKTBuckTimer(buck.Time, buck.Hits), riding, Time.realtimeSinceStartupAsDouble);
                return;
            }
            s_buckTime(module) = buck.Time;
            s_buckHits(module) = buck.Hits;
        }

        /// <summary>A flier another machine simulates bucked a rider: shown and heard here. The host passes a client's on to everyone else.</summary>
        public void OnFlierBucked(WKTPlayer sender, NetDataReader reader) {
            if (m_sync.IsHost == sender.IsHost || !WKTEntityEvent.TryReadHandle(reader, out var bucked) || !m_sync.TryGetIdentity(bucked.Handle, out var identity) ||
                identity!.Entity == null || FindSteering(identity.Entity) is not { } module) return;
            if (m_sync.IsHost) {
                if (identity.Owner != sender.Id) return;
                SendBucked(sender, bucked.Handle);
            }
            ShowBuck(module);
        }

        /// <summary>Tells everyone a flier bucked, except <paramref name="except"/>. A client's goes through the host.</summary>
        private void SendBucked(WKTPlayer? except, ushort handle) {
            var writer = m_network.BeginMessage(WKTMessageId.FlierBucked);
            WKTEntityEvent.Despawn(handle).WriteHandle(writer);
            if (m_sync.IsHost)
                m_network.Broadcast(writer, SendType.Reliable, except);
            else
                m_network.SendToHost(writer, SendType.Reliable);
        }

        /// <summary>The flier's buck as the game shows it: its buck animation and sound.</summary>
        private static void ShowBuck(HandholdModule_FlierSteering module) {
            if (module.animator != null)
                module.animator.SetTrigger(s_buck);
            WKTEntitySounds.PlayLocal(module.basicFlier.clipHandler, c_buck_sound);
        }

        /// <summary>The steering module of a flier's first handhold that steers it, or null.</summary>
        private static HandholdModule_FlierSteering? FindSteering(GameEntity flier) {
            flier.GetComponentsInChildren(true, s_handholdScratch);
            foreach (var handhold in s_handholdScratch) {
                if (handhold.modules == null) continue;
                foreach (var module in handhold.modules) {
                    if (module is HandholdModule_FlierSteering steering && steering.basicFlier == flier) return steering;
                }
            }
            return null;
        }

        #endregion

        #region Rider input

        /// <summary>
        /// The local player grabbed a handhold of a flier. If another machine simulates it, this client starts sending how it
        /// steers.
        /// </summary>
        public void OnLocalRideGrab(HandholdModule_FlierSteering module, ushort handle) {
            if (m_sync.IsHost) return;
            m_ridden = module;
            m_riddenHandle = handle;
        }

        /// <summary>The local player let go of every handhold of the flier it rode.</summary>
        public void OnLocalRideRelease(ushort handle) {
            if (m_riddenHandle == handle)
                StopRiding();
        }

        /// <summary>
        /// The local player jumped off a flier another machine simulates: counted for the host to boost it. A double jump counts
        /// as a full jump, slightly more than the game's own boost for it.
        /// </summary>
        public void OnPuppetJump() {
            if (m_ridden != null)
                m_jumps++;
        }

        private void StopRiding() {
            m_ridden = null;
            m_riddenHandle = 0;
        }

        /// <summary>Client only: while the flier it rides is simulated by the host, tells the host how the local player steers.</summary>
        private void SendRiderInput(double now) {
            if (m_ridden == null || m_riddenHandle == 0 || !m_inputClock.TryAdvance(now, out _)) return;
            var flier = m_ridden.basicFlier;
            var local = ENT_Player.playerObject;
            if (flier == null || !WKTWorldSync.IsPuppet(flier) || local == null || local.cam == null) return;
            var cam = local.cam.transform;
            var move = InputManager.GetVector("Move").vector;
            var clamped = new Vector2(Mathf.Clamp(move.x, -1f, 1f), Mathf.Clamp(move.y, -1f, 1f));
            var writer = m_network.BeginMessage(WKTMessageId.RiderInput);
            new WKTRiderInput(m_riddenHandle, clamped, WKTWorldOffset.ToWire(cam.position, WKTWorldOffset.Get()), cam.forward, m_jumps).Write(writer);
            m_network.SendToHost(writer, c_input_send_type);
        }

        #endregion

        /// <summary>The remote players holding one object: where each holds it, and the newest steering of each rider.</summary>
        private sealed class Holders {
            private readonly Holder[] m_holders = new Holder[WKTLobbyConsts.MAX_PLAYERS];

            public int Count { get; private set; }
            /// <summary>The frame a flier's riders were last handled, so several handholds steering it do so once.</summary>
            public int LastFrame { get; set; } = -1;

            public byte PlayerAt(int index) => m_holders[index].PlayerId;

            public bool Contains(byte playerId) => IndexOf(playerId) >= 0;

            public Vector3 AnchorAt(int index) => m_holders[index].Anchor;

            public byte JumpsAt(int index) => m_holders[index].Jumps;

            public void SetJumps(int index, byte jumps) => m_holders[index].Jumps = jumps;

            public bool TryGetInput(int index, out WKTRiderInput input) {
                input = m_holders[index].Input;
                return m_holders[index].HasInput;
            }

            public void Set(byte playerId, Vector3 anchor) {
                var index = IndexOf(playerId);
                if (index < 0) {
                    if (Count == m_holders.Length) return;
                    index = Count++;
                    m_holders[index] = new Holder { PlayerId = playerId };
                }
                m_holders[index].Anchor = anchor;
            }

            public void SetInput(byte playerId, in WKTRiderInput input) {
                var index = IndexOf(playerId);
                if (index < 0) return;
                if (!m_holders[index].HasInput)
                    // The first input only sets where the jump count starts.
                    m_holders[index].Jumps = input.Jumps;
                m_holders[index].Input = input;
                m_holders[index].HasInput = true;
            }

            public bool Remove(byte playerId) {
                var index = IndexOf(playerId);
                if (index < 0) return false;
                m_holders[index] = m_holders[--Count];
                m_holders[Count] = default;
                return true;
            }

            public void Clear() {
                for (var i = 0; i < Count; i++)
                    m_holders[i] = default;
                Count = 0;
                LastFrame = -1;
            }

            private int IndexOf(byte playerId) {
                for (var i = 0; i < Count; i++) {
                    if (m_holders[i].PlayerId == playerId) return i;
                }
                return -1;
            }
        }

        private struct Holder {
            public byte PlayerId;
            public Vector3 Anchor;
            public WKTRiderInput Input;
            public bool HasInput;
            public byte Jumps;
        }
    }
}
