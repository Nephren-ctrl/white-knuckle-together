using System;
using System.Collections.Generic;
using HarmonyLib;
using JetBrains.Annotations;
using Steamworks.Data;
using UnityEngine;
using WhiteKnuckleTogether.Networking.LiteNetLib;
using Object = UnityEngine.Object;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// Shares every enemy, item and prop of a synced co-op run. The host registers each world object it has, gives it a
    /// handle, tells every client about it and streams its state. A client turns its own copy of each such object into a
    /// puppet, spawns the ones it has no copy of, and hides every object the host never claimed, so it has no world objects
    /// of its own. <see cref="Authority"/> decides which machine simulates each object: usually the host, but an enemy
    /// engaged with a client or a prop a client holds is simulated by that client and shown as a puppet everywhere else,
    /// the host included. Hits on puppets go to the machine simulating them, and the host decides every kill.
    /// Lives next to <see cref="WKTRunController"/> and only exists during a synced co-op run.
    /// </summary>
    internal sealed class WKTWorldSync : MonoBehaviour, IWKTPuppetActions<WKTNetIdentity> {
        /// <summary>How long a client waits for its own copy of a host object to appear before spawning one.</summary>
        public const double SPAWN_GRACE = 0.75;
        /// <summary>How long a client's copy of a dead object the host removed may stay, for its own death (effects, a roach's delayed pop) to finish.</summary>
        public const double DEAD_DESPAWN_GRACE = 5.0;
        private const SendType c_state_send_type = SendType.Unreliable | SendType.NoNagle;
        private const string c_runtime_salt = "WKT.RuntimeObject";
        private const string c_drop_salt = "WKT.Drop";
        private const string c_leave_salt = "WKT.Leave";
        /// <summary>How far, beyond reach, a client's pickup may be from where the host has that client. Covers latency.</summary>
        private const float c_pickup_range = 12f;
        private const string c_hammer_tag = "hammer";
        /// <summary>How long a puppet's animator blends into a state the host is in, in seconds.</summary>
        private const float c_state_blend = 0.1f;
        /// <summary>Set on runtime keys, so they never collide with content keys.</summary>
        private const ulong c_runtime_bit = WKTNestedKeys.RUNTIME_BIT;
        /// <summary>Puppets by GameEntity instance id, on any machine: the game's own logic is skipped for these.</summary>
        private static readonly HashSet<int> s_puppetEntities = new();
        /// <summary>Client only: what the local player's own actions spawn into the world, handed to the host like drops.</summary>
        private static readonly WKTSpawnScope<Object> s_localSpawns = new();
        private static readonly List<Object> s_localSpawnScratch = new();
        private static readonly HashSet<string> s_unclaimableSpawns = new();
        private static readonly HashSet<string> s_missingPrefabs = new();
        /// <summary>Prefabs no asset database lists, found among loaded objects by name.</summary>
        private static readonly Dictionary<string, GameObject> s_loadedPrefabs = new();
        private static readonly AccessTools.FieldRef<List<CL_AssetManager.WKDatabaseHolder>?> s_assetDatabases =
            AccessTools.StaticFieldRefAccess<List<CL_AssetManager.WKDatabaseHolder>?>(AccessTools.Field(typeof(CL_AssetManager), "databases"));
        private static readonly List<CL_Handhold> s_handholdScratch = new();
        private static readonly List<UT_SeamlessTeleporter> s_teleporterScratch = new();
        private readonly Dictionary<ushort, WKTNetIdentity> m_byHandle = new(256);
        private readonly UShortIdPool m_handles = new();
        private readonly WKTEntityStateSender m_sender = new();
        private readonly WKTSendClock m_sendClock = new();
        private readonly Dictionary<ushort, WKTEntitySnapshotBuffer> m_buffers = new(256);
        private readonly List<WKTNetIdentity> m_hideQueue = new();
        private readonly WKTEntityBind m_bind = new();
        private readonly float[] m_animatorValues = new float[WKTEntityState.MAX_ANIMATOR_VALUES];
        private readonly int[] m_animatorStates = new int[WKTEntityState.MAX_ANIMATOR_LAYERS];
        private readonly List<ushort> m_handleScratch = new();
        /// <summary>Client only: the host's items the local player took and the host has yet to give them.</summary>
        private readonly WKTItemClaims<Item> m_claims = new();
        private readonly List<PendingDrop> m_pendingDrops = new();
        private readonly WKTDropRequest m_dropRequest = new();
        /// <summary>Host only: keeps the items clients drop from falling out of the world.</summary>
        private readonly WKTDropSettler m_dropSettler = new();
        private uint m_dropCount;
        /// <summary>Host only: how many items of players who left this machine has dropped, for their keys.</summary>
        private uint m_leaveDropCount;
        private WKTNetwork m_network = null!;
        private WKTPuppetBinder<WKTNetIdentity> m_binder = null!;
        private Action<M_Level, ulong> m_onLevelKeyed = null!;
        private Action<M_Level> m_onLevelForgetting = null!;
        private Func<NetDataWriter> m_beginStatePacket = null!;
        private Action<NetDataWriter> m_sendStatePacket = null!;
        private Action<WKTPlayer> m_onPlayerConnected = null!;
        private Action<WKTPlayer> m_onPlayerDisconnected = null!;
        private Action m_onHostChanged = null!;
        private WKTPlayer? m_stateTarget;
        /// <summary>While applying a hit another machine forwarded: who dealt it, and whether their own player did.</summary>
        private byte m_forwardDealer = WKTEntityAuthority.NOBODY;
        private bool m_forwardByPlayer;
        /// <summary>Host only: the player a client's kill claim being applied credits, for the kill it tells everyone about.</summary>
        private byte m_claimedKiller = WKTDamageCause.NOBODY;
        private ulong m_runtimeCount;
        private int m_inventoryLayer;
        /// <summary>Client only: the key the object being spawned gets, so it binds to the host's object.</summary>
        private ulong m_spawningKey;
        private bool m_registering;
        /// <summary>Host only: set while spawning a dropped item, which registers once its data is in place.</summary>
        private bool m_suppressRegistration;

        /// <summary>The world sync of the synced co-op run in progress. Null everywhere else, including singleplayer.</summary>
        public static WKTWorldSync? Instance { get; private set; }
        /// <summary>While true, a client's copy of a host object may be killed: the host's kill is being applied.</summary>
        public static bool AllowHostKill { get; private set; }
        /// <summary>Set while a hand item goes into the bag, which the game shows in the world for a moment first.</summary>
        public static bool IsStowing { get; set; }
        /// <summary>Set while this machine spawns objects of its own alone, which are marked <see cref="WKTLocalObject"/> as they register.</summary>
        public static int LocalSpawnDepth { get; set; }
        public bool IsHost => m_network.Mode == NetworkMode.Host;
        public WKTAuthoritySync Authority { get; private set; } = null!;
        public WKTSharedHolds SharedHolds { get; private set; } = null!;
        /// <summary>Host only: every world object the host has, by handle.</summary>
        public Dictionary<ushort, WKTNetIdentity>.ValueCollection HostIdentities => m_byHandle.Values;
        public WKTTongueSync Tongues { get; private set; } = null!;
        public WKTEnemyHits EnemyHits { get; private set; } = null!;
        public WKTPlayerShots PlayerShots { get; private set; } = null!;
        public WKTPitonSync Pitons { get; private set; } = null!;
        public WKTReturnRebarSync ReturnRebars { get; private set; } = null!;
        public WKTRopeSync Ropes { get; private set; } = null!;
        public WKTPlacedRopeSync PlacedRopes { get; private set; } = null!;
        public WKTHandholdSync Handholds { get; private set; } = null!;
        public WKTEntitySounds Sounds { get; private set; } = null!;
        public WKTEntityEvents Events { get; private set; } = null!;
        public WKTPuppetPresentation Presentation { get; private set; } = null!;
        public WKTSharedClock Clock { get; private set; } = null!;
        public WKTEntityForces Forces { get; private set; } = null!;
        public WKTTouchKills TouchKills { get; private set; } = null!;
        public WKTViewerCreatures Viewers { get; private set; } = null!;
        public WKTMotherWrath MotherWrath { get; private set; } = null!;
        public WKTPlayerPerks Perks { get; private set; } = null!;
        public WKTPlayerInventories Inventories { get; private set; } = null!;
        public WKTEnemyShots EnemyShots { get; private set; } = null!;
        public WKTRecyclers Recyclers { get; private set; } = null!;
        public WKTHazardSync Hazards { get; private set; } = null!;
        public WKTLadderSecret Ladder { get; private set; } = null!;
        public WKTEventSpawns EventSpawns { get; private set; } = null!;

        /// <summary>Whether <paramref name="entity"/> is a puppet another machine simulates, whose own logic must not run here.</summary>
        public static bool IsPuppet(GameEntity entity) => s_puppetEntities.Count != 0 && s_puppetEntities.Contains(entity.GetInstanceID());

        [UsedImplicitly]
        private void Awake() {
            m_network = WKT.Instance.Network;
            if (!m_network.Session.IsSyncedWorld) {
                enabled = false;
                return;
            }
            Instance = this;
            m_inventoryLayer = LayerMask.NameToLayer("Inventory");
            m_binder = new WKTPuppetBinder<WKTNetIdentity>(this, SPAWN_GRACE) { DeadDespawnGrace = DEAD_DESPAWN_GRACE };
            Tongues = new WKTTongueSync(this, m_network);
            EnemyHits = new WKTEnemyHits(this, m_network);
            PlayerShots = new WKTPlayerShots(this, m_network);
            Pitons = new WKTPitonSync(this, m_network);
            ReturnRebars = new WKTReturnRebarSync(this, m_network);
            Authority = new WKTAuthoritySync(this, m_network);
            SharedHolds = new WKTSharedHolds(this, m_network);
            Ropes = new WKTRopeSync(this, m_network);
            PlacedRopes = new WKTPlacedRopeSync(this, m_network);
            Handholds = new WKTHandholdSync(this, m_network);
            Sounds = new WKTEntitySounds(this, m_network);
            Events = new WKTEntityEvents(this, m_network);
            Presentation = new WKTPuppetPresentation(this, m_network);
            Clock = new WKTSharedClock(this, m_network);
            Forces = new WKTEntityForces(this, m_network);
            TouchKills = new WKTTouchKills(this, m_network);
            Viewers = new WKTViewerCreatures(this, m_network);
            MotherWrath = new WKTMotherWrath(this, m_network);
            Perks = new WKTPlayerPerks(this, m_network);
            Inventories = new WKTPlayerInventories(this, m_network);
            EnemyShots = new WKTEnemyShots(this, m_network);
            Recyclers = new WKTRecyclers(this, m_network);
            Hazards = new WKTHazardSync(this, m_network);
            Ladder = new WKTLadderSecret(this, m_network);
            EventSpawns = new WKTEventSpawns(this, m_network);
            m_onLevelKeyed = (level, levelKey) => {
                Pitons.OnLevelKeyed(levelKey);
                ReturnRebars.OnLevelKeyed(levelKey);
                PlacedRopes.OnLevelKeyed(levelKey);
                Handholds.IndexLevel(level, levelKey);
                Hazards.OnLevelKeyed(level);
                Ladder.OnLevelKeyed(level, levelKey);
            };
            m_onLevelForgetting = level => {
                if (!WKTContentSeeding.TryGetLevelKey(level, out var levelKey)) return;
                Pitons.OnLevelForgetting(levelKey);
                ReturnRebars.OnLevelForgetting(levelKey);
                PlacedRopes.OnLevelForgetting(levelKey);
                Handholds.OnLevelForgetting(levelKey);
            };
            WKTContentSeeding.LevelKeyed += m_onLevelKeyed;
            WKTContentSeeding.LevelForgetting += m_onLevelForgetting;
            m_beginStatePacket = () => m_network.BeginMessage(WKTMessageId.EntityStates);
            m_sendStatePacket = writer => m_network.SendToPlayer(m_stateTarget!, writer, c_state_send_type);
            var messages = m_network.Messages;
            messages.Register(WKTMessageId.EntityBind, OnEntityBind);
            messages.Register(WKTMessageId.EntityDespawn, OnEntityDespawn);
            messages.Register(WKTMessageId.EntityActivation, OnEntityActivation);
            messages.Register(WKTMessageId.EntityKill, OnEntityKill);
            messages.Register(WKTMessageId.EntityGib, OnEntityGib);
            messages.Register(WKTMessageId.DamageRequest, OnDamageRequest);
            messages.Register(WKTMessageId.EntityStates, OnEntityStates);
            messages.Register(WKTMessageId.SeamlessTeleport, OnSeamlessTeleport);
            messages.Register(WKTMessageId.EventSpawn, EventSpawns.OnMessage);
            messages.Register(WKTMessageId.PickupRequest, OnPickupRequest);
            messages.Register(WKTMessageId.PickupResult, OnPickupResult);
            messages.Register(WKTMessageId.DropRequest, OnDropRequest);
            messages.Register(WKTMessageId.EntityGrab, Authority.OnEntityGrab);
            messages.Register(WKTMessageId.EntityAuthority, Authority.OnEntityAuthority);
            messages.Register(WKTMessageId.OwnerStates, Authority.OnOwnerStates);
            messages.Register(WKTMessageId.EntityRelease, Authority.OnEntityRelease);
            messages.Register(WKTMessageId.RiderInput, SharedHolds.OnRiderInput);
            messages.Register(WKTMessageId.RiderBuck, SharedHolds.OnRiderBuck);
            messages.Register(WKTMessageId.FlierBucked, SharedHolds.OnFlierBucked);
            messages.Register(WKTMessageId.PlayerEffect, OnPlayerEffect);
            messages.Register(WKTMessageId.ProjectileFired, EnemyShots.OnFired);
            messages.Register(WKTMessageId.ProjectileReleased, EnemyShots.OnReleased);
            messages.Register(WKTMessageId.ProjectileStuck, EnemyShots.OnStuck);
            messages.Register(WKTMessageId.ProjectileGone, EnemyShots.OnGone);
            messages.Register(WKTMessageId.HazardCall, Hazards.OnHazardCall);
            messages.Register(WKTMessageId.VentOpened, Ladder.OnVentOpened);
            messages.Register(WKTMessageId.LadderNightmare, Ladder.OnNightmareAttack);
            messages.Register(WKTMessageId.ItemConsume, OnItemConsume);
            messages.Register(WKTMessageId.ItemPaid, OnItemPaid);
            messages.Register(WKTMessageId.TongueState, Tongues.OnTongueEvent);
            messages.Register(WKTMessageId.PlayerHit, OnPlayerHit);
            messages.Register(WKTMessageId.AttackPerformed, EnemyHits.OnAttackEvent);
            messages.Register(WKTMessageId.PlayerShot, PlayerShots.OnShot);
            messages.Register(WKTMessageId.PitonPlaced, Pitons.OnPlaced);
            messages.Register(WKTMessageId.PitonChange, Pitons.OnChange);
            messages.Register(WKTMessageId.ReturnRebarThrown, ReturnRebars.OnThrown);
            messages.Register(WKTMessageId.ReturnRebarEvent, ReturnRebars.OnEvent);
            messages.Register(WKTMessageId.RopeStates, Ropes.OnRopeStates);
            messages.Register(WKTMessageId.RopeHold, Ropes.OnRopeHold);
            messages.Register(WKTMessageId.RopeAuthority, Ropes.OnRopeAuthority);
            messages.Register(WKTMessageId.RopeForce, Ropes.OnRopeForce);
            messages.Register(WKTMessageId.RopePlaced, PlacedRopes.OnPlaced);
            messages.Register(WKTMessageId.HandholdSound, Handholds.OnSound);
            messages.Register(WKTMessageId.HandholdEvent, Handholds.OnEvent);
            messages.Register(WKTMessageId.HandholdHoldTime, Handholds.OnHoldTime);
            messages.Register(WKTMessageId.EntitySound, Sounds.OnSound);
            messages.Register(WKTMessageId.BuddyLine, Sounds.OnBuddyLine);
            messages.Register(WKTMessageId.EntityUnityEvent, Events.OnEvent);
            messages.Register(WKTMessageId.EntityPresentation, Presentation.OnPresentation);
            messages.Register(WKTMessageId.ClockSync, Clock.OnClockSync);
            messages.Register(WKTMessageId.EntityForce, Forces.OnForce);
            messages.Register(WKTMessageId.RemainsSeen, Viewers.OnRemainsSeen);
            messages.Register(WKTMessageId.TeethLightFear, Viewers.OnTeethLightFear);
            messages.Register(WKTMessageId.MotherEnrage, MotherWrath.OnEnrage);
            messages.Register(WKTMessageId.MotherLine, MotherWrath.OnLine);
            messages.Register(WKTMessageId.MotherGlare, MotherWrath.OnGlare);
            messages.Register(WKTMessageId.EntityInteraction, Events.OnInteraction);
            messages.Register(WKTMessageId.PlayerPerks, Perks.OnPerks);
            messages.Register(WKTMessageId.PlayerMassBuffs, Perks.OnMassBuffs);
            messages.Register(WKTMessageId.PlayerInventory, Inventories.OnInventory);
            m_onPlayerDisconnected = HandlePlayerDisconnected;
            m_network.OnPlayerDisconnected += m_onPlayerDisconnected;
            m_onPlayerConnected = HandlePlayerConnected;
            m_network.OnPlayerConnected += m_onPlayerConnected;
            m_onHostChanged = OnHostChanged;
            m_network.OnHostChanged += m_onHostChanged;
            Ropes.RegisterExisting();
            Handholds.IndexExisting();
            WKT.Instance.Logger.LogInfo($"World sync started as {(IsHost ? "host" : "client")}.");
        }

        [UsedImplicitly]
        private void OnDestroy() {
            if (Instance != this) return;
            Instance = null;
            s_puppetEntities.Clear();
            s_localSpawns.Clear();
            WKTBearTraps.Clear();
            WKTPlayerShots.ClearCosmetic();
            WKTPuppetHitScope.Reset();
            WKTLocalReplayScope.Reset();
            WKTDamageCause.Reset();
            WKTSharedEffectScope.Reset();
            WKTMeleeReplay.Exit();
            Sounds.Clear();
            Events.Clear();
            Presentation.Clear();
            Forces.Clear();
            TouchKills.Clear();
            Viewers.Clear();
            MotherWrath.Clear();
            Perks.Clear();
            Inventories.Clear();
            EnemyShots.Clear();
            Recyclers.Clear();
            Hazards.Clear();
            Ladder.Clear();
            EventSpawns.Clear();
            WKTSharedMotion.Reset();
            AllowHostKill = false;
            WKTContentSeeding.LevelKeyed -= m_onLevelKeyed;
            WKTContentSeeding.LevelForgetting -= m_onLevelForgetting;
            Authority.Dispose();
            if (WKT.InstanceOrNull == null) return;
            var messages = m_network.Messages;
            messages.Unregister(WKTMessageId.EntityBind);
            messages.Unregister(WKTMessageId.EntityDespawn);
            messages.Unregister(WKTMessageId.EntityActivation);
            messages.Unregister(WKTMessageId.EntityKill);
            messages.Unregister(WKTMessageId.EntityGib);
            messages.Unregister(WKTMessageId.DamageRequest);
            messages.Unregister(WKTMessageId.SeamlessTeleport);
            messages.Unregister(WKTMessageId.EventSpawn);
            messages.Unregister(WKTMessageId.EntityStates);
            messages.Unregister(WKTMessageId.PickupRequest);
            messages.Unregister(WKTMessageId.PickupResult);
            messages.Unregister(WKTMessageId.DropRequest);
            messages.Unregister(WKTMessageId.EntityGrab);
            messages.Unregister(WKTMessageId.EntityAuthority);
            messages.Unregister(WKTMessageId.OwnerStates);
            messages.Unregister(WKTMessageId.EntityRelease);
            messages.Unregister(WKTMessageId.RiderInput);
            messages.Unregister(WKTMessageId.RiderBuck);
            messages.Unregister(WKTMessageId.FlierBucked);
            messages.Unregister(WKTMessageId.PlayerEffect);
            messages.Unregister(WKTMessageId.ProjectileFired);
            messages.Unregister(WKTMessageId.ProjectileReleased);
            messages.Unregister(WKTMessageId.ProjectileStuck);
            messages.Unregister(WKTMessageId.ProjectileGone);
            messages.Unregister(WKTMessageId.HazardCall);
            messages.Unregister(WKTMessageId.VentOpened);
            messages.Unregister(WKTMessageId.LadderNightmare);
            messages.Unregister(WKTMessageId.ItemConsume);
            messages.Unregister(WKTMessageId.ItemPaid);
            messages.Unregister(WKTMessageId.TongueState);
            messages.Unregister(WKTMessageId.PlayerHit);
            messages.Unregister(WKTMessageId.AttackPerformed);
            messages.Unregister(WKTMessageId.PlayerShot);
            messages.Unregister(WKTMessageId.PitonPlaced);
            messages.Unregister(WKTMessageId.PitonChange);
            messages.Unregister(WKTMessageId.ReturnRebarThrown);
            messages.Unregister(WKTMessageId.ReturnRebarEvent);
            messages.Unregister(WKTMessageId.RopeStates);
            messages.Unregister(WKTMessageId.RopeHold);
            messages.Unregister(WKTMessageId.RopeAuthority);
            messages.Unregister(WKTMessageId.RopeForce);
            messages.Unregister(WKTMessageId.RopePlaced);
            messages.Unregister(WKTMessageId.HandholdSound);
            messages.Unregister(WKTMessageId.HandholdEvent);
            messages.Unregister(WKTMessageId.HandholdHoldTime);
            messages.Unregister(WKTMessageId.EntitySound);
            messages.Unregister(WKTMessageId.BuddyLine);
            messages.Unregister(WKTMessageId.EntityUnityEvent);
            messages.Unregister(WKTMessageId.EntityPresentation);
            messages.Unregister(WKTMessageId.ClockSync);
            messages.Unregister(WKTMessageId.EntityForce);
            messages.Unregister(WKTMessageId.RemainsSeen);
            messages.Unregister(WKTMessageId.TeethLightFear);
            messages.Unregister(WKTMessageId.MotherEnrage);
            messages.Unregister(WKTMessageId.MotherLine);
            messages.Unregister(WKTMessageId.MotherGlare);
            messages.Unregister(WKTMessageId.EntityInteraction);
            messages.Unregister(WKTMessageId.PlayerPerks);
            messages.Unregister(WKTMessageId.PlayerMassBuffs);
            messages.Unregister(WKTMessageId.PlayerInventory);
            m_network.OnPlayerDisconnected -= m_onPlayerDisconnected;
            m_network.OnPlayerConnected -= m_onPlayerConnected;
            m_network.OnHostChanged -= m_onHostChanged;
        }

        [UsedImplicitly]
        private void Update() {
            if (!m_network.IsMultiplayerRun) return;
            var now = Time.realtimeSinceStartupAsDouble;
            ReturnRebars.Update();
            Clock.Update(now);
            if (IsHost) {
                WKTTargets.FlushForces();
                m_dropSettler.Update(now);
                if (m_sendClock.TryAdvance(now, out var sequence)) {
                    Authority.HostTick(now);
                    SendStates(sequence, now);
                }
            }
            else {
                m_binder.Update(now);
                m_claims.ExpireBeforeBind(now);
                SendPendingDrops();
                Authority.ClientTick(now);
            }
            SharedHolds.Update(now);
            Ropes.Update(now);
            Handholds.Update(now);
            EnemyHits.Update(now);
            TouchKills.Update(now);
            Viewers.Update(now);
            Perks.Update(now);
            Inventories.Update(now);
            EnemyShots.Update();
            Hazards.Update();
            WKTSharedMotion.Update();
            ApplyPuppetPoses(now);
            Presentation.Update(now);
        }

        [UsedImplicitly]
        private void FixedUpdate() {
            if (m_network.IsMultiplayerRun)
                Presentation.FixedUpdate();
        }

        [UsedImplicitly]
        private void LateUpdate() {
            if (m_network.IsMultiplayerRun)
                Forces.Flush();
            // Decided here rather than when registered: deactivating an object from inside its own activation is not allowed,
            // and the game often moves a new object into the bag or a hand in the same frame it creates it.
            foreach (var identity in m_hideQueue) {
                if (identity == null) continue;
                var identityObject = identity.gameObject;
                var action = WKTClientRegistration.Decide(identity.Handle != 0 || identity.IsLocalDrop, identity.Key != 0, identityObject.activeSelf,
                    !IsInInventory(identityObject), IsReplicated(identity));
                switch (action) {
                    case WKTUnclaimedAction.Hide:
                        identityObject.SetActive(false);
                        break;
                    case WKTUnclaimedAction.Forget:
                        if (identity.Key != 0)
                            m_binder.OnLocalDestroyed(identity.Key, identity, Time.realtimeSinceStartupAsDouble);
                        identity.Key = 0;
                        Object.Destroy(identity);
                        break;
                }
            }
            m_hideQueue.Clear();
        }

        #region Registration

        /// <summary>A world object became live. Called for every enabled GameEntity and every started item.</summary>
        public void Register(Component owner) {
            if (m_suppressRegistration) return;
            var ownerObject = owner.gameObject;
            if (ownerObject.TryGetComponent<WKTNetIdentity>(out _) || !IsReplicated(owner) || ownerObject.TryGetComponent<WKTLocalObject>(out _)) return;
            // Spawned as this machine's own, such as a player's own artifact picks: their props register as they are made.
            if (LocalSpawnDepth > 0) {
                ownerObject.AddComponent<WKTLocalObject>();
                return;
            }
            // Each player's own copy of a placed progression pickup, such as the flashlight before the Teeth. Items are also
            // props, and the prop's GameEntity.OnEnable registers them before Item_Object.Start, so the item is looked up.
            if (ownerObject.TryGetComponent<Item_Object>(out var placed) && WKTInstancedItems.IsInstanced(placed)) {
                ownerObject.AddComponent<WKTLocalObject>();
                return;
            }
            // A client's bag holds only its own items. The host keeps its bagged items registered, reported as out of the
            // world, so they appear for everyone once dropped.
            if (!IsHost && (IsStowing || IsInInventory(ownerObject))) return;
            var level = ownerObject.GetComponentInParent<M_Level>(true);
            ulong levelKey = 0;
            // Levels are briefly enabled while generating, before they are keyed. Their objects register the next time
            // they are enabled, once every machine can tell them apart.
            if (level != null && !WKTContentSeeding.TryGetLevelKey(level, out levelKey) && m_spawningKey == 0) return;
            if (level == null && IsInChamber(ownerObject))
                levelKey = WKTPlayerRooms.CHAMBER;
            m_registering = true;
            WKTNetIdentity identity;
            try {
                identity = ownerObject.AddComponent<WKTNetIdentity>();
            }
            finally {
                m_registering = false;
            }
            identity.Entity = ownerObject.GetComponent<GameEntity>();
            identity.Prop = ownerObject.GetComponent<CL_Prop>();
            identity.Item = ownerObject.GetComponent<Item_Object>();
            identity.Kind = identity.Item != null ? WKTNetKind.Item : identity.Entity is CL_Prop ? WKTNetKind.Prop : WKTNetKind.Enemy;
            identity.Body = ownerObject.GetComponent<Rigidbody>();
            identity.Animator = identity.Entity != null ? WKTEntityAnimators.Find(identity.Entity) : ownerObject.GetComponentInChildren<Animator>();
            identity.ReportedActive = IsInWorld(identity);
            identity.LevelKey = levelKey;
            identity.SelfDriven = WKTAmbientMotion.IsSelfDriven(ownerObject);
            Events.Index(identity);
            Presentation.Register(identity);
            Sounds.Register(identity);
            Viewers.Register(identity);
            identity.NestedIn = identity.Item == null ? FindNestingIdentity(ownerObject) : null;
            var hasKey = TryGetKey(identity, out var key);
            if (IsHost) {
                identity.Key = hasKey ? key : c_runtime_bit | WKTStableHash.Mix(WKTStableHash.Of(c_runtime_salt), ++m_runtimeCount);
                NameHandholds(identity);
                identity.Handle = m_handles.GetNext();
                m_byHandle[identity.Handle] = identity;
                WriteBind(identity);
                var writer = m_network.BeginMessage(WKTMessageId.EntityBind);
                m_bind.Write(writer);
                m_network.Broadcast(writer, SendType.Reliable);
                return;
            }
            if (!hasKey) {
                // Never matched by a host object, and the client may have no world objects of its own.
                EventSpawns.OnUnmatched(identity);
                Hide(identity);
                return;
            }
            identity.Key = key;
            NameHandholds(identity);
            m_binder.OnLocalRegister(key, identity);
        }

        /// <summary>The object's handholds, such as a flier's or a stuck rebar's, are named by its key on every machine.</summary>
        private void NameHandholds(WKTNetIdentity identity) =>
            Handholds.RegisterRoot(identity.gameObject, WKTHandholdKeys.GetRootKey(WKTHandholdKeys.IdentitySalt, identity.Key), true);

        /// <summary>
        /// The world object a creature is nested in, such as a barnacle zombie for its barnacle, registered first if it was not
        /// yet, so it takes the key of a spawn in progress rather than its child. Null for anything else.
        /// </summary>
        private WKTNetIdentity? FindNestingIdentity(GameObject ownerObject) {
            var parent = ownerObject.transform.parent;
            var parentEntity = parent != null ? parent.GetComponentInParent<GameEntity>(true) : null;
            if (parentEntity == null || !IsReplicated(parentEntity)) return null;
            if (parentEntity.TryGetComponent<WKTNetIdentity>(out var existing)) return existing;
            Register(parentEntity);
            return parentEntity.TryGetComponent<WKTNetIdentity>(out var registered) ? registered : null;
        }

        /// <summary>A nested object's key, from its parent object's key and the path down to it.</summary>
        private static bool TryGetNestedKey(WKTNetIdentity identity, WKTNetIdentity parent, out ulong key) {
            key = 0;
            if (parent.Key == 0) return false;
            var path = WKTNestedKeys.EmptyPath;
            var parentTransform = parent.transform;
            for (var current = identity.transform; current != null && current != parentTransform; current = current.parent)
                path = WKTNestedKeys.Step(path, current.name, current.GetSiblingIndex());
            key = WKTNestedKeys.Derive(parent.Key, path);
            return true;
        }

        private bool TryGetKey(WKTNetIdentity identity, out ulong key) {
            if (identity.NestedIn != null) return TryGetNestedKey(identity, identity.NestedIn, out key);
            if (m_spawningKey != 0) {
                key = m_spawningKey;
                m_spawningKey = 0;
                return true;
            }
            Component? keyed = identity.Entity != null ? identity.Entity : identity.Item;
            key = 0;
            return keyed != null && WKTContentSeeding.TryGetKey(keyed, out key);
        }

        /// <summary>
        /// Players and whatever they carry are never world objects, nor is the goo, which <see cref="WKTGooSync"/> shares, nor a
        /// mask, which haunts each player on their own machine.
        /// </summary>
        private static bool IsReplicated(Component owner) =>
            owner is not ENT_Player && owner is not DEN_DeathFloor && owner is not DEN_Mask && owner.GetComponentInParent<ENT_Player>(true) == null &&
            owner.GetComponentInParent<WKTRemotePlayer>(true) == null;

        /// <summary>Whether clients should show the object: it is active itself and not in someone's bag.</summary>
        private bool IsInWorld(WKTNetIdentity identity) => identity.gameObject.activeSelf && identity.gameObject.layer != m_inventoryLayer;

        /// <summary>Whether an object is in the local player's bag or hand rather than in the world.</summary>
        private bool IsInInventory(GameObject target) {
            if (target.layer == m_inventoryLayer) return true;
            var inventory = Inventory.instance;
            return inventory != null && inventory.itemRoot != null && target.transform.IsChildOf(inventory.itemRoot);
        }

        private void WriteBind(WKTNetIdentity identity) {
            var offset = WKTWorldOffset.Get();
            var objectTransform = identity.transform;
            m_bind.Handle = identity.Handle;
            m_bind.Key = identity.Key;
            m_bind.Kind = identity.Kind;
            m_bind.Flags = IsInWorld(identity) ? WKTEntityBind.FLAG_ACTIVE : (byte)0;
            m_bind.PrefabId = GetPrefabId(identity);
            m_bind.LevelKey = identity.LevelKey;
            m_bind.Position = WKTWorldOffset.ToWire(objectTransform.position, offset);
            m_bind.Rotation = objectTransform.rotation;
            m_bind.ItemData.Clear();
            if (identity.Item != null)
                WKTItemModules.Pack(identity.Item.itemData);
            var data = identity.Item != null ? identity.Item.itemData?.data : null;
            if (data != null) {
                for (var i = 0; i < data.Count && i < WKTWire.MAX_ITEM_DATA; i++)
                    m_bind.ItemData.Add(data[i] ?? string.Empty);
            }
        }

        /// <summary>
        /// The id clients spawn an object from (see <see cref="GetPrefab"/>). An entity id no asset database lists (empty, the
        /// game's default <c>entity</c>, or one only a level's spawner holds the prefab of) is sent as the object's prefab
        /// name instead.
        /// </summary>
        public static string GetPrefabId(WKTNetIdentity identity) {
            if (identity.Item != null && identity.Item.itemData != null && !string.IsNullOrEmpty(identity.Item.itemData.prefabName))
                return identity.Item.itemData.prefabName;
            if (identity.Entity == null) return string.Empty;
            var id = identity.Entity.entityPrefabID;
            return IsListedAsset(id) ? id : PrefabNameOf(identity.Entity.gameObject.name);
        }

        /// <summary>Whether an asset database lists <paramref name="id"/>, checked without the game's warning for unknown ids.</summary>
        private static bool IsListedAsset(string? id) {
            if (string.IsNullOrEmpty(id)) return false;
            // Not set up yet: the id is sent as it is.
            if (s_assetDatabases() is not { } databases) return true;
            foreach (var database in databases) {
                if (database.HasAssetInDictionary(id)) return true;
            }
            return false;
        }

        /// <summary>An instance's name without the <c>(Clone)</c> Unity adds for each copy made from it.</summary>
        public static string PrefabNameOf(string name) {
            const string clone = "(Clone)";
            var end = name.Length;
            while (end >= clone.Length && string.CompareOrdinal(name, end - clone.Length, clone, 0, clone.Length) == 0)
                end -= clone.Length;
            return end == name.Length ? name : name.Substring(0, end).TrimEnd();
        }

        public void OnIdentityActivationChanged(WKTNetIdentity identity) {
            if (m_registering || !IsHost || identity.Handle == 0) return;
            ReportActivation(identity);
        }

        /// <summary>
        /// Host only: binds an object to clients again as it is now, for one the game finished setting up after it registered,
        /// such as Bazaar stock tagged for sale only once it was made.
        /// </summary>
        public void Rebind(WKTNetIdentity identity) {
            if (!IsHost || identity.Handle == 0) return;
            UpdateLevelKey(identity);
            WriteBind(identity);
            var writer = m_network.BeginMessage(WKTMessageId.EntityBind);
            m_bind.Write(writer);
            m_network.Broadcast(writer, SendType.Reliable);
        }

        /// <summary>
        /// Host only: tells clients when an object enters or leaves the world. Only its own state counts: a level being
        /// unloaded around it is not a change.
        /// </summary>
        private void ReportActivation(WKTNetIdentity identity) {
            var active = IsInWorld(identity);
            if (active == identity.ReportedActive) return;
            identity.ReportedActive = active;
            // Back in the world in another level, such as an item carried up and dropped: clients are told where it is now.
            // One whose copy went with the level it was in, unloaded behind them, would otherwise wait for that level forever
            // before making a new one.
            if (active && UpdateLevelKey(identity)) {
                WriteBind(identity);
                var bind = m_network.BeginMessage(WKTMessageId.EntityBind);
                m_bind.Write(bind);
                m_network.Broadcast(bind, SendType.Reliable);
                return;
            }
            var writer = m_network.BeginMessage(WKTMessageId.EntityActivation);
            WKTEntityEvent.Activation(identity.Handle, active).WriteActivation(writer);
            m_network.Broadcast(writer, SendType.Reliable);
        }

        /// <summary>
        /// Points <paramref name="identity"/> at the keyed level it is in now, <see cref="WKTPlayerRooms.CHAMBER"/> inside a
        /// chamber, or none. True if that changed.
        /// </summary>
        private static bool UpdateLevelKey(WKTNetIdentity identity) {
            var level = identity.GetComponentInParent<M_Level>(true);
            var levelKey = level != null && WKTContentSeeding.TryGetLevelKey(level, out var key) ? key : IsInChamber(identity.gameObject) ? WKTPlayerRooms.CHAMBER : 0UL;
            if (levelKey == identity.LevelKey) return false;
            identity.LevelKey = levelKey;
            return true;
        }

        public void OnIdentityDestroyed(WKTNetIdentity identity) {
            Sounds.Forget(identity);
            Events.Forget(identity);
            Presentation.Forget(identity);
            Viewers.Forget(identity);
            if (identity.Entity != null)
                s_puppetEntities.Remove(identity.Entity.GetInstanceID());
            if (identity.Handle != 0) {
                Authority.Forget(identity.Handle);
                SharedHolds.Forget(identity.Handle);
                m_buffers.Remove(identity.Handle);
            }
            if (IsHost) {
                if (identity.Handle == 0 || !m_byHandle.Remove(identity.Handle)) return;
                Recyclers.Forget(identity.Handle);
                m_sender.Forget(identity.Handle);
                Tongues.Forget(identity.Handle);
                EnemyHits.Forget(identity.Handle);
                var writer = m_network.BeginMessage(WKTMessageId.EntityDespawn);
                WKTEntityEvent.Despawn(identity.Handle).WriteHandle(writer);
                m_network.Broadcast(writer, SendType.Reliable);
                return;
            }
            if (identity.Key != 0)
                m_binder.OnLocalDestroyed(identity.Key, identity, Time.realtimeSinceStartupAsDouble);
        }

        /// <summary>Host only: tells a player who (re)connected about every world object.</summary>
        private void HandlePlayerConnected(WKTPlayer player) {
            if (!IsHost || player.IsHost) return;
            Perks.SendAllTo(player);
            Inventories.SendAllTo(player);
            foreach (var identity in m_byHandle.Values) {
                if (identity == null) continue;
                WriteBind(identity);
                var writer = m_network.BeginMessage(WKTMessageId.EntityBind);
                m_bind.Write(writer);
                m_network.SendToPlayer(player, writer, SendType.Reliable);
            }
            Authority.SendAll(player);
            Tongues.SendAll(player);
            Pitons.SendAll(player);
            ReturnRebars.SendAll(player);
            PlacedRopes.SendAll(player);
            Handholds.SendAll(player);
            Hazards.SendAll(player);
            Ladder.SendAll(player);
            Ropes.SendAll(player);
            EnemyShots.SendAll(player);
        }

        /// <summary>The object with a handle: the host's own, or the client's copy bound to it.</summary>
        public bool TryGetIdentity(ushort handle, out WKTNetIdentity? identity) {
            if (IsHost)
                return m_byHandle.TryGetValue(handle, out identity) && identity != null;
            return m_binder.TryGetObject(handle, out identity) && identity != null;
        }

        #endregion

        #region Seamless teleporters

        /// <summary>Whether this machine moves <paramref name="entity"/> itself: one it simulates, or one only it has.</summary>
        public bool SimulatesHere(GameEntity entity) =>
            !entity.TryGetComponent<WKTNetIdentity>(out var identity) || identity.Handle == 0 || Authority.SimulatesHere(identity);

        /// <summary>Client only: the local player went through a seamless teleporter, so the host moves its objects in the box along.</summary>
        public void OnLocalSeamlessTeleport(UT_SeamlessTeleporter teleporter) {
            if (IsHost || !WKTContentSeeding.TryGetKey(teleporter, out var key) || teleporter.GetComponentInParent<M_Level>() is not { } level ||
                !WKTContentSeeding.TryGetLevelKey(level, out var levelKey)) return;
            var writer = m_network.BeginMessage(WKTMessageId.SeamlessTeleport);
            writer.Put(levelKey);
            writer.Put(key);
            m_network.SendToHost(writer, SendType.Reliable);
        }

        private void OnSeamlessTeleport(WKTPlayer sender, NetDataReader reader) {
            if (IsHost && !sender.IsHost && reader.TryGetULong(out var levelKey) && reader.TryGetULong(out var key) &&
                WKTContentSeeding.TryFind(levelKey, key, s_teleporterScratch, out var teleporter) && teleporter!.destinationTransform != null)
                SeamlessTeleporter_Patch.Move(teleporter, this, true);
        }

        #endregion

        #region Damage and kills

        /// <summary>
        /// A hit on an object another machine simulates goes to that machine instead of being applied here: a client's goes
        /// through the host, the host's straight to the owning client. True if it was one, in which case the game's own damage
        /// must be skipped. The host also notes who hit an enemy it simulates, which may hand the enemy to them.
        /// </summary>
        public static bool TryForwardDamage(GameEntity entity, Damageable.DamageInfo? info) {
            var sync = Instance;
            if (sync == null || !entity.TryGetComponent<WKTNetIdentity>(out var identity) || identity.Handle == 0) return false;
            var byPlayer = sync.m_forwardDealer != WKTEntityAuthority.NOBODY ? sync.m_forwardByPlayer : IsLocalPlayer(info?.sourceEntity);
            if (sync.Authority.SimulatesHere(identity)) {
                if (sync.IsHost && byPlayer && sync.m_forwardDealer == WKTEntityAuthority.NOBODY && sync.m_network.LocalPlayer is { } host && !entity.dead)
                    sync.Authority.Engage(identity.Handle, host.Id, WKTEngageReason.Damage, Time.realtimeSinceStartupAsDouble);
                return false;
            }
            // A client's copy of a dead host object plays its death by itself, but hits on the body (which can gib it) are the
            // host's, so the body never goes on one machine only.
            var hostCorpse = !sync.IsHost && entity.dead && sync.m_forwardDealer == WKTEntityAuthority.NOBODY;
            if (!identity.IsPuppet && !hostCorpse) return false;
            if (info == null) return true;
            var dealer = sync.m_forwardDealer != WKTEntityAuthority.NOBODY ? sync.m_forwardDealer : sync.m_network.LocalPlayer?.Id ?? WKTEntityAuthority.NOBODY;
            var hit = ToWire(identity.Handle, info);
            if (!sync.IsHost) {
                sync.SendDamage(null, hit, dealer, byPlayer);
                return true;
            }
            var now = Time.realtimeSinceStartupAsDouble;
            if (byPlayer)
                sync.Authority.Engage(identity.Handle, dealer, WKTEngageReason.Damage, now);
            sync.RouteDamage(identity, hit, dealer, byPlayer);
            return true;
        }

        private static bool IsLocalPlayer(GameEntity? source) => source is ENT_Player player && player == ENT_Player.playerObject;

        /// <summary>Host only: a stalking creature hurt here turns on the player who hurt it (see <see cref="WKTProgressTargets"/>).</summary>
        public static void OnDamagedHere(GameEntity entity, Damageable.DamageInfo? info) {
            var sync = Instance;
            if (sync == null || !sync.IsHost || !WKTProgressTargets.Covers(entity)) return;
            var cause = CauseOf(info);
            if (cause == WKTDamageCause.NOBODY) return;
            WKTProgressTargets.OnDamaged(entity, cause == sync.m_network.LocalPlayer?.Id ? WKTTargets.LOCAL : cause);
        }

        /// <summary>
        /// The player whose hit <paramref name="info"/> is, for <see cref="WKTDamageCause"/>: the dealer of a hit another machine
        /// passed here, the local player, or the remote player a stand-in on the host stands for. Nobody for anything else.
        /// </summary>
        public static byte CauseOf(Damageable.DamageInfo? info) {
            var sync = Instance;
            if (sync == null) return WKTDamageCause.NOBODY;
            if (sync.m_forwardDealer != WKTEntityAuthority.NOBODY)
                return sync.m_forwardByPlayer ? sync.m_forwardDealer : WKTDamageCause.NOBODY;
            return info?.sourceEntity switch {
                ENT_Player player when player == ENT_Player.playerObject => sync.m_network.LocalPlayer?.Id ?? WKTDamageCause.NOBODY,
                WKTPlayerProxy proxy => proxy.PlayerId,
                _ => WKTDamageCause.NOBODY
            };
        }

        private static WKTEntityEvent ToWire(ushort handle, Damageable.DamageInfo info) =>
            WKTEntityEvent.Damage(handle, Mathf.Clamp(info.amount, 0f, WKTWire.MAX_AMOUNT), info.type, WKTWorldOffset.ToWire(info.position, WKTWorldOffset.Get()),
                info.force, info.direction, WKTEntityEvent.TagsToSend(info.tags, info.type));

        /// <summary>
        /// Sends a hit to <paramref name="player"/>, or to the host when null. The dealer is who hit it: the host replaces it
        /// with the sender for hits from clients.
        /// </summary>
        private void SendDamage(WKTPlayer? player, in WKTEntityEvent hit, byte dealer, bool byPlayer) {
            var writer = m_network.BeginMessage(WKTMessageId.DamageRequest);
            hit.WriteDamage(writer);
            writer.Put(dealer);
            writer.Put(byPlayer);
            if (player == null)
                m_network.SendToHost(writer, SendType.Reliable);
            else
                m_network.SendToPlayer(player, writer, SendType.Reliable);
        }

        /// <summary>Host only: applies a hit on an object here if the host simulates it, or hands it to the client that does.</summary>
        private void RouteDamage(WKTNetIdentity identity, in WKTEntityEvent hit, byte dealer, bool byPlayer) {
            var owner = identity.Owner;
            if (owner != WKTEntityAuthority.HOST) {
                if (m_network.TryGetPlayer(owner, out var player) && player != null)
                    SendDamage(player, hit, dealer, byPlayer);
                return;
            }
            var source = byPlayer && WKTPlayerProxy.TryGet(dealer, out var proxy) ? proxy : null;
            ApplyForwardedDamage(identity, hit, dealer, byPlayer, source);
        }

        /// <summary>Applies a hit another machine dealt. If this machine no longer simulates the object, it is sent on with the same dealer.</summary>
        private void ApplyForwardedDamage(WKTNetIdentity identity, in WKTEntityEvent hit, byte dealer, bool byPlayer, GameEntity? source) {
            // Dead bodies still take hits, as in the game, which can gib them.
            var entity = identity.Entity;
            if (entity == null || !entity.isActiveAndEnabled) return;
            var info = Damageable.DamageInfo.CreateDamageInfo(hit.Amount, hit.DamageType);
            hit.AddTagsTo(info.tags);
            // A hammer bounces hoppers up, dead or alive (DEN_Hopper.Damage). The sender's copy of a body plays its death by
            // itself and never bounces, so bouncing ours would only launch it while they keep hitting a body that stays put.
            if (entity.dead)
                while (info.tags.Remove(c_hammer_tag)) { }
            info.position = WKTWorldOffset.FromWire(hit.Position, WKTWorldOffset.Get());
            info.force = hit.Force;
            info.direction = hit.Direction;
            info.sourceEntity = source;
            m_forwardDealer = dealer;
            m_forwardByPlayer = byPlayer;
            try {
                entity.Damage(info);
            }
            finally {
                m_forwardDealer = WKTEntityAuthority.NOBODY;
                m_forwardByPlayer = false;
            }
        }

        /// <summary>
        /// A world object was killed here. The host tells every client, once per object. A client that simulates it claims the
        /// kill from the host, which decides it for everyone. Both say which player's hit killed it, whose machine credits the kill.
        /// </summary>
        public void OnLocalKill(GameEntity entity, string? type, Damageable.DamageInfo? info) {
            if (!entity.TryGetComponent<WKTNetIdentity>(out var identity) || identity.Handle == 0 || identity.KillSent) return;
            if (!IsHost && !Authority.IsOwnedHere(identity.Handle)) return;
            identity.KillSent = true;
            Authority.Forget(identity.Handle);
            SharedHolds.Forget(identity.Handle);
            var offset = WKTWorldOffset.Get();
            var position = info != null ? info.position : entity.transform.position;
            var writer = m_network.BeginMessage(WKTMessageId.EntityKill);
            WKTEntityEvent.Damage(identity.Handle, info?.amount ?? 0f, type, WKTWorldOffset.ToWire(position, offset),
                info?.force ?? Vector3.zero, info?.direction ?? Vector3.zero, WKTEntityEvent.TagsToSend(info?.tags, type)).WriteDamage(writer);
            writer.Put(m_claimedKiller != WKTDamageCause.NOBODY ? m_claimedKiller : CauseOf(info));
            if (IsHost)
                m_network.Broadcast(writer, SendType.Reliable);
            else
                m_network.SendToHost(writer, SendType.Reliable);
        }

        private void OnDamageRequest(WKTPlayer sender, NetDataReader reader) {
            if (!WKTEntityEvent.TryReadDamage(reader, out var hit) || !reader.TryGetByte(out var dealer) || !reader.TryGetBool(out var byPlayer)) return;
            if (!TryGetIdentity(hit.Handle, out var identity)) return;
            if (!IsHost) {
                // The host hands this client a hit on an object it simulates.
                if (sender.IsHost)
                    ApplyForwardedDamage(identity!, hit, dealer, byPlayer, null);
                return;
            }
            if (sender.IsHost) return;
            if (identity!.Entity == null) return;
            // A client deals its own hits, or passes on one it was handed after it stopped simulating the object.
            if (!m_network.TryGetPlayer(dealer, out _))
                dealer = sender.Id;
            if (byPlayer && !identity.Entity.dead)
                Authority.Engage(hit.Handle, dealer, WKTEngageReason.Damage, Time.realtimeSinceStartupAsDouble);
            RouteDamage(identity, hit, dealer, byPlayer);
        }

        /// <summary>
        /// A kill from the host, which every client applies, or on the host a kill claim from the client simulating the object,
        /// which the host applies and then tells everyone about. The killer's machine credits the kill as the game does for its
        /// own player's kills (<c>GameEntity.OnKilledByPlayer</c>: the run's kill score), unless it killed it there itself.
        /// </summary>
        private void OnEntityKill(WKTPlayer sender, NetDataReader reader) {
            if (!WKTEntityEvent.TryReadDamage(reader, out var kill) || !reader.TryGetByte(out var killer) || !TryGetIdentity(kill.Handle, out var identity) ||
                identity!.Entity == null) return;
            if (IsHost ? sender.IsHost || identity.Owner != sender.Id : !sender.IsHost) return;
            var entity = identity.Entity;
            if (entity.dead) return;
            var info = Damageable.DamageInfo.CreateDamageInfo(kill.Amount, kill.DamageType);
            kill.AddTagsTo(info.tags);
            info.position = WKTWorldOffset.FromWire(kill.Position, WKTWorldOffset.Get());
            info.force = kill.Force;
            info.direction = kill.Direction;
            Authority.Forget(kill.Handle);
            SharedHolds.Forget(kill.Handle);
            if (!IsHost)
                m_binder.MarkDead(kill.Handle);
            AllowHostKill = true;
            if (IsHost)
                m_claimedKiller = killer;
            try {
                entity.Kill(kill.DamageType, info);
            }
            finally {
                AllowHostKill = false;
                m_claimedKiller = WKTDamageCause.NOBODY;
            }
            if (killer == m_network.LocalPlayer?.Id)
                entity.OnKilledByPlayer(info);
            // The death plays out here, as the game runs it: animations, tongues pulling back, parts despawning. The host still
            // decides when the body goes away.
            ReleasePuppet(identity);
        }

        /// <summary>
        /// Runs a creature's own death on its puppet here, before the host decides it, for a death the local player set off
        /// (see <see cref="WKTTouchKills"/>). Its sounds and events play here only. If it died, the puppet is let go as
        /// <see cref="OnEntityKill"/> does, and the host's kill that follows finds it dead already. True if it died.
        /// </summary>
        public bool RunPredictedKill(WKTNetIdentity identity, Action<GameEntity> run) {
            var entity = identity.Entity;
            if (entity == null || entity.dead) return false;
            AllowHostKill = true;
            WKTLocalReplayScope.Enter();
            try {
                run(entity);
            }
            finally {
                AllowHostKill = false;
                WKTLocalReplayScope.Exit();
            }
            if (!entity.dead) return false;
            Authority.Forget(identity.Handle);
            SharedHolds.Forget(identity.Handle);
            if (!IsHost)
                m_binder.MarkDead(identity.Handle);
            ReleasePuppet(identity);
            return true;
        }

        /// <summary>
        /// An entity is about to gib here (<c>GameEntity.Gib</c>), which destroys it. The host tells every client, whose copies
        /// gib too; a client's own gib of a host object only marks it dead, so it is not replaced.
        /// </summary>
        public void OnLocalGib(GameEntity entity) {
            if (!entity.TryGetComponent<WKTNetIdentity>(out var identity) || identity.Handle == 0) return;
            if (!IsHost) {
                m_binder.MarkDead(identity.Handle);
                return;
            }
            var writer = m_network.BeginMessage(WKTMessageId.EntityGib);
            writer.Put(identity.Handle);
            m_network.Broadcast(writer, SendType.Reliable);
        }

        private void OnEntityGib(WKTPlayer sender, NetDataReader reader) {
            if (IsHost || !sender.IsHost || !WKTEntityEvent.TryReadHandle(reader, out var gib)) return;
            m_binder.MarkDead(gib.Handle);
            if (!TryGetIdentity(gib.Handle, out var identity) || identity!.Entity == null) return;
            AllowHostKill = true;
            try {
                identity.Entity.Gib();
            }
            finally {
                AllowHostKill = false;
            }
        }

        /// <summary>
        /// An enemy hit a player on that player's own screen, which may change what the enemy does. The machine simulating the
        /// enemy applies it; the host passes on clients' reports to the owning client.
        /// </summary>
        private void OnPlayerHit(WKTPlayer sender, NetDataReader reader) {
            if (!WKTPlayerHit.TryRead(reader, out var hit) || !TryGetIdentity(hit.Handle, out var identity) || identity!.Entity == null) return;
            if (IsHost && !sender.IsHost && hit.Kind == WKTPlayerHitKind.Grabbed) {
                switch (identity.Entity) {
                    case DEN_Remains_Active:
                        Viewers.OnRemainsClaim(sender, identity);
                        return;
                    case DEN_Teeth:
                        Viewers.TeethKills.OnClaim(sender, identity);
                        return;
                }
            }
            if (hit.Kind == WKTPlayerHitKind.Triggered) {
                if (IsHost && !sender.IsHost)
                    TouchKills.OnClaim(sender, identity);
                else if (!IsHost && sender.IsHost)
                    TouchKills.OnRemoteSquish(identity);
                return;
            }
            if (!IsHost) {
                if (sender.IsHost && Authority.IsOwnedHere(hit.Handle))
                    WKTEnemyHits.OnPlayerHit(identity.Entity, hit);
                return;
            }
            if (sender.IsHost) return;
            if (identity.Entity is DEN_Barnacle barnacle) {
                Tongues.OnPlayerHit(sender, barnacle, hit);
                return;
            }
            ApplyPlayerHit(identity, hit);
        }

        /// <summary>Host only: an enemy's hit on a player reaches the machine simulating the enemy.</summary>
        public void ApplyPlayerHit(WKTNetIdentity identity, in WKTPlayerHit hit) {
            if (identity.Entity == null) return;
            if (identity.Owner == WKTEntityAuthority.HOST) {
                WKTEnemyHits.OnPlayerHit(identity.Entity, hit);
                return;
            }
            if (!m_network.TryGetPlayer(identity.Owner, out var owner) || owner == null) return;
            var writer = m_network.BeginMessage(WKTMessageId.PlayerHit);
            hit.Write(writer);
            m_network.SendToPlayer(owner, writer, SendType.Reliable);
        }

        #endregion

        #region Client binding

        private void OnEntityBind(WKTPlayer sender, NetDataReader reader) {
            if (IsHost || !sender.IsHost) return;
            if (!WKTEntityBind.TryRead(reader, m_bind)) {
                WKT.Instance.Logger.LogError("Received a malformed EntityBind.");
                return;
            }
            m_binder.OnHostBind(m_bind, Time.realtimeSinceStartupAsDouble);
            if (!m_claims.TryTakeBeforeBind(m_bind.Key, out var item)) return;
            // The local player already holds it: the host's copy is never shown here, and the host is asked for it.
            m_binder.Release(m_bind.Handle);
            RequestPickup(m_bind.Handle, item);
        }

        private void OnEntityDespawn(WKTPlayer sender, NetDataReader reader) {
            if (IsHost || !sender.IsHost || !WKTEntityEvent.TryReadHandle(reader, out var despawn)) return;
            m_buffers.Remove(despawn.Handle);
            Authority.Forget(despawn.Handle);
            Tongues.Forget(despawn.Handle);
            EnemyHits.Forget(despawn.Handle);
            m_binder.OnHostDespawn(despawn.Handle, Time.realtimeSinceStartupAsDouble);
        }

        private void OnEntityActivation(WKTPlayer sender, NetDataReader reader) {
            if (IsHost || !sender.IsHost || !WKTEntityEvent.TryReadActivation(reader, out var activation)) return;
            if (m_binder.TryGetBind(activation.Handle, out var bind))
                bind!.Flags = activation.Active ? (byte)(bind.Flags | WKTEntityBind.FLAG_ACTIVE) : (byte)(bind.Flags & ~WKTEntityBind.FLAG_ACTIVE);
            if (m_binder.TryGetObject(activation.Handle, out var identity) && identity != null)
                identity.gameObject.SetActive(activation.Active);
        }

        public void Bind(WKTNetIdentity identity, WKTEntityBind bind) {
            identity.Handle = bind.Handle;
            identity.Key = bind.Key;
            NameHandholds(identity);
            identity.IsLocalDrop = false;
            MakePuppet(identity);
            EnemyHits.OnPuppetBound(identity);
            TouchKills.OnPuppetBound(identity);
            identity.transform.SetPositionAndRotation(WKTWorldOffset.FromWire(bind.Position, WKTWorldOffset.Get()), bind.Rotation);
            if (identity.gameObject.activeSelf != bind.IsActive)
                identity.gameObject.SetActive(bind.IsActive);
            ApplyItemData(identity, bind);
            ClearBuffer(bind.Handle);
            Authority.OnBound(identity);
        }

        public WKTNetIdentity? Spawn(WKTEntityBind bind) {
            var prefab = GetPrefab(bind.Kind, bind.PrefabId);
            if (prefab == null) return null;
            Transform? parent = null;
            // The host's chamber objects go into this machine's own copy of the chamber, which is only spawned into once inside.
            if (bind.LevelKey == WKTPlayerRooms.CHAMBER)
                parent = WorldLoader.GetCurrentLevelParentRoot();
            else if (bind.LevelKey != 0 && WKTContentSeeding.TryGetLevel(bind.LevelKey, out var level))
                parent = level!.transform;
            var position = WKTWorldOffset.FromWire(bind.Position, WKTWorldOffset.Get());
            m_spawningKey = bind.Key;
            GameObject instance;
            try {
                instance = Instantiate(prefab, position, bind.Rotation, parent);
            }
            finally {
                m_spawningKey = 0;
            }
            if (!instance.TryGetComponent<WKTNetIdentity>(out var identity)) {
                // Prefabs whose components did not register while spawning, such as items that only register on Start.
                m_spawningKey = bind.Key;
                var owner = (Component?)instance.GetComponent<Item_Object>() ?? instance.GetComponent<GameEntity>();
                if (owner != null)
                    Register(owner);
                m_spawningKey = 0;
                if (!instance.TryGetComponent(out identity)) {
                    Destroy(instance);
                    return null;
                }
            }
            // Dropped items always end at their full size in the world, whatever scale the prefab was saved with.
            if (identity!.Kind == WKTNetKind.Item && (bind.Key & c_runtime_bit) != 0)
                identity.transform.localScale = Vector3.one;
            ApplyItemData(identity, bind);
            return identity;
        }

        /// <summary>Puts the host's item data (and the modules it packs, such as being for sale) on an item here.</summary>
        private static void ApplyItemData(WKTNetIdentity identity, WKTEntityBind bind) {
            if (identity.Item == null || identity.Item.itemData == null || bind.ItemData.Count == 0) return;
            identity.Item.itemData.data ??= new List<string>();
            identity.Item.itemData.data.Clear();
            identity.Item.itemData.data.AddRange(bind.ItemData);
            WKTItemModules.Unpack(identity.Item.itemData, identity.Item);
        }

        /// <summary>The asset an object of <paramref name="kind"/> is spawned from by its id, or null (logged once) if there is none.</summary>
        public static GameObject? GetPrefab(WKTNetKind kind, string prefabId) {
            if (string.IsNullOrEmpty(prefabId)) {
                LogMissingPrefab($"(none, {kind})");
                return null;
            }
            GameObject? prefab = null;
            try {
                prefab = kind == WKTNetKind.Item ? CL_AssetManager.GetItemObjectPrefab(prefabId)?.gameObject : null;
                if (prefab == null)
                    prefab = CL_AssetManager.GetAssetGameObject(prefabId);
            }
            catch (Exception) {
                // The asset manager logs and throws for unknown ids.
            }
            if (prefab == null && kind != WKTNetKind.Item)
                prefab = FindLoadedPrefab(prefabId);
            if (prefab == null)
                LogMissingPrefab(prefabId);
            return prefab;
        }

        /// <summary>
        /// A prefab no asset database lists, by name, among the ones in memory: a level holding a spawner for it (drone pods,
        /// object spawners) has it loaded here too. Searched once per name.
        /// </summary>
        private static GameObject? FindLoadedPrefab(string name) {
            if (s_loadedPrefabs.TryGetValue(name, out var known) && known != null) return known;
            if (s_missingPrefabs.Contains(name)) return null;
            foreach (var entity in Resources.FindObjectsOfTypeAll<GameEntity>()) {
                var candidate = entity.gameObject;
                // Prefab assets belong to no scene.
                if (candidate.scene.IsValid() || candidate.transform.parent != null || candidate.name != name) continue;
                s_loadedPrefabs[name] = candidate;
                return candidate;
            }
            return null;
        }

        private static void LogMissingPrefab(string prefabId) {
            if (s_missingPrefabs.Add(prefabId))
                WKT.Instance.Logger.LogError($"Cannot spawn {prefabId}: no such asset. It stays missing on this machine.");
        }

        public void Hide(WKTNetIdentity identity) => m_hideQueue.Add(identity);

        public void Destroy(WKTNetIdentity identity) {
            if (identity != null)
                Object.Destroy(identity.gameObject);
        }

        public bool IsRuntimeOnly(WKTEntityBind bind) => (bind.Key & c_runtime_bit) != 0;

        /// <summary>Whether a level is active here, or for <see cref="WKTPlayerRooms.CHAMBER"/> whether the local player is in a chamber.</summary>
        public bool IsLevelActive(ulong levelKey) => WKTPlayerRooms.IsLoadedHere(levelKey);

        /// <summary>Whether an object sits in a chamber, such as the Bazaar: outside the levels, shown only to those inside one.</summary>
        private static bool IsInChamber(GameObject gameObject) => gameObject.GetComponentInParent<M_Chamber>(true) != null;

        /// <summary>Stops this machine simulating <paramref name="identity"/>: another machine's states drive it from now on.</summary>
        public static void MakePuppet(WKTNetIdentity identity) {
            if (identity.IsPuppet) return;
            WKTBearTraps.OnStopSimulating(identity);
            identity.IsPuppet = true;
            if (identity.Entity != null) {
                s_puppetEntities.Add(identity.Entity.GetInstanceID());
                StopAttacks(identity.Entity);
            }
            // A creature's prop half ticks like any puppet's: the machine simulating the creature runs it.
            if (identity.Prop != null && identity.Prop != identity.Entity)
                s_puppetEntities.Add(identity.Prop.GetInstanceID());
            if (identity.Body == null) return;
            identity.BodyWasKinematic = identity.Body.isKinematic;
            identity.Body.isKinematic = true;
        }

        /// <summary>
        /// Ends an attack under way when another machine takes the creature over, so its coroutines (a burst of shots, a lunge)
        /// do not go on here unseen by anyone else. The new owner's own AI attacks again.
        /// </summary>
        private static void StopAttacks(GameEntity entity) {
            if (WKTAttackModules.GetField(entity.GetType())?.GetValue(entity) is not DenizenAttackModule module) return;
            try {
                module.StopAllAttacks();
            }
            catch (Exception exception) {
                WKT.Instance.Logger.LogWarning($"Could not stop {entity.name}'s attacks on handover: {exception.Message}");
            }
        }

        /// <summary>Lets this machine simulate <paramref name="identity"/> again.</summary>
        public static void ReleasePuppet(WKTNetIdentity identity) {
            if (!identity.IsPuppet) return;
            identity.IsPuppet = false;
            if (identity.Entity != null)
                s_puppetEntities.Remove(identity.Entity.GetInstanceID());
            if (identity.Prop != null)
                s_puppetEntities.Remove(identity.Prop.GetInstanceID());
            if (identity.Body != null)
                identity.Body.isKinematic = identity.BodyWasKinematic;
        }

        /// <summary>Forgets an object's buffered poses, as when the machine sending them changes.</summary>
        public void ClearBuffer(ushort handle) {
            if (m_buffers.TryGetValue(handle, out var buffer))
                buffer.Clear();
        }

        /// <summary>
        /// Starts an object's buffered poses over for a new sender from where it is shown now, which its first state from that
        /// sender is blended out of rather than jumped from.
        /// </summary>
        public void RestartBuffer(WKTNetIdentity identity) {
            if (!m_buffers.TryGetValue(identity.Handle, out var buffer)) {
                buffer = new WKTEntitySnapshotBuffer();
                m_buffers.Add(identity.Handle, buffer);
            }
            var objectTransform = identity.transform;
            buffer.Restart(new WKTEntityPose(WKTWorldOffset.ToWire(objectTransform.position, WKTWorldOffset.Get()), objectTransform.rotation));
        }

        #endregion

        #region State

        private void SendStates(ushort sequence, double now) {
            var offset = WKTWorldOffset.Get();
            m_sender.BeginTick();
            foreach (var identity in m_byHandle.Values) {
                if (identity == null) continue;
                // Polled too, since a held item changes layer and a bagged one moves without being disabled.
                ReportActivation(identity);
                if (!identity.ReportedActive || !identity.gameObject.activeInHierarchy) continue;
                var objectTransform = identity.transform;
                var pose = new WKTEntityPose(WKTWorldOffset.ToWire(objectTransform.position, offset), objectTransform.rotation);
                var entity = identity.Entity;
                var aiBits = WKTEnemyHits.GetAiBits(entity);
                var owner = identity.Owner;
                // A client simulates this one: everyone else gets its newest state as it sent it, not the host's puppet of it.
                if (owner != WKTEntityAuthority.HOST && Authority.TryGetLatest(identity.Handle, out var latest)) {
                    if (latest.HasPose)
                        pose = latest.Pose;
                    aiBits = latest.AiBits;
                }
                var animatorCount = ReadAnimator(identity.Animator, m_animatorValues);
                var stateCount = ReadAnimatorStates(identity.Animator, m_animatorStates);
                m_sender.Submit(identity.Handle, pose, entity != null ? entity.health : 0f, entity != null && entity.dead,
                    m_animatorValues.AsSpan(0, animatorCount), now, m_animatorStates.AsSpan(0, stateCount), aiBits, skipFor: owner,
                    farVisible: entity != null && WKTAmbientMotion.IsFarVisible(entity.GetType()));
                if (entity is DEN_Barnacle barnacle)
                    Tongues.HostPoll(identity.Handle, barnacle);
            }
            if (m_sender.PendingCount == 0) return;
            for (var i = 0; i < WKTPlayerPositions.RemoteCount; i++) {
                if (!m_network.TryGetPlayer(WKTPlayerPositions.GetRemoteId(i), out var player) || player == null) continue;
                m_stateTarget = player;
                m_sender.Flush(WKTWorldOffset.ToWire(WKTPlayerPositions.GetRemotePosition(i), offset), sequence, m_beginStatePacket, m_sendStatePacket, player.Id);
            }
            m_stateTarget = null;
        }

        /// <summary>The animator's float, int and bool parameters in their declared order, which every copy of the prefab shares.</summary>
        public static int ReadAnimator(Animator? animator, float[] values) {
            if (animator == null || !animator.isActiveAndEnabled || animator.runtimeAnimatorController == null) return 0;
            var count = Math.Min(animator.parameterCount, WKTEntityState.MAX_ANIMATOR_VALUES);
            var written = 0;
            for (var i = 0; i < count; i++) {
                var parameter = animator.GetParameter(i);
                values[written++] = parameter.type switch {
                    AnimatorControllerParameterType.Float => animator.GetFloat(parameter.nameHash),
                    AnimatorControllerParameterType.Int => animator.GetInteger(parameter.nameHash),
                    AnimatorControllerParameterType.Bool => animator.GetBool(parameter.nameHash) ? 1f : 0f,
                    _ => 0f
                };
            }
            return written;
        }

        /// <summary>
        /// The state each animator layer is in, or heading to while it blends. Carries what parameters cannot, such as the
        /// animations one-shot triggers start.
        /// </summary>
        public static int ReadAnimatorStates(Animator? animator, int[] states) {
            if (animator == null || !animator.isActiveAndEnabled || animator.runtimeAnimatorController == null) return 0;
            var count = Math.Min(animator.layerCount, WKTEntityState.MAX_ANIMATOR_LAYERS);
            for (var layer = 0; layer < count; layer++) {
                states[layer] = animator.IsInTransition(layer)
                    ? animator.GetNextAnimatorStateInfo(layer).fullPathHash
                    : animator.GetCurrentAnimatorStateInfo(layer).fullPathHash;
            }
            return count;
        }

        /// <summary>Blends a puppet's animator into the host's states where it is not already in or heading to them.</summary>
        private static void WriteAnimatorStates(Animator? animator, ReadOnlySpan<int> states) {
            if (animator == null || !animator.isActiveAndEnabled || animator.runtimeAnimatorController == null) return;
            var count = Math.Min(animator.layerCount, states.Length);
            for (var layer = 0; layer < count; layer++) {
                var hash = states[layer];
                if (hash == 0 || animator.GetCurrentAnimatorStateInfo(layer).fullPathHash == hash) continue;
                if (animator.IsInTransition(layer) && animator.GetNextAnimatorStateInfo(layer).fullPathHash == hash) continue;
                if (animator.HasState(layer, hash))
                    animator.CrossFadeInFixedTime(hash, c_state_blend, layer);
            }
        }

        private static void WriteAnimator(Animator? animator, ReadOnlySpan<float> values) {
            if (animator == null || !animator.isActiveAndEnabled || animator.runtimeAnimatorController == null) return;
            var count = Math.Min(animator.parameterCount, values.Length);
            for (var i = 0; i < count; i++) {
                var parameter = animator.GetParameter(i);
                switch (parameter.type) {
                    case AnimatorControllerParameterType.Float:
                        animator.SetFloat(parameter.nameHash, values[i]);
                        break;
                    case AnimatorControllerParameterType.Int:
                        animator.SetInteger(parameter.nameHash, Mathf.RoundToInt(values[i]));
                        break;
                    case AnimatorControllerParameterType.Bool:
                        animator.SetBool(parameter.nameHash, values[i] > 0.5f);
                        break;
                }
            }
        }

        private void OnEntityStates(WKTPlayer sender, NetDataReader reader) {
            if (IsHost || !sender.IsHost || !WKTEntityState.TryReadHeader(reader, out var sequence, out var count)) return;
            var now = Time.realtimeSinceStartupAsDouble;
            for (var i = 0; i < count; i++) {
                if (!WKTEntityState.TryRead(reader, out var state, m_animatorValues, m_animatorStates)) return;
                // This client simulates the ones it owns itself.
                if (!m_binder.TryGetObject(state.Handle, out var identity) || identity == null || Authority.IsOwnedHere(state.Handle)) continue;
                if (state.Has(WKTEntityState.FLAG_DEAD))
                    m_binder.MarkDead(state.Handle);
                ApplyRemoteState(identity, sequence, state, m_animatorValues.AsSpan(0, state.Has(WKTEntityState.FLAG_ANIMATOR) ? state.AnimatorCount : 0),
                    m_animatorStates.AsSpan(0, state.Has(WKTEntityState.FLAG_ANIMATOR_STATES) ? state.StateCount : 0), now);
            }
        }

        /// <summary>Shows another machine's state of an object on its puppet here: its pose is buffered, the rest applied at once.</summary>
        public void ApplyRemoteState(WKTNetIdentity identity, ushort sequence, in WKTEntityState state, ReadOnlySpan<float> animatorValues,
                                     ReadOnlySpan<int> animatorStates, double now) {
            if (state.Has(WKTEntityState.FLAG_POSE)) {
                if (!m_buffers.TryGetValue(state.Handle, out var buffer)) {
                    buffer = new WKTEntitySnapshotBuffer();
                    m_buffers.Add(state.Handle, buffer);
                }
                buffer.Add(sequence, state.Pose, now);
            }
            if (state.Has(WKTEntityState.FLAG_HEALTH) && identity.Entity != null)
                identity.Entity.health = state.Health;
            if (state.Has(WKTEntityState.FLAG_ANIMATOR))
                WriteAnimator(identity.Animator, animatorValues);
            if (state.Has(WKTEntityState.FLAG_ANIMATOR_STATES))
                WriteAnimatorStates(identity.Animator, animatorStates);
            if (state.Has(WKTEntityState.FLAG_AI))
                EnemyHits.OnAiBits(state.Handle, state.AiBits);
        }

        private void ApplyPuppetPoses(double now) {
            if (m_buffers.Count == 0) return;
            var offset = WKTWorldOffset.Get();
            m_handleScratch.Clear();
            foreach (var pair in m_buffers) {
                if (!TryGetIdentity(pair.Key, out var identity)) {
                    m_handleScratch.Add(pair.Key);
                    continue;
                }
                // What still hangs on the Hadal Diver rides this machine's own diver, built alike everywhere but at its own
                // point in the dive (see WKTDiverExit): the host's pose of it is on the host's diver.
                if (!identity!.IsPuppet || identity.SelfDriven || identity.NestedIn != null || !identity.gameObject.activeInHierarchy ||
                    WKTDiverExit.IsPartOfDiver(identity) || !pair.Value.TrySample(now, out var pose)) continue;
                identity.transform.SetPositionAndRotation(WKTWorldOffset.FromWire(pose.Position, offset), pose.Rotation);
            }
            foreach (var handle in m_handleScratch)
                m_buffers.Remove(handle);
        }

        #endregion

        #region Items

        /// <summary>
        /// Client only: the local player is taking an item into a hand. If it is a host object, the client keeps its copy
        /// and asks the host for it; the host's copy goes away for everyone, or the client loses the item if someone was first.
        /// </summary>
        public void OnLocalPickup(Item item) {
            var dropObject = item.GetDropObject(false);
            if (dropObject == null || !dropObject.TryGetComponent<WKTNetIdentity>(out var identity)) return;
            var handle = identity.Handle;
            if (IsHost) {
                // An item that is also a creature (a ruby roach) may be simulated by a client: the host's bag holds it now.
                if (handle != 0 && identity.Owner != WKTEntityAuthority.HOST)
                    Authority.Reclaim(handle);
                return;
            }
            if (handle == 0) {
                OnPickupBeforeBind(item, identity);
                return;
            }
            m_binder.Release(handle);
            m_buffers.Remove(handle);
            Detach(identity);
            RequestPickup(handle, item);
        }

        /// <summary>
        /// Client only: the local player took an item the host has not bound yet. A drop the host never heard of is simply
        /// cancelled; otherwise the item is asked for once its bind arrives.
        /// </summary>
        private void OnPickupBeforeBind(Item item, WKTNetIdentity identity) {
            var key = identity.Key;
            if (key == 0) return;
            if (!CancelPendingDrop(identity))
                m_claims.AddBeforeBind(key, item, Time.realtimeSinceStartupAsDouble);
            m_binder.OnLocalDestroyed(key, identity, Time.realtimeSinceStartupAsDouble);
            Detach(identity);
        }

        /// <summary>Removes a drop that was not sent yet. True if there was one.</summary>
        /// <summary>
        /// Client only: the local player used up one of the host's world items here, such as in an amalgamator's craft. It is
        /// let go so its copy is not brought back, and the host removes its own.
        /// </summary>
        public void OnLocalConsume(Item item) {
            if (IsHost || item.hasBeenDestroyed) return;
            var dropObject = item.GetDropObject(false);
            if (dropObject == null || !dropObject.TryGetComponent<WKTNetIdentity>(out var identity) || identity.Handle == 0) return;
            var handle = identity.Handle;
            m_binder.Release(handle);
            m_buffers.Remove(handle);
            var writer = m_network.BeginMessage(WKTMessageId.ItemConsume);
            WKTEntityEvent.Despawn(handle).WriteHandle(writer);
            m_network.SendToHost(writer, SendType.Reliable);
        }

        private void OnItemConsume(WKTPlayer sender, NetDataReader reader) {
            if (!IsHost || sender.IsHost || !WKTEntityEvent.TryReadHandle(reader, out var request)) return;
            if (!m_byHandle.TryGetValue(request.Handle, out var identity) || identity == null || identity.Kind != WKTNetKind.Item || !IsInWorld(identity)) return;
            var item = identity.Item;
            if (item != null && item.itemData != null)
                item.itemData.Destroy();
            else
                Object.Destroy(identity.gameObject);
        }

        /// <summary>
        /// The local player paid for a Bazaar item, which the game marks paid on this machine only: the host's copy is paid too
        /// and bound again, so it is no longer for sale anywhere.
        /// </summary>
        public void OnLocalPaid(Item_Object item) {
            if (!item.TryGetComponent<WKTNetIdentity>(out var identity) || identity.Handle == 0) return;
            if (IsHost) {
                Rebind(identity);
                return;
            }
            var writer = m_network.BeginMessage(WKTMessageId.ItemPaid);
            WKTEntityEvent.Despawn(identity.Handle).WriteHandle(writer);
            m_network.SendToHost(writer, SendType.Reliable);
        }

        private void OnItemPaid(WKTPlayer sender, NetDataReader reader) {
            if (!IsHost || sender.IsHost || !WKTEntityEvent.TryReadHandle(reader, out var request)) return;
            // Only stock still for sale, paid for by someone in a chamber, as only they can trade for it.
            if (!m_byHandle.TryGetValue(request.Handle, out var identity) || identity == null || identity.Item is not { itemData: { } item } ||
                !IsInWorld(identity) || !item.HasTag(WKTItemModules.UNPURCHASED) || WKTPlayerRooms.Get(sender.Id) != WKTPlayerRooms.CHAMBER) return;
            WKTItemModules.MarkPaid(item, identity.Item);
            Rebind(identity);
        }

        /// <summary>Client only: an item just dropped here is used up before the host heard of it, so it is never sent.</summary>
        public void CancelLocalDrop(Item item) {
            var dropObject = item.GetDropObject(false);
            if (!IsHost && dropObject != null && dropObject.TryGetComponent<WKTNetIdentity>(out var identity))
                CancelPendingDrop(identity);
        }

        private bool CancelPendingDrop(WKTNetIdentity identity) {
            for (var i = 0; i < m_pendingDrops.Count; i++) {
                if (m_pendingDrops[i].Identity != identity) continue;
                m_pendingDrops.RemoveAt(i);
                return true;
            }
            return false;
        }

        private void RequestPickup(ushort handle, Item item) {
            m_claims.AddPending(handle, item);
            var writer = m_network.BeginMessage(WKTMessageId.PickupRequest);
            WKTEntityEvent.Despawn(handle).WriteHandle(writer);
            m_network.SendToHost(writer, SendType.Reliable);
        }

        /// <summary>Client only: the local player dropped or threw an item. Asked for once its throw velocity is known.</summary>
        public void OnLocalDrop(Item item) {
            if (IsHost) return;
            var dropObject = item.GetDropObject(false);
            if (dropObject == null || string.IsNullOrEmpty(item.prefabName)) return;
            var localPlayer = m_network.LocalPlayer;
            if (localPlayer == null) return;
            var token = ++m_dropCount;
            var key = GetDropKey(localPlayer.Id, token);
            // Registered now, before the item's own Start would hide it as an object the host never claimed.
            var identity = AddIdentity(dropObject, key);
            identity.IsLocalDrop = true;
            m_binder.Expect(key, identity);
            m_pendingDrops.Add(new PendingDrop(item, identity, token));
        }

        /// <summary>
        /// Client only: the local player's own action spawned an item into the world, such as buying from a vending machine.
        /// The host makes it a world object everyone sees, as if the client had dropped it.
        /// </summary>
        public void OnLocalSpawnedItem(Item_Object itemObject) {
            if (IsHost || itemObject.itemData == null) return;
            itemObject.itemData.prefabName ??= string.Empty;
            var localPlayer = m_network.LocalPlayer;
            if (localPlayer == null || itemObject.itemData.prefabName.Length == 0) return;
            var token = ++m_dropCount;
            var key = GetDropKey(localPlayer.Id, token);
            var identity = AddIdentity(itemObject, key);
            identity.IsLocalDrop = true;
            m_binder.Expect(key, identity);
            m_pendingDrops.Add(new PendingDrop(itemObject.itemData, identity, token));
        }

        /// <summary>
        /// Opens a scope around the local player's own action, such as a thrown rebar sticking in a wall or a piton falling
        /// out. Items it spawns into the world are handed to the host like drops instead of being hidden. Returns whether a
        /// scope was opened, to pass to <see cref="EndLocalSpawn"/>.
        /// </summary>
        public static bool BeginLocalSpawn() {
            var sync = Instance;
            if (sync == null || sync.IsHost) return false;
            s_localSpawns.Enter();
            return true;
        }

        /// <summary>Remembers an object spawned while a local spawn scope is open.</summary>
        public static void RecordLocalSpawn(Object? spawned) => s_localSpawns.Record(spawned);

        /// <summary>Closes a scope opened by <see cref="BeginLocalSpawn"/>; the outermost one hands its items to the host.</summary>
        public static void EndLocalSpawn(bool began) {
            if (!began || !s_localSpawns.Exit()) return;
            s_localSpawnScratch.Clear();
            s_localSpawns.Drain(s_localSpawnScratch);
            var sync = Instance;
            if (sync != null) {
                foreach (var spawned in s_localSpawnScratch)
                    sync.ClaimLocalSpawn(spawned);
            }
            s_localSpawnScratch.Clear();
        }

        private void ClaimLocalSpawn(Object spawned) {
            if (spawned == null) return;
            var spawnedObject = spawned switch {
                GameObject spawnedGameObject => spawnedGameObject,
                Component component => component.gameObject,
                _ => null
            };
            if (spawnedObject == null || !spawnedObject.activeSelf || IsInInventory(spawnedObject)) return;
            if (!spawnedObject.TryGetComponent<Item_Object>(out var itemObject)) {
                // Only items can be handed over. Anything else stays the client's own, and is hidden like any such object.
                if (s_unclaimableSpawns.Add(spawnedObject.name))
                    WKT.Instance.Logger.LogWarning($"The local player spawned {spawnedObject.name}, which is not an item. Only this player sees it.");
                return;
            }
            if (!IsReplicated(itemObject)) return;
            if (itemObject.TryGetComponent<WKTNetIdentity>(out var identity) && (identity.Handle != 0 || identity.IsLocalDrop)) return;
            if (itemObject.itemData != null && string.IsNullOrEmpty(itemObject.itemData.prefabName))
                itemObject.itemData.prefabName = WKTPrefabSearch.GetPrefabName(itemObject.name);
            OnLocalSpawnedItem(itemObject);
        }

        /// <summary>The key of the item a client drops: the host spawns it under this key, so the client's copy binds to it.</summary>
        public static ulong GetDropKey(byte playerId, uint token) =>
            c_runtime_bit | WKTStableHash.Mix(WKTStableHash.Mix(WKTStableHash.Of(c_drop_salt), playerId), token);

        /// <summary>
        /// Sends the drops of this frame. A drop of an item the host has yet to give the local player waits for its answer
        /// (see <see cref="WKTItemClaims{TItem}"/>).
        /// </summary>
        private void SendPendingDrops() {
            if (m_pendingDrops.Count == 0) return;
            var offset = WKTWorldOffset.Get();
            var kept = 0;
            for (var i = 0; i < m_pendingDrops.Count; i++) {
                var drop = m_pendingDrops[i];
                if (drop.Identity == null) continue;
                if (m_claims.IsUnresolved(drop.Item)) {
                    m_pendingDrops[kept++] = drop;
                    continue;
                }
                var dropTransform = drop.Identity.transform;
                m_dropRequest.Token = drop.Token;
                m_dropRequest.PrefabId = drop.Item.prefabName;
                m_dropRequest.Position = WKTWorldOffset.ToWire(dropTransform.position, offset);
                m_dropRequest.Rotation = dropTransform.rotation;
                var body = drop.Identity.Body;
                var kinematic = body != null && body.isKinematic;
                m_dropRequest.Velocity = body != null && !kinematic ? Vector3.ClampMagnitude(body.linearVelocity, WKTWire.MAX_SPEED) : Vector3.zero;
                m_dropRequest.Flags = (byte)((kinematic ? WKTDropRequest.FLAG_KINEMATIC : 0) | (drop.Item.used ? WKTDropRequest.FLAG_USED : 0));
                m_dropRequest.ItemData.Clear();
                WKTItemModules.Pack(drop.Item);
                if (drop.Item.data != null)
                    m_dropRequest.ItemData.AddRange(drop.Item.data);
                var writer = m_network.BeginMessage(WKTMessageId.DropRequest);
                m_dropRequest.Write(writer);
                m_network.SendToHost(writer, SendType.Reliable);
            }
            m_pendingDrops.RemoveRange(kept, m_pendingDrops.Count - kept);
        }

        private void OnPickupRequest(WKTPlayer sender, NetDataReader reader) {
            if (!IsHost || sender.IsHost || !WKTEntityEvent.TryReadHandle(reader, out var request)) return;
            // A chamber's items can only be taken by someone inside one: the host's switched-off chamber stays where it was.
            var accepted = m_byHandle.TryGetValue(request.Handle, out var identity) && identity != null && identity.Kind == WKTNetKind.Item &&
                IsInWorld(identity) && IsNear(sender.Id, identity.transform.position, c_pickup_range) &&
                (identity.LevelKey != WKTPlayerRooms.CHAMBER || WKTPlayerRooms.Get(sender.Id) == WKTPlayerRooms.CHAMBER);
            if (accepted) {
                var item = identity!.Item;
                // A shared artifact altar's pick is the client's: the altar takes back the other artifacts, as for the host.
                if (item != null && item.GetComponentInParent<ENV_ArtifactDevice>() != null)
                    item.onFirstPickup?.Invoke(item);
                if (item != null && item.itemData != null)
                    item.itemData.Destroy();
                else
                    Object.Destroy(identity.gameObject);
            }
            var writer = m_network.BeginMessage(WKTMessageId.PickupResult);
            WKTEntityEvent.Activation(request.Handle, accepted).WriteActivation(writer);
            m_network.SendToPlayer(sender, writer, SendType.Reliable);
            if (accepted || identity == null) return;
            // The client already let go of its copy: it gets the object back from a fresh bind.
            WriteBind(identity);
            writer = m_network.BeginMessage(WKTMessageId.EntityBind);
            m_bind.Write(writer);
            m_network.SendToPlayer(sender, writer, SendType.Reliable);
        }

        private void OnPickupResult(WKTPlayer sender, NetDataReader reader) {
            if (IsHost || !sender.IsHost || !WKTEntityEvent.TryReadActivation(reader, out var result)) return;
            if (!m_claims.TryResolve(result.Handle, out var item) || result.Active) return;
            // Someone else took it first, or it was out of reach: it was never ours. A host object that still exists is bound
            // again, which shows it. If the local player dropped it meanwhile, that drop was held back and goes with it.
            WKT.Instance.Logger.LogInfo($"Another player picked up {item.itemName} first.");
            var dropObject = item.GetDropObject(false);
            if (dropObject != null && dropObject.TryGetComponent<WKTNetIdentity>(out var dropped))
                CancelPendingDrop(dropped);
            if (!item.hasBeenDestroyed)
                item.Destroy();
        }

        private void OnDropRequest(WKTPlayer sender, NetDataReader reader) {
            if (!IsHost || sender.IsHost || !WKTDropRequest.TryRead(reader, m_dropRequest)) return;
            var position = WKTWorldOffset.FromWire(m_dropRequest.Position, WKTWorldOffset.Get());
            var dropped = SpawnItem(m_dropRequest.PrefabId, position, m_dropRequest.Rotation, m_dropRequest.Velocity,
                (m_dropRequest.Flags & WKTDropRequest.FLAG_KINEMATIC) != 0, (m_dropRequest.Flags & WKTDropRequest.FLAG_USED) != 0,
                m_dropRequest.ItemData, GetDropKey(sender.Id, m_dropRequest.Token));
            if (dropped != null)
                Recyclers.OnDropped(dropped.Handle, sender.Id);
        }

        /// <summary>Host only: drops an item a player who left carried at <paramref name="position"/>, in this machine's world.</summary>
        public void SpawnLeftItem(WKTInventorySnapshot.Entry item, Vector3 position) {
            var hostId = m_network.LocalPlayer?.Id ?? 0;
            // The host's id is mixed in, so a player who takes over never reuses the keys of the old host's drops.
            var key = c_runtime_bit | WKTStableHash.Mix(WKTStableHash.Mix(WKTStableHash.Of(c_leave_salt), hostId), ++m_leaveDropCount);
            SpawnItem(item.PrefabId, position, Quaternion.identity, Vector3.zero, false, item.Used, item.Data, key);
        }

        /// <summary>Client only: whether the host has yet to give the local player <paramref name="item"/>.</summary>
        public bool IsClaimPending(Item item) => m_claims.IsUnresolved(item);

        /// <summary>
        /// Host only: spawns an item into the world with its data, registered under <paramref name="key"/> and kept from falling
        /// out of the world. Null when the prefab is unknown.
        /// </summary>
        private WKTNetIdentity? SpawnItem(string prefabId, Vector3 position, Quaternion rotation, Vector3 velocity, bool kinematic, bool used,
            List<string> itemData, ulong key) {
            Item_Object? prefab = null;
            try {
                prefab = CL_AssetManager.GetItemObjectPrefab(prefabId);
            }
            catch (Exception) {
                // Unknown ids throw inside the asset manager.
            }
            if (prefab == null) {
                LogMissingPrefab(prefabId);
                return null;
            }
            Item_Object instance;
            // Props register while spawning. They must wait for the item's data and the key, or clients would bind a copy
            // with the prefab's data under a key the dropping client never matches.
            m_suppressRegistration = true;
            try {
                instance = Instantiate(prefab, position, rotation, GetLevelParent(position));
            }
            finally {
                m_suppressRegistration = false;
            }
            instance.transform.localScale = Vector3.one;
            instance.itemData.data ??= new List<string>();
            instance.itemData.data.Clear();
            instance.itemData.data.AddRange(itemData);
            WKTItemModules.Unpack(instance.itemData, instance);
            instance.itemData.used = used;
            if (instance.TryGetComponent<Rigidbody>(out var body)) {
                body.isKinematic = kinematic;
                m_dropSettler.Place(body, kinematic, velocity, Time.realtimeSinceStartupAsDouble);
            }
            m_spawningKey = key;
            try {
                Register(instance);
            }
            finally {
                m_spawningKey = 0;
            }
            return instance.TryGetComponent<WKTNetIdentity>(out var identity) ? identity : null;
        }

        /// <summary>
        /// The level a position is in, so a spawned object unloads with it. The world loader's root when unknown, and the chamber
        /// while the host is in one, as the levels are hidden then (everyone is taken in together).
        /// </summary>
        private static Transform? GetLevelParent(Vector3 position) {
            if (WKTPlayerRooms.IsInChamber()) return WorldLoader.GetCurrentLevelParentRoot();
            var levels = WorldLoader.instance != null ? WorldLoader.instance.GetCurrentBranch()?.levelTracker : null;
            var level = levels != null ? WKTHostStreaming.FindClosestLevel(levels, position)?.GetLevel() : null;
            return level != null ? level.transform : WorldLoader.GetCurrentLevelParentRoot();
        }

        private static bool IsNear(byte playerId, Vector3 position, float range) {
            for (var i = 0; i < WKTPlayerPositions.RemoteCount; i++) {
                if (WKTPlayerPositions.GetRemoteId(i) == playerId)
                    return (WKTPlayerPositions.GetRemotePosition(i) - position).sqrMagnitude <= range * range;
            }
            // No position yet: trust the client rather than eat its item.
            return true;
        }

        /// <summary>Gives a client's own object an identity with a known key, without going through registration.</summary>
        private WKTNetIdentity AddIdentity(Component owner, ulong key) {
            var ownerObject = owner.gameObject;
            if (!ownerObject.TryGetComponent<WKTNetIdentity>(out var identity)) {
                m_registering = true;
                try {
                    identity = ownerObject.AddComponent<WKTNetIdentity>();
                }
                finally {
                    m_registering = false;
                }
            }
            identity.Key = key;
            NameHandholds(identity);
            identity.Handle = 0;
            identity.Entity = ownerObject.GetComponent<GameEntity>();
            identity.Prop = ownerObject.GetComponent<CL_Prop>();
            identity.Item = ownerObject.GetComponent<Item_Object>();
            identity.Kind = identity.Item != null ? WKTNetKind.Item : identity.Entity is CL_Prop ? WKTNetKind.Prop : WKTNetKind.Enemy;
            identity.Body = ownerObject.GetComponent<Rigidbody>();
            identity.Animator = identity.Entity != null ? WKTEntityAnimators.Find(identity.Entity) : ownerObject.GetComponentInChildren<Animator>();
            return identity;
        }

        /// <summary>Makes a client's copy its own again: no longer a puppet, and no longer tracked.</summary>
        private void Detach(WKTNetIdentity identity) {
            ReleasePuppet(identity);
            if (identity.Handle != 0)
                Authority.Forget(identity.Handle);
            identity.Key = 0;
            identity.Handle = 0;
            Object.Destroy(identity);
        }

        #endregion

        #region Props

        /// <summary>
        /// The local player grabbed a prop at <paramref name="anchor"/>, in the prop's space: its simulation moves to whoever
        /// should hold it now.
        /// </summary>
        public void OnLocalGrab(CL_Prop prop, Vector3 anchor) {
            if (!prop.TryGetComponent<WKTNetIdentity>(out var identity) || identity.Handle == 0) return;
            Authority.OnLocalGrab(identity, 0, anchor);
        }

        /// <summary>The local player grabbed a handhold. A flier's handhold makes them ride it.</summary>
        public void OnLocalHandholdGrab(CL_Handhold handhold) {
            Handholds.OnLocalGrab(handhold);
            if (!TryGetRideable(handhold, out var module, out var identity, out var part)) return;
            Authority.OnLocalGrab(identity!, part, identity!.transform.InverseTransformPoint(handhold.transform.position));
            SharedHolds.OnLocalRideGrab(module!, identity.Handle);
        }

        /// <summary>The local player let go of a handhold. Letting go of a flier's last one they held stops riding it.</summary>
        public void OnLocalHandholdRelease(CL_Handhold handhold) {
            if (handhold is CL_Handhold_Rope ropeLink) {
                Ropes.OnLocalRelease(ropeLink);
                return;
            }
            if (!TryGetRideable(handhold, out _, out var identity, out _)) return;
            foreach (var other in s_handholdScratch) {
                if (other != null && other.GetHolding()) return;
            }
            Authority.OnLocalRelease(identity!);
            SharedHolds.OnLocalRideRelease(identity!.Handle);
        }

        /// <summary>
        /// Whether <paramref name="handhold"/> steers a replicated flier, which handhold of it it is, and fills
        /// <see cref="s_handholdScratch"/> with all of the flier's handholds.
        /// </summary>
        private static bool TryGetRideable(CL_Handhold handhold, out HandholdModule_FlierSteering? module, out WKTNetIdentity? identity, out byte part) {
            module = null;
            identity = null;
            part = 0;
            if (handhold.modules == null) return false;
            foreach (var candidate in handhold.modules) {
                if (candidate is not HandholdModule_FlierSteering steering || steering.basicFlier == null) continue;
                module = steering;
                break;
            }
            if (module == null || !module.basicFlier.TryGetComponent(out identity) || identity!.Handle == 0) return false;
            module.basicFlier.GetComponentsInChildren(true, s_handholdScratch);
            var index = s_handholdScratch.IndexOf(handhold);
            part = (byte)Mathf.Clamp(index, 0, byte.MaxValue);
            return true;
        }

        /// <summary>The local player let go of a prop with every hand.</summary>
        public void OnLocalRelease(CL_Prop prop) {
            if (prop.TryGetComponent<WKTNetIdentity>(out var identity))
                Authority.OnLocalRelease(identity);
        }

        private void HandlePlayerDisconnected(WKTPlayer player) {
            Authority.OnPlayerLeft(player.Id);
            WKTPlayerPerks.Remove(player.Id);
            Inventories.OnPlayerLeft(player.Id);
            Viewers.OnPlayerLeft(player.Id);
        }

        #endregion

        #region Player effects

        /// <summary>Client only: an enemy in the host's world hit or otherwise affected the local player.</summary>
        private static void OnPlayerEffect(WKTPlayer sender, NetDataReader reader) {
            if (!sender.IsHost || !WKTPlayerEffect.TryRead(reader, out var effect)) return;
            var player = ENT_Player.playerObject;
            if (player != null && !player.dead)
                WKTTargets.Apply(player, effect);
        }

        #endregion

        #region Migration

        /// <summary>
        /// The host changed. A client that took over turns every puppet back into a simulated object under the same handle,
        /// so the other clients' puppets stay matched once they reconnect and get its binds.
        /// </summary>
        private void OnHostChanged() {
            m_buffers.Clear();
            Clock.Reset();
            ReturnRebars.OnHostChanged();
            Authority.OnHostChanged();
            Ropes.OnHostChanged();
            Inventories.OnHostChanged();
            SharedHolds.Clear();
            if (!IsHost) return;
            m_claims.ClearBeforeBind();
            m_byHandle.Clear();
            ushort highest = 0;
            foreach (var identity in FindObjectsByType<WKTNetIdentity>(FindObjectsInactive.Include, FindObjectsSortMode.None)) {
                if (identity.Handle == 0) continue;
                ReleasePuppet(identity);
                identity.KillSent = identity.Entity != null && identity.Entity.dead;
                identity.ReportedActive = IsInWorld(identity);
                m_byHandle[identity.Handle] = identity;
                if (identity.Handle > highest)
                    highest = identity.Handle;
            }
            m_binder.Clear();
            Tongues.Clear();
            EnemyHits.Clear();
            m_claims.Clear();
            m_pendingDrops.Clear();
            m_handles.ResetAfter(highest);
            m_sender.Clear();
            WKT.Instance.Logger.LogInfo($"Took over {m_byHandle.Count} world objects.");
        }

        #endregion

        private readonly struct PendingDrop {
            public readonly Item Item;
            public readonly WKTNetIdentity Identity;
            public readonly uint Token;

            public PendingDrop(Item item, WKTNetIdentity identity, uint token) {
                Item = item;
                Identity = identity;
                Token = token;
            }
        }
    }
}
