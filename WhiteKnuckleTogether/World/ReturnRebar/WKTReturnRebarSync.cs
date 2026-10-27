using System;
using System.Collections.Generic;
using HarmonyLib;
using Steamworks.Data;
using UnityEngine;
using WhiteKnuckleTogether.Networking.LiteNetLib;
using Object = UnityEngine.Object;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// Thrown return rebars in a synced co-op run. The return rebar is an artifact: one player's own item, which flies back to
    /// them. Everyone sees it fly and stick, and anyone can climb it while it is in a wall. Only its thrower can recall it,
    /// unless they are dead or gone: then anyone alive can, and the host lets the first claim win. If whoever it flies back to
    /// dies or leaves on the way, it flies on with no owner until it sticks again.
    /// <para>
    /// The thrower's machine keeps the game's own projectile; every other machine shows a copy from the same prefab. Only the
    /// copy on the machine deciding the rebar (<see cref="WKTReturnRebarRegistry.GetAuthority"/>) hits anything and reports
    /// where it sticks; the others are cosmetic. Every machine keeps the same registry, so the host can replay the stuck
    /// rebars to late joiners and a client that takes over as host already has them. Owned by <see cref="WKTWorldSync"/>.
    /// </para>
    /// </summary>
    internal sealed class WKTReturnRebarSync {
        /// <summary>How far around a stuck rebar the wall it sits in is looked for, so it follows moving walls like the original.</summary>
        private const float c_wall_search = 0.75f;
        /// <summary>How close a copy flying to another player gets before it is gone, as the game's own return.</summary>
        private const float c_arrive_distance = 1.5f;
        /// <summary>How long a copy flying to another player may take before it is removed anyway.</summary>
        private const float c_return_timeout = 8f;
        /// <summary>How long the local player waits for the host to answer a claim before the prompt shows again.</summary>
        private const double c_claim_timeout = 2.0;
        /// <summary>How fast a rebar whose wall went away, with nobody to fly back to, starts falling.</summary>
        private const float c_drop_speed = 8f;
        private static readonly AccessTools.FieldRef<Projectile, Vector3> s_velocity = AccessTools.FieldRefAccess<Projectile, Vector3>("velocity");
        private static readonly AccessTools.FieldRef<Projectile, Item?> s_sourceItem = AccessTools.FieldRefAccess<Projectile, Item?>("sourceItem");
        private static readonly AccessTools.FieldRef<Projectile_ReturnRebar, bool> s_hasHitSurface =
            AccessTools.FieldRefAccess<Projectile_ReturnRebar, bool>("hasHitSurface");
        private static readonly AccessTools.FieldRef<Projectile_ReturnRebar, UT_SoftParent?> s_softParent =
            AccessTools.FieldRefAccess<Projectile_ReturnRebar, UT_SoftParent?>("softParent");
        private static readonly AccessTools.FieldRef<bool> s_grabbedThisFrame =
            AccessTools.StaticFieldRefAccess<bool>(AccessTools.Field(typeof(Projectile_ReturnRebar), "hasBeenGrabbedThisFrame"));
        private static readonly Action<Projectile_ReturnRebar> s_startReturn =
            AccessTools.MethodDelegate<Action<Projectile_ReturnRebar>>(AccessTools.Method(typeof(Projectile_ReturnRebar), "StartReturn"));
        private static readonly RaycastHit[] s_wallHits = new RaycastHit[8];
        private readonly WKTWorldSync m_sync;
        private readonly WKTNetwork m_network;
        private readonly WKTReturnRebarRegistry m_registry = new();
        private readonly Dictionary<uint, WKTReturnRebarCopy> m_copies = new();
        /// <summary>Rebars stuck in levels this machine has not built yet, by level key.</summary>
        private readonly Dictionary<ulong, List<uint>> m_pending = new();
        private readonly WKTReturnRebarThrown m_thrown = new();
        private readonly List<uint> m_idScratch = new();
        private uint m_counter;

        /// <summary>Set while a recall from another machine starts the game's return here, so it is not sent back.</summary>
        public static bool IsApplying { get; private set; }

        public WKTReturnRebarSync(WKTWorldSync sync, WKTNetwork network) {
            m_sync = sync;
            m_network = network;
        }

        private byte LocalId => m_network.LocalPlayer?.Id ?? WKTReturnRebarRegistry.NOBODY;
        private byte HostId => m_network.HostPlayer?.Id ?? WKTReturnRebarRegistry.NOBODY;

        /// <summary>Host only: takes over the rebars of a machine that left mid-flight. Called every frame.</summary>
        public void Update() {
            if (!m_sync.IsHost || m_registry.Count == 0) return;
            var hostId = HostId;
            m_registry.GetIds(m_idScratch);
            foreach (var id in m_idScratch) {
                if (!m_registry.TryGet(id, out var entry)) continue;
                var authority = WKTReturnRebarRegistry.GetAuthority(entry, hostId);
                if (authority == hostId || m_network.TryGetPlayer(authority, out _)) continue;
                // A rebar in a wall needs nobody to decide it: anyone may claim it once its thrower is gone.
                if (entry.Recaller == WKTReturnRebarRegistry.NOBODY && entry.Thrown.IsStuck) continue;
                var offset = WKTWorldOffset.Get();
                var position = WKTWorldOffset.FromWire(entry.Thrown.Position, offset);
                var velocity = Vector3.zero;
                if (m_copies.TryGetValue(id, out var copy) && copy != null) {
                    position = copy.Rebar.transform.position;
                    velocity = s_velocity(copy.Rebar);
                }
                var wirePosition = WKTWorldOffset.ToWire(position, offset);
                if (!m_registry.Release(id, hostId, hostId, false, wirePosition, velocity)) continue;
                if (copy != null)
                    ApplyRelease(copy, position, velocity);
                Send(new WKTReturnRebarEvent(id, WKTReturnRebarEvent.RELEASED, hostId, 0, wirePosition, Quaternion.identity, velocity));
            }
        }

        #region Local

        /// <summary>The local player threw <paramref name="rebar"/> from <paramref name="handItem"/>: everyone gets a copy.</summary>
        public void OnLocalThrow(HandItem handItem, Projectile_ReturnRebar rebar) {
            var local = m_network.LocalPlayer;
            var item = handItem.item;
            if (local == null || item == null || string.IsNullOrEmpty(item.prefabName)) return;
            var rebarTransform = rebar.transform;
            m_thrown.Id = WKTPitonRegistry.MakeId(local.Id, ++m_counter);
            m_thrown.Owner = local.Id;
            m_thrown.ItemPrefab = item.prefabName;
            m_thrown.Projectile = WKTPrefabSearch.GetPrefabName(rebar.name);
            m_thrown.Position = WKTWorldOffset.ToWire(rebarTransform.position, WKTWorldOffset.Get());
            m_thrown.Rotation = rebarTransform.rotation;
            m_thrown.Velocity = Vector3.ClampMagnitude(s_velocity(rebar), WKTPlayerShot.MAX_SPEED);
            m_thrown.Flags = 0;
            m_thrown.LevelKey = 0;
            m_thrown.ItemData.Clear();
            if (item.data != null) {
                for (var i = 0; i < item.data.Count && i < WKTWire.MAX_ITEM_DATA; i++)
                    m_thrown.ItemData.Add(item.data[i] ?? string.Empty);
            }
            if (!m_registry.Add(m_thrown)) return;
            Track(m_thrown.Id, rebar);
            var writer = m_network.BeginMessage(WKTMessageId.ReturnRebarThrown);
            m_thrown.Write(writer);
            Send(writer);
        }

        /// <summary>A rebar just stuck in a wall here. If this machine decides it, everyone else's copy sticks in the same place.</summary>
        public void OnLocalStuck(Projectile_ReturnRebar rebar) {
            if (!rebar.TryGetComponent<WKTReturnRebarCopy>(out var copy) || !m_registry.TryGet(copy.Id, out var entry)) return;
            var local = LocalId;
            if (WKTReturnRebarRegistry.GetAuthority(entry, HostId) != local || !TryGetLevelKey(rebar, out var levelKey)) return;
            var rebarTransform = rebar.transform;
            var position = WKTWorldOffset.ToWire(rebarTransform.position, WKTWorldOffset.Get());
            if (!m_registry.Stick(copy.Id, local, HostId, levelKey, position, rebarTransform.rotation)) return;
            Send(new WKTReturnRebarEvent(copy.Id, WKTReturnRebarEvent.STUCK, local, levelKey, position, rebarTransform.rotation, Vector3.zero));
        }

        /// <summary>
        /// The game is about to start <paramref name="rebar"/>'s return here. Only a living thrower recalls their own rebar this
        /// way (<paramref name="report"/>); a player recalling someone else's claims it from the host instead. Everything else
        /// that would start a return here, such as the wall it sits in going away, is the deciding machine's to report.
        /// </summary>
        public bool OnStartReturn(Projectile_ReturnRebar rebar, out bool report) {
            report = false;
            if (IsApplying || !rebar.TryGetComponent<WKTReturnRebarCopy>(out var copy) || !m_registry.TryGet(copy.Id, out var entry)) return true;
            if (entry.Recaller != WKTReturnRebarRegistry.NOBODY) return false;
            var local = LocalId;
            // The wall it sat in went away with no living thrower to send it back to: the host drops it until it sticks again.
            if (m_sync.IsHost && IsWallLost(rebar) && (entry.Thrown.IsOwnerless || WKTPlayerDeaths.IsDead(entry.Thrown.Owner) ||
                                                       !m_network.TryGetPlayer(entry.Thrown.Owner, out _))) {
                Drop(copy);
                return false;
            }
            var localDead = WKTPlayerDeaths.IsDead(local);
            if (entry.Thrown.Owner == local && !entry.Thrown.IsOwnerless) {
                report = !localDead;
                return report;
            }
            // Only a recall the local player asked for, not one the game starts by distance.
            if (s_grabbedThisFrame() && IsClaimableHere(entry) && Time.realtimeSinceStartupAsDouble >= copy.ClaimPendingUntil)
                Claim(copy);
            return false;
        }

        /// <summary>The local player recalled their own rebar: it flies back to them for everyone.</summary>
        public void OnLocalRecall(Projectile_ReturnRebar rebar) {
            if (!rebar.TryGetComponent<WKTReturnRebarCopy>(out var copy)) return;
            var local = LocalId;
            if (!m_registry.OwnerRecall(copy.Id, local, false)) return;
            BeginReturn(copy, local);
            Send(WKTReturnRebarEvent.Of(copy.Id, WKTReturnRebarEvent.RECALL, local));
        }

        /// <summary>
        /// Whether the game's own <c>ReturnUpdate</c> runs for this copy: its recall prompt and its return to the local player.
        /// It does not when the rebar is not the local player's to recall, nor when it flies to someone else.
        /// </summary>
        public bool ShouldRunReturnUpdate(WKTReturnRebarCopy copy) {
            var rebar = copy.Rebar;
            var local = LocalId;
            var localDead = WKTPlayerDeaths.IsDead(local);
            if (rebar.returning) {
                if (copy.Recaller != local || local == WKTReturnRebarRegistry.NOBODY) {
                    FlyToRemote(copy);
                    return false;
                }
                if (!localDead) return true;
                ReleaseLocal(copy);
                return false;
            }
            if (!m_registry.TryGet(copy.Id, out var entry)) return true;
            var mayRecall = entry.Recaller == WKTReturnRebarRegistry.NOBODY && (entry.Thrown.Owner == local && !entry.Thrown.IsOwnerless
                ? !localDead
                : IsClaimableHere(entry) && Time.realtimeSinceStartupAsDouble >= copy.ClaimPendingUntil);
            if (mayRecall) return true;
            HidePrompt(rebar);
            return false;
        }

        private bool IsClaimableHere(WKTReturnRebarRegistry.Entry entry) {
            var owner = entry.Thrown.Owner;
            return WKTReturnRebarRegistry.IsClaimable(entry, WKTPlayerDeaths.IsDead(LocalId), WKTPlayerDeaths.IsDead(owner), m_network.TryGetPlayer(owner, out _));
        }

        /// <summary>The local player asks to recall a rebar whose thrower is dead or gone. The host's answer starts the return.</summary>
        private void Claim(WKTReturnRebarCopy copy) {
            copy.ClaimPendingUntil = Time.realtimeSinceStartupAsDouble + c_claim_timeout;
            var local = LocalId;
            if (m_sync.IsHost) {
                HandleClaim(local, copy.Id);
                return;
            }
            var writer = m_network.BeginMessage(WKTMessageId.ReturnRebarEvent);
            WKTReturnRebarEvent.Of(copy.Id, WKTReturnRebarEvent.CLAIM, local).Write(writer);
            m_network.SendToHost(writer, SendType.Reliable);
        }

        /// <summary>The local player died while the rebar flew back to them: it flies on with no owner.</summary>
        /// <summary>Whether the wall a rebar is soft-parented to was switched off, which is what starts the game's return then.</summary>
        private static bool IsWallLost(Projectile_ReturnRebar rebar) =>
            s_softParent(rebar) is { parent: { } wall } && !wall.gameObject.activeInHierarchy;

        /// <summary>Host only: lets a rebar fall from where it is, with no owner, until it sticks somewhere again.</summary>
        private void Drop(WKTReturnRebarCopy copy) {
            var hostId = HostId;
            var position = copy.Rebar.transform.position;
            var velocity = Vector3.down * c_drop_speed;
            var wirePosition = WKTWorldOffset.ToWire(position, WKTWorldOffset.Get());
            if (!m_registry.Release(copy.Id, hostId, hostId, false, wirePosition, velocity)) return;
            ApplyRelease(copy, position, velocity);
            Send(new WKTReturnRebarEvent(copy.Id, WKTReturnRebarEvent.RELEASED, hostId, 0, wirePosition, Quaternion.identity, velocity));
        }

        private void ReleaseLocal(WKTReturnRebarCopy copy) {
            var local = LocalId;
            var position = copy.Rebar.transform.position;
            var velocity = s_velocity(copy.Rebar);
            var wirePosition = WKTWorldOffset.ToWire(position, WKTWorldOffset.Get());
            if (!m_registry.Release(copy.Id, local, HostId, true, wirePosition, velocity)) return;
            ApplyRelease(copy, position, velocity);
            Send(new WKTReturnRebarEvent(copy.Id, WKTReturnRebarEvent.RELEASED, local, 0, wirePosition, Quaternion.identity, velocity));
        }

        private void Send(in WKTReturnRebarEvent rebarEvent) {
            var writer = m_network.BeginMessage(WKTMessageId.ReturnRebarEvent);
            rebarEvent.Write(writer);
            Send(writer);
        }

        private void Send(NetDataWriter writer) {
            if (m_sync.IsHost)
                m_network.Broadcast(writer, SendType.Reliable);
            else
                m_network.SendToHost(writer, SendType.Reliable);
        }

        #endregion

        #region Remote

        public void OnThrown(WKTPlayer sender, NetDataReader reader) {
            if (!WKTReturnRebarThrown.TryRead(reader, m_thrown)) return;
            if (m_sync.IsHost) {
                // A client only throws its own rebars.
                if (sender.IsHost || m_thrown.Owner != sender.Id || m_thrown.Flags != 0 || !m_registry.Add(m_thrown)) return;
                Build(m_thrown.Id);
                var writer = m_network.BeginMessage(WKTMessageId.ReturnRebarThrown);
                m_thrown.Write(writer);
                m_network.Broadcast(writer, SendType.Reliable, sender);
                return;
            }
            if (!sender.IsHost) return;
            // A replay after a reconnect can carry the rebars this machine already shows.
            if (m_registry.Add(m_thrown) || !m_copies.ContainsKey(m_thrown.Id))
                Build(m_thrown.Id);
        }

        public void OnEvent(WKTPlayer sender, NetDataReader reader) {
            if (!WKTReturnRebarEvent.TryRead(reader, out var rebarEvent)) return;
            if (m_sync.IsHost) {
                if (sender.IsHost) return;
                if (rebarEvent.Kind == WKTReturnRebarEvent.CLAIM) {
                    HandleClaim(sender.Id, rebarEvent.Id);
                    return;
                }
                // Whoever sent it did it.
                rebarEvent = rebarEvent.WithPlayer(sender.Id);
                if (!Apply(rebarEvent)) return;
                var writer = m_network.BeginMessage(WKTMessageId.ReturnRebarEvent);
                rebarEvent.Write(writer);
                m_network.Broadcast(writer, SendType.Reliable, sender);
                return;
            }
            if (sender.IsHost && rebarEvent.Kind != WKTReturnRebarEvent.CLAIM)
                Apply(rebarEvent);
        }

        /// <summary>Applies an event from another machine. False when it does not apply, so the host does not pass it on.</summary>
        private bool Apply(in WKTReturnRebarEvent rebarEvent) {
            var id = rebarEvent.Id;
            var player = rebarEvent.Player;
            var isHost = m_sync.IsHost;
            m_copies.TryGetValue(id, out var copy);
            switch (rebarEvent.Kind) {
                case WKTReturnRebarEvent.STUCK:
                    if (!m_registry.Stick(id, player, HostId, rebarEvent.LevelKey, rebarEvent.Position, rebarEvent.Rotation)) return false;
                    if (copy != null && m_registry.TryGet(id, out var stuck))
                        Stick(copy, stuck.Thrown);
                    else
                        Build(id);
                    return true;
                case WKTReturnRebarEvent.RECALL:
                    if (isHost ? !m_registry.OwnerRecall(id, player, WKTPlayerDeaths.IsDead(player)) : !m_registry.Grant(id, player)) return false;
                    if (copy != null)
                        BeginReturn(copy, player);
                    return true;
                case WKTReturnRebarEvent.RELEASED:
                    // The host only passes on a release from whoever the rebar flew back to; a release from the host is its own.
                    if (!m_registry.Release(id, player, HostId, isHost, rebarEvent.Position, rebarEvent.Velocity)) return false;
                    if (copy != null)
                        ApplyRelease(copy, WKTWorldOffset.FromWire(rebarEvent.Position, WKTWorldOffset.Get()), rebarEvent.Velocity);
                    return true;
                case WKTReturnRebarEvent.REMOVED:
                    if (!m_registry.Remove(id, player, HostId)) return false;
                    if (copy != null)
                        Remove(copy);
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>Host only: <paramref name="claimer"/> asks to recall a rebar whose thrower is dead or gone. The first claim wins.</summary>
        private void HandleClaim(byte claimer, uint id) {
            if (!m_registry.TryGet(id, out var entry)) return;
            var owner = entry.Thrown.Owner;
            if (!m_registry.Claim(id, claimer, WKTPlayerDeaths.IsDead(claimer), WKTPlayerDeaths.IsDead(owner), m_network.TryGetPlayer(owner, out _))) return;
            if (m_copies.TryGetValue(id, out var copy))
                BeginReturn(copy, claimer);
            var writer = m_network.BeginMessage(WKTMessageId.ReturnRebarEvent);
            WKTReturnRebarEvent.Of(id, WKTReturnRebarEvent.RECALL, claimer).Write(writer);
            m_network.Broadcast(writer, SendType.Reliable);
        }

        #endregion

        #region Copies

        /// <summary>Builds this machine's copy of another player's rebar from the item's own projectile prefab.</summary>
        private void Build(uint id) {
            if (m_copies.ContainsKey(id) || !m_registry.TryGet(id, out var entry)) return;
            var thrown = entry.Thrown;
            if (thrown.IsStuck && !WKTContentSeeding.TryGetLevel(thrown.LevelKey, out _)) {
                AddPending(thrown.LevelKey, id);
                return;
            }
            if (m_sync.PlayerShots.FindPrefab(thrown.ItemPrefab, thrown.Projectile) is not Projectile_ReturnRebar prefab) return;
            var rebar = Object.Instantiate(prefab, WKTWorldOffset.FromWire(thrown.Position, WKTWorldOffset.Get()), thrown.Rotation);
            var copy = Track(id, rebar);
            // As the game fires it: it never hits the player it would fly back to.
            rebar.damageTags.Add("player");
            var localPlayer = ENT_Player.playerObject;
            if (localPlayer != null)
                rebar.AddIgnore(localPlayer.gameObject);
            rebar.Initialize(rebar.speed > 0f ? thrown.Velocity / rebar.speed : thrown.Velocity);
            if (thrown.IsStuck)
                Stick(copy, thrown);
        }

        private WKTReturnRebarCopy Track(uint id, Projectile_ReturnRebar rebar) {
            var copy = rebar.gameObject.AddComponent<WKTReturnRebarCopy>();
            copy.Id = id;
            copy.Rebar = rebar;
            m_copies[id] = copy;
            m_sync.Handholds.RegisterRoot(rebar.gameObject, WKTHandholdKeys.GetRootKey(WKTHandholdKeys.ReturnRebarSalt, id));
            RefreshAuthority(copy);
            return copy;
        }

        /// <summary>Only the copy on the machine deciding the rebar hits anything; the others only show it.</summary>
        private void RefreshAuthority(WKTReturnRebarCopy copy) {
            var decidesHere = m_registry.TryGet(copy.Id, out var entry) && WKTReturnRebarRegistry.GetAuthority(entry, HostId) == LocalId;
            WKTPlayerShots.SetCosmetic(copy.Rebar, !decidesHere);
        }

        /// <summary>Puts a copy where the deciding machine's rebar stuck, climbable, as the game's own hit leaves it.</summary>
        private static void Stick(WKTReturnRebarCopy copy, WKTReturnRebarThrown thrown) {
            var rebar = copy.Rebar;
            var rebarTransform = rebar.transform;
            var position = WKTWorldOffset.FromWire(thrown.Position, WKTWorldOffset.Get());
            if (WKTContentSeeding.TryGetLevel(thrown.LevelKey, out var level) && level != null)
                rebarTransform.SetParent(level.transform, true);
            rebarTransform.SetPositionAndRotation(position, thrown.Rotation);
            rebarTransform.localScale = Vector3.one;
            rebar.returning = false;
            s_velocity(rebar) = Vector3.zero;
            s_hasHitSurface(rebar) = true;
            copy.Recaller = WKTReturnRebarRegistry.NOBODY;
            rebar.hitRootObject.SetActive(true);
            var softParent = s_softParent(rebar);
            if (softParent == null) return;
            softParent.ClearParent();
            var wall = FindWall(rebar, position, thrown.Rotation * Vector3.forward);
            if (wall != null)
                softParent.Initialize(wall);
        }

        /// <summary>The wall a rebar at <paramref name="position"/> points into, skipping the rebar's own handhold.</summary>
        private static Transform? FindWall(Projectile_ReturnRebar rebar, Vector3 position, Vector3 forward) {
            var count = Physics.RaycastNonAlloc(position - forward * c_wall_search, forward, s_wallHits, c_wall_search * 2f, rebar.hitMask, QueryTriggerInteraction.Ignore);
            Transform? nearest = null;
            var nearestDistance = float.MaxValue;
            for (var i = 0; i < count; i++) {
                var hit = s_wallHits[i];
                if (hit.collider == null || hit.collider.transform.IsChildOf(rebar.transform) || hit.distance >= nearestDistance) continue;
                nearest = hit.collider.transform;
                nearestDistance = hit.distance;
            }
            return nearest;
        }

        /// <summary>A copy starts flying back to <paramref name="recaller"/>. Anyone hanging on it lets go, as the game's own recall does.</summary>
        private void BeginReturn(WKTReturnRebarCopy copy, byte recaller) {
            var rebar = copy.Rebar;
            DropHands(rebar);
            copy.Recaller = recaller;
            copy.ReturnStartTime = Time.time;
            copy.ClaimPendingUntil = 0;
            if (!rebar.returning) {
                IsApplying = true;
                try {
                    s_startReturn(rebar);
                }
                finally {
                    IsApplying = false;
                }
            }
            RefreshAuthority(copy);
            if (recaller == LocalId)
                GiveSourceItem(copy);
        }

        /// <summary>
        /// A copy flying to the local player that was not thrown here gets the thrower's item, strain and all, which the game's
        /// own return puts in a hand or the bag.
        /// </summary>
        private void GiveSourceItem(WKTReturnRebarCopy copy) {
            var rebar = copy.Rebar;
            if (s_sourceItem(rebar) != null || rebar.returnItem == null || rebar.returnItem.itemData == null || !m_registry.TryGet(copy.Id, out var entry)) return;
            var item = rebar.returnItem.itemData.GetClone();
            if (entry.Thrown.ItemData.Count > 0) {
                item.data.Clear();
                item.data.AddRange(entry.Thrown.ItemData);
            }
            s_sourceItem(rebar) = item;
        }

        /// <summary>A copy flying to another player steers toward them, as the game's return does, and is gone once there.</summary>
        private static void FlyToRemote(WKTReturnRebarCopy copy) {
            var rebar = copy.Rebar;
            var position = rebar.transform.position;
            if (Time.time - copy.ReturnStartTime > c_return_timeout) {
                Remove(copy);
                return;
            }
            if (!WKTPlayerPositions.TryGetRemotePosition(copy.Recaller, out var target)) return;
            if (Vector3.Distance(position, target) < c_arrive_distance) {
                Remove(copy);
                return;
            }
            ref var velocity = ref s_velocity(rebar);
            velocity = Vector3.Lerp(velocity.normalized, (target - position).normalized, Time.deltaTime * rebar.returnRotate).normalized * rebar.returnSpeed;
        }

        /// <summary>Whoever a copy flew back to died or left: it flies on from where it was, and sticks in the next wall.</summary>
        private void ApplyRelease(WKTReturnRebarCopy copy, Vector3 position, Vector3 velocity) {
            var rebar = copy.Rebar;
            DropHands(rebar);
            rebar.returning = false;
            s_hasHitSurface(rebar) = false;
            rebar.hitRootObject.SetActive(false);
            s_softParent(rebar)?.ClearParent();
            rebar.transform.position = position;
            s_velocity(rebar) = velocity;
            copy.Recaller = WKTReturnRebarRegistry.NOBODY;
            RefreshAuthority(copy);
        }

        private static void Remove(WKTReturnRebarCopy copy) {
            copy.Removing = true;
            DropHands(copy.Rebar);
            Object.Destroy(copy.Rebar.gameObject);
        }

        private static void DropHands(Projectile_ReturnRebar rebar) {
            if (rebar.hitRootObject != null && rebar.hitRootObject.TryGetComponent<CL_Handhold>(out var handhold))
                handhold.ForceDropHands();
        }

        private static void HidePrompt(Projectile_ReturnRebar rebar) {
            if (!rebar.recoveryRootObject.activeSelf && !rebar.recoveryPrimeRootObject.activeSelf) return;
            rebar.recoveryRootObject.SetActive(false);
            rebar.recoveryPrimeRootObject.SetActive(false);
        }

        /// <summary>
        /// A copy is being destroyed. On the machine deciding it, the rebar is gone for everyone: it reached whoever recalled it,
        /// or was lost. Anywhere else a level unloading only takes this machine's copy, which is built again with the level.
        /// </summary>
        public void OnCopyDestroyed(WKTReturnRebarCopy copy) {
            if (!m_copies.TryGetValue(copy.Id, out var tracked) || tracked != copy) return;
            m_copies.Remove(copy.Id);
            WKTPlayerShots.SetCosmetic(copy.Rebar, false);
            if (copy.Removing || !m_registry.TryGet(copy.Id, out var entry)) return;
            var local = LocalId;
            if (WKTReturnRebarRegistry.GetAuthority(entry, HostId) == local) {
                if (m_registry.Remove(copy.Id, local, HostId))
                    Send(WKTReturnRebarEvent.Of(copy.Id, WKTReturnRebarEvent.REMOVED, local));
                return;
            }
            if (entry.Recaller == WKTReturnRebarRegistry.NOBODY && entry.Thrown.IsStuck)
                AddPending(entry.Thrown.LevelKey, copy.Id);
        }

        private void AddPending(ulong levelKey, uint id) {
            if (!m_pending.TryGetValue(levelKey, out var waiting)) {
                waiting = new List<uint>();
                m_pending.Add(levelKey, waiting);
            }
            if (!waiting.Contains(id))
                waiting.Add(id);
        }

        private static bool TryGetLevelKey(Projectile_ReturnRebar rebar, out ulong levelKey) {
            var level = rebar.GetComponentInParent<M_Level>();
            if (level == null && WorldLoader.initialized && WorldLoader.instance != null)
                level = WorldLoader.instance.GetCurrentLevel()?.GetLevel();
            levelKey = 0;
            return level != null && WKTContentSeeding.TryGetLevelKey(level, out levelKey);
        }

        #endregion

        /// <summary>A level was built here: the rebars stuck in it are built now.</summary>
        public void OnLevelKeyed(ulong levelKey) {
            if (!m_pending.Remove(levelKey, out var waiting)) return;
            foreach (var id in waiting)
                Build(id);
        }

        /// <summary>
        /// A level unloaded here. The host forgets the rebars stuck in it, since the host keeps every level any player is in. A
        /// client keeps them in its registry: the host still has them.
        /// </summary>
        public void OnLevelForgetting(ulong levelKey) {
            m_pending.Remove(levelKey);
            if (m_sync.IsHost)
                m_registry.ForgetLevel(levelKey);
        }

        /// <summary>The host changed: whoever decides each ownerless rebar may have too.</summary>
        public void OnHostChanged() {
            foreach (var copy in m_copies.Values) {
                if (copy != null)
                    RefreshAuthority(copy);
            }
        }

        /// <summary>Host only: tells a player who (re)connected about every rebar stuck in a wall or still flying out.</summary>
        public void SendAll(WKTPlayer player) {
            foreach (var entry in m_registry.Rebars) {
                // Stuck ones and ones still flying out: a flying one is thrown again from where it was thrown, and snaps to
                // where it sticks once its thrower's machine says. One flying back is about to be gone.
                if (entry.Recaller != WKTReturnRebarRegistry.NOBODY) continue;
                var writer = m_network.BeginMessage(WKTMessageId.ReturnRebarThrown);
                entry.Thrown.Write(writer);
                m_network.SendToPlayer(player, writer, SendType.Reliable);
            }
        }
    }
}
