using System;
using System.Collections.Generic;
using HarmonyLib;
using JetBrains.Annotations;
using Steamworks.Data;
using UnityEngine;
using UnityEngine.Events;
using WhiteKnuckleTogether.Networking.LiteNetLib;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// Makes the host own the world's doors and world events in a synced co-op run.
    /// <list type="bullet">
    /// <item>Doors open and close only on the host, which tells clients; a client's own door changes are ignored.</item>
    /// <item>The world part of an event (doors, animators, objects switched on or off) only ever runs because the host says
    /// so. A client's player setting an event off (walking into a trigger, touching a hand trigger, pressing a button) sends
    /// it to the host. The host runs the world part, once for triggers that only fire once, and tells every client to run it
    /// too. The local part (the player, camera, screen or sound) runs on the machine that set the event off.</item>
    /// <item>The gate doors that close behind the players follow <see cref="WKTDoorGrouping"/>, so the group stays together.
    /// Gates are <see cref="UT_Door"/>s, or doors an animator moves (the silo doors) whose bool can be set back.</item>
    /// <item>Every machine remembers the world events applied in each loaded level, so a player who joins late gets them.</item>
    /// </list>
    /// Lives next to <see cref="WKTRunController"/> and only exists during a synced co-op run.
    /// </summary>
    internal sealed class WKTDoorSync : MonoBehaviour {
        /// <summary>How often the host re-evaluates the gates, per second.</summary>
        public const float GROUPING_RATE = 10f;
        /// <summary>How near a door the camera must be to hear it open: a player waiting under it. Silo rooms are about 70 m tall.</summary>
        public const float OPEN_SOUND_RANGE = 40f;
        /// <summary>Half the size of the box around a gate in which a player blocks it from closing.</summary>
        public static readonly Vector3 DOORWAY_EXTENTS = new(2.5f, 3f, 2.5f);
        /// <summary>The height of the box around an animated door in which a player blocks it; its width comes from its trigger.</summary>
        private const float c_animated_doorway_half_height = 3f;
        /// <summary>How long a started group start ignores being set off again, by the players pulled into its trigger.</summary>
        private const float c_group_start_cooldown = 10f;
        /// <summary>How far above a trigger's bottom its room is looked up, so a bottom resting on a level's entrance counts in that level.</summary>
        private const float c_room_probe_height = 0.1f;
        /// <summary>
        /// <c>UT_Timer</c> fires by itself on every machine, at times that drift apart, such as Delta Labs' power fluctuation
        /// switching lights and turrets. Only the host's timers fire; clients run what the host's did.
        /// </summary>
        private static readonly Type[] s_inputTypes = {
            typeof(UT_TriggerZone), typeof(UT_HandTrigger), typeof(CL_Button), typeof(CL_HoldButton), typeof(UT_Timer), typeof(UT_RandomTimer),
            typeof(CL_HoldLever), typeof(CL_ToggleButton)
        };
        private static readonly List<(UnityEventBase Event, string Name)> s_inputEvents = new();
        private static readonly Dictionary<Type, OnceFields> s_onceFields = new();
        private static readonly HashSet<string> s_warnedLevels = new();
        private readonly Dictionary<UnityEventBase, EventRef> m_events = new();
        private readonly Dictionary<(ulong, byte), EventRef> m_eventsByKey = new();
        private readonly Dictionary<ulong, UT_Door> m_doors = new();
        private readonly Dictionary<int, ulong> m_doorKeys = new();
        /// <summary>Doors an animator moves, by the key of their controller.</summary>
        private readonly Dictionary<ulong, AnimatedDoor> m_animatedDoors = new();
        /// <summary>Host: every door's last state. Client: the host's state for doors not ready yet, applied when they are.</summary>
        private readonly Dictionary<ulong, WKTGateState> m_doorStates = new();
        private readonly HashSet<int> m_startedDoors = new();
        private readonly List<Gate> m_gates = new();
        private readonly HashSet<int> m_gateDoors = new();
        private readonly WKTDoorGrouping m_grouping = new();
        private readonly List<KeyValuePair<ulong, M_Level>> m_levelScratch = new();
        private readonly List<UT_Door> m_doorScratch = new();
        private readonly List<MonoBehaviour> m_behaviourScratch = new();
        private readonly HashSet<int> m_computerLevers = new();
        private readonly List<EventRef> m_eventScratch = new();
        private readonly List<ulong> m_keyScratch = new();
        private readonly List<WKTGroupGate> m_groupGates = new();
        private readonly List<WKTGroupPlayer> m_groupPlayers = new();
        private readonly List<bool> m_passed = new();
        private readonly List<bool> m_open = new();
        /// <summary>Each player's last known route room, by id, kept while a lookup briefly comes back unknown.</summary>
        private readonly int[] m_lastRooms = new int[byte.MaxValue + 1];
        private readonly WKTAppliedEvents m_applied = new();
        private WKTOneWayDoors m_oneWayDoors = null!;
        private WKTMovingParts m_movingParts = null!;
        /// <summary>Client only: events the host applied in levels this client has not indexed yet.</summary>
        private readonly List<(ulong Key, byte Index)> m_pendingApplied = new();
        private readonly Dictionary<(ulong, byte), float> m_groupStarts = new();
        /// <summary>Level loading events that already brought everyone to whoever set them off, here or anywhere.</summary>
        private readonly HashSet<(ulong, byte)> m_loadPulls = new();
        /// <summary>Group starts the local player set off, waiting for everyone (see <see cref="UpdateWaitingStarts"/>).</summary>
        private readonly List<(EventRef Reference, Transform? Argument)> m_waiting = new();
        private WKTNetwork m_network = null!;
        private Action<M_Level, ulong> m_onLevelKeyed = null!;
        private Action<M_Level> m_onLevelForgetting = null!;
        private Action<WKTPlayer> m_onPlayerConnected = null!;
        private Action m_onHostChanged = null!;
        private float m_nextGrouping;
        private float m_lastPulled = float.NegativeInfinity;
        private float m_lastGrouping = -1f;

        /// <summary>The door sync of the synced co-op run in progress. Null everywhere else, including singleplayer.</summary>
        public static WKTDoorSync? Instance { get; private set; }
        /// <summary>Set while a door's own Start sets up its first state, which every machine does alike.</summary>
        public static int DoorStartDepth { get; set; }
        /// <summary>Set while the host's gate grouping or a host door message moves a door.</summary>
        public static bool IsDriving { get; private set; }
        /// <summary>Set while this machine runs part of an event itself, so that invocation is not intercepted again.</summary>
        public static bool IsApplying { get; private set; }
        public bool IsHost => m_network.Mode == NetworkMode.Host;
        /// <summary>The level buttons' own states.</summary>
        public WKTButtonSync Buttons { get; private set; } = null!;

        [UsedImplicitly]
        private void Awake() {
            m_network = WKT.Instance.Network;
            if (!m_network.Session.IsSyncedWorld) {
                enabled = false;
                return;
            }
            Instance = this;
            Array.Fill(m_lastRooms, -1);
            m_network.Messages.Register(WKTMessageId.EventRelay, OnEventRelay);
            m_network.Messages.Register(WKTMessageId.DoorState, OnDoorState);
            m_network.Messages.Register(WKTMessageId.EventApplied, OnEventApplied);
            m_movingParts = new WKTMovingParts(m_network);
            m_oneWayDoors = new WKTOneWayDoors();
            Buttons = new WKTButtonSync(m_network, m_oneWayDoors);
            m_network.Messages.Register(WKTMessageId.ButtonState, Buttons.OnMessage);
            m_network.Messages.Register(WKTMessageId.MovingPart, OnMovingPart);
            m_network.Messages.Register(WKTMessageId.GatherPull, OnGatherPull);
            m_onLevelKeyed = IndexLevel;
            m_onLevelForgetting = ForgetLevel;
            m_onPlayerConnected = HandlePlayerConnected;
            WKTContentSeeding.LevelKeyed += m_onLevelKeyed;
            WKTContentSeeding.LevelForgetting += m_onLevelForgetting;
            m_network.OnPlayerConnected += m_onPlayerConnected;
            m_onHostChanged = OnHostChanged;
            m_network.OnHostChanged += m_onHostChanged;
            // Levels keyed before this existed.
            WKTContentSeeding.GetLevels(m_levelScratch);
            foreach (var pair in m_levelScratch)
                IndexLevel(pair.Value, pair.Key);
            m_levelScratch.Clear();
        }

        [UsedImplicitly]
        private void OnDestroy() {
            if (Instance != this) return;
            Instance = null;
            DoorStartDepth = 0;
            IsDriving = false;
            IsApplying = false;
            WKTContentSeeding.LevelKeyed -= m_onLevelKeyed;
            WKTContentSeeding.LevelForgetting -= m_onLevelForgetting;
            if (WKT.InstanceOrNull == null) return;
            m_network.Messages.Unregister(WKTMessageId.EventRelay);
            m_network.Messages.Unregister(WKTMessageId.DoorState);
            m_network.Messages.Unregister(WKTMessageId.EventApplied);
            m_network.Messages.Unregister(WKTMessageId.MovingPart);
            m_network.Messages.Unregister(WKTMessageId.GatherPull);
            m_network.Messages.Unregister(WKTMessageId.ButtonState);
            m_network.OnPlayerConnected -= m_onPlayerConnected;
            m_network.OnHostChanged -= m_onHostChanged;
        }

        /// <summary>
        /// A client that took over as host now owns the doors as they stand on its machine, which followed the old host, and
        /// works out which gates the group passed from where everyone is. It already has the applied events.
        /// </summary>
        private void OnHostChanged() {
            m_pendingApplied.Clear();
            Buttons.OnHostChanged();
            if (!IsHost) return;
            m_doorStates.Clear();
            foreach (var pair in m_doors) {
                if (pair.Value != null && m_startedDoors.Contains(pair.Value.GetInstanceID()))
                    m_doorStates[pair.Key] = pair.Value.IsOpen() ? WKTGateState.Open : WKTGateState.Closed;
            }
            foreach (var pair in m_animatedDoors)
                m_doorStates[pair.Key] = pair.Value.State;
            m_grouping.Clear();
        }

        [UsedImplicitly]
        private void Update() {
            if (m_network.IsMultiplayerRun) {
                m_movingParts.Update(this);
                Buttons.Update();
                m_oneWayDoors.Update();
                UpdateWaitingStarts();
                // Level scripting held back while the loader generated runs once it is done.
                if (m_pendingApplied.Count > 0)
                    ApplyPendingEvents();
            }
            if (!m_network.IsMultiplayerRun || Time.unscaledTime < m_nextGrouping) return;
            m_nextGrouping = Time.unscaledTime + 1f / GROUPING_RATE;
            if (IsHost)
                UpdateGrouping();
            ReconcileAnimatedDoors();
        }

        #region Indexing

        private void IndexLevel(M_Level level, ulong levelKey) {
            WKTLevelTweaks.Apply(level);
            var levelId = level.gameObject.GetInstanceID();
            level.GetComponentsInChildren(true, m_doorScratch);
            foreach (var door in m_doorScratch) {
                if (door == null || !WKTContentSeeding.TryGetKey(door, out var key)) continue;
                m_doors[key] = door;
                m_doorKeys[door.GetInstanceID()] = key;
            }
            m_doorScratch.Clear();
            level.GetComponentsInChildren(true, m_behaviourScratch);
            WKTComputerLevers.Collect(level, m_computerLevers);
            foreach (var behaviour in m_behaviourScratch) {
                if (behaviour == null || (Array.IndexOf(s_inputTypes, behaviour.GetType()) < 0 && !IsDoorDamage(behaviour))) continue;
                // A creature's own buttons run on the machine simulating it (see WKTEntityEvents). A computer's lever is each
                // player's own, so its events run where it was pulled, as in singleplayer (see WKTComputerLevers).
                if (behaviour is CL_Button and not CL_ToggleButton && behaviour.GetComponentInParent<Denizen>(true) != null ||
                    m_computerLevers.Contains(behaviour.GetInstanceID())) continue;
                IndexInput(behaviour, levelId, level);
            }
            m_behaviourScratch.Clear();
            m_computerLevers.Clear();
            m_movingParts.IndexLevel(level);
            Buttons.IndexLevel(level);
            m_oneWayDoors.IndexLevel(level);
            ApplyPendingEvents();
        }

        /// <summary>A door's open amount changed here by hand (a crank or lever), which every other machine follows.</summary>
        public void OnLocalDoorMoved(UT_Door door) => m_movingParts.OnLocalDoorMoved(door);

        public bool TryGetDoorKey(UT_Door door, out ulong key) => m_doorKeys.TryGetValue(door.GetInstanceID(), out key);

        public bool TryGetDoor(ulong key, out UT_Door? door) => m_doors.TryGetValue(key, out door) && door != null;

        private void OnMovingPart(WKTPlayer sender, NetDataReader reader) => m_movingParts.OnMessage(sender, reader, this);

        /// <summary>
        /// Indexes an input component's events: which listeners act on the world and which on the local machine, and the gate
        /// doors its trigger closes behind the players. A listener that closes a gate belongs to the grouping, so the event
        /// itself never runs it.
        /// </summary>
        private void IndexInput(MonoBehaviour input, int levelId, M_Level level) {
            if (!WKTContentSeeding.TryGetKey(input, out var key)) return;
            CollectInputEvents(input, s_inputEvents);
            var zone = input as UT_TriggerZone;
            var playerZone = zone != null && WKTWorldWiring.IsTriggeredByPlayers(zone.entityTags);
            var triggeredByPlayers = playerZone || input is UT_HandTrigger;
            for (var index = 0; index < s_inputEvents.Count && index <= byte.MaxValue; index++) {
                var (unityEvent, name) = s_inputEvents[index];
                if (unityEvent == null) continue;
                var count = unityEvent.GetPersistentEventCount();
                var masks = default(WKTListenerMasks);
                var drivesGoo = false;
                for (var i = 0; i < count; i++) {
                    var target = unityEvent.GetPersistentTarget(i);
                    if (target == null) {
                        masks = masks.WithSkipped(i);
                        continue;
                    }
                    drivesGoo |= WKTWorldWiring.IsGooTarget(target.GetType());
                    var method = unityEvent.GetPersistentMethodName(i);
                    if (target is UT_Door door && WKTWorldWiring.IsGateWiring(door.startOpen, method, triggeredByPlayers))
                        AddGate(door, input, level);
                    else if (playerZone && TryAddAnimatedGate(zone!, unityEvent, i, target, level)) {
                        masks = masks.WithSkipped(i);
                        continue;
                    }
                    else if (input is UT_TriggerZone && LooksLikeDoor(target))
                        WarnUnknownDoor(level, target, method);
                    masks = masks.With(i, WKTWorldWiring.IsLocalTarget(target.GetType()) || IsOnPlayer(target));
                }
                var buildsBranch = WKTGatherCheck.CallsGenerator(unityEvent);
                var listed = WKTGatherCheck.IsGroupStart(level.name, WKTGatherCheck.PathBelow(input.transform, level.transform), name, out var inZone, out var below,
                    out var zonePath);
                var zoneOwner = zonePath != null ? level.transform.Find(zonePath) : input.transform;
                var gatherZone = inZone && zoneOwner != null && zoneOwner.TryGetComponent<BoxCollider>(out var box) ? box : null;
                var reference = new EventRef(key, (byte)index, unityEvent, input, masks, name, levelId, WKTGatherCheck.WaitsForGroup(listed, buildsBranch, masks),
                    buildsBranch, drivesGoo, gatherZone, below, WKTGatherCheck.IsLoading(unityEvent), WKTGatherCheck.GetLoadPull(unityEvent));
                m_events[unityEvent] = reference;
                m_eventsByKey[(key, (byte)index)] = reference;
            }
            s_inputEvents.Clear();
        }

        /// <summary>
        /// An input's events, numbered the same on every machine: its serialized event fields, then for a timer each of its
        /// intermediate events.
        /// </summary>
        private static void CollectInputEvents(MonoBehaviour input, List<(UnityEventBase Event, string Name)> events) {
            events.Clear();
            foreach (var field in WKTUnityEventFields.Get(input.GetType()))
                events.Add((field.GetValue(input) as UnityEventBase, field.Name)!);
            if (input is CL_HoldLever { snapPoints: { } snaps }) {
                for (var i = 0; i < snaps.Length; i++) {
                    events.Add((snaps[i]?.onEntered!, $"{nameof(CL_HoldLever.snapPoints)}[{i}].onEntered"));
                    events.Add((snaps[i]?.onExited!, $"{nameof(CL_HoldLever.snapPoints)}[{i}].onExited"));
                }
            }
            if (input is not UT_Timer { otherEvents: { } intermediates }) return;
            for (var i = 0; i < intermediates.Count; i++)
                events.Add((intermediates[i]?.timeEvent!, $"{nameof(UT_Timer.otherEvents)}[{i}]"));
        }

        /// <summary>A door the trigger <paramref name="input"/> closes. It is passed at that trigger, the way the game closes it.</summary>
        private void AddGate(UT_Door door, MonoBehaviour input, M_Level level) {
            if (!m_doorKeys.TryGetValue(door.GetInstanceID(), out var key) || !m_gateDoors.Add(door.GetInstanceID())) return;
            var levelTransform = level.transform;
            var pivot = door.transform.position;
            if (!TryGetZoneBox(input, out var zoneCenter, out var zoneExtents)) {
                zoneCenter = pivot;
                zoneExtents = DOORWAY_EXTENTS;
            }
            m_gates.Add(new Gate(key, level, levelTransform.InverseTransformPoint(zoneCenter), zoneExtents, levelTransform.InverseTransformPoint(pivot),
                DOORWAY_EXTENTS, level.gameObject.GetInstanceID(), door, null));
            WKT.Instance.Logger.LogInfo($"Gate door {door.name} in {level.name}, passed at {input.name}.");
        }

        /// <summary>The world box a trigger's collider covers, whatever its rotation and scale. False without a collider.</summary>
        private static bool TryGetZoneBox(Component input, out Vector3 center, out Vector3 extents) {
            if (input.TryGetComponent<BoxCollider>(out var box)) {
                var transform = box.transform;
                var half = box.size * 0.5f;
                center = transform.TransformPoint(box.center);
                extents = Abs(transform.TransformVector(half.x, 0f, 0f)) + Abs(transform.TransformVector(0f, half.y, 0f)) + Abs(transform.TransformVector(0f, 0f, half.z));
                return true;
            }
            if (input.TryGetComponent<Collider>(out var collider) && collider.enabled && collider.gameObject.activeInHierarchy) {
                var bounds = collider.bounds;
                center = bounds.center;
                extents = bounds.extents;
                return extents != Vector3.zero;
            }
            center = default;
            extents = default;
            return false;
        }

        private static Vector3 Abs(Vector3 v) => new(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

        /// <summary>
        /// A player trigger that moves a door through a reversible animator bool, like the trigger above each silo door that
        /// closes it once the players are through. The door becomes a gate passed at the trigger, the way the game closes it.
        /// </summary>
        private bool TryAddAnimatedGate(UT_TriggerZone zone, UnityEventBase unityEvent, int listener, UnityEngine.Object target, M_Level level) {
            if (target is not UT_SimpleAnimatorController controller || !WKTWorldWiring.IsAnimatorBoolWiring(target.GetType(), unityEvent.GetPersistentMethodName(listener)))
                return false;
            if (!controller.TryGetComponent<Animator>(out var animator) || animator.runtimeAnimatorController == null ||
                !WKTWorldWiring.IsReversibleDoorAnimator(animator.runtimeAnimatorController.name)) return false;
            if (!WKTUnityEventFields.TryGetStringArgument(unityEvent, listener, out var argument) ||
                !WKTWorldWiring.TryParseAnimatorBool(argument, out var parameter, out var closedValue)) return false;
            if (!WKTContentSeeding.TryGetKey(controller, out var key)) return false;
            if (m_animatedDoors.ContainsKey(key)) return true;
            var animated = new AnimatedDoor(controller, animator, Animator.StringToHash(parameter), closedValue);
            m_animatedDoors.Add(key, animated);
            m_gateDoors.Add(controller.GetInstanceID());
            var levelTransform = level.transform;
            var doorway = DOORWAY_EXTENTS;
            if (!TryGetZoneBox(zone, out var zoneCenter, out var zoneExtents)) {
                zoneCenter = zone.transform.position;
                zoneExtents = DOORWAY_EXTENTS;
            }
            else
                doorway = new Vector3(Mathf.Max(zoneExtents.x, doorway.x), c_animated_doorway_half_height, Mathf.Max(zoneExtents.z, doorway.z));
            m_gates.Add(new Gate(key, level, levelTransform.InverseTransformPoint(zoneCenter), zoneExtents,
                levelTransform.InverseTransformPoint(controller.transform.position), doorway, level.gameObject.GetInstanceID(), null, animated));
            WKT.Instance.Logger.LogInfo($"Gate door {controller.name} (animator) in {level.name}.");
            // The host's state may have arrived before this level was built here.
            if (!IsHost && m_doorStates.TryGetValue(key, out var state))
                SetAnimatedDoor(animated, state);
            return true;
        }

        /// <summary>
        /// Damage that moves a door, such as a drop ladder that swings down when something hits it. Any player's hit lands on
        /// their own machine, so its events go through the host like a button's. Other damage keeps to its own syncs.
        /// </summary>
        private static bool IsDoorDamage(MonoBehaviour behaviour) =>
            behaviour is UT_Damage damage && (TargetsDoor(damage.DamageEvent) || TargetsDoor(damage.KillEvent));

        private static bool TargetsDoor(UnityEventBase? unityEvent) {
            if (unityEvent == null) return false;
            var count = unityEvent.GetPersistentEventCount();
            for (var i = 0; i < count; i++) {
                if (unityEvent.GetPersistentTarget(i) is UT_Door) return true;
            }
            return false;
        }

        private static bool LooksLikeDoor(UnityEngine.Object target) =>
            target is not UT_Door && target.name.IndexOf("door", StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>Wiring that seems to close a door some other way. Logged once per level type so unknown rooms show up in play.</summary>
        private static void WarnUnknownDoor(M_Level level, UnityEngine.Object target, string method) {
            if (!s_warnedLevels.Add(level.name)) return;
            WKT.Instance.Logger.LogInfo($"Level {level.name} drives door-like object {target.name} through {target.GetType().Name}.{method}. " +
                "It is not grouped like gate doors.");
        }

        private static bool IsOnPlayer(UnityEngine.Object target) =>
            target is Component component && component != null && component.GetComponentInParent<ENT_Player>(true) != null;

        private void ForgetLevel(M_Level level) {
            var levelId = level.gameObject.GetInstanceID();
            m_applied.ForgetLevel(levelId);
            Buttons.ForgetLevel(level);
            m_oneWayDoors.ForgetLevel(level);
            for (var i = m_gates.Count - 1; i >= 0; i--) {
                var gate = m_gates[i];
                if (gate.LevelId != levelId) continue;
                if (gate.Door != null)
                    m_gateDoors.Remove(gate.Door.GetInstanceID());
                if (gate.Animated != null) {
                    if (gate.Animated.Controller != null)
                        m_gateDoors.Remove(gate.Animated.Controller.GetInstanceID());
                    m_animatedDoors.Remove(gate.Key);
                }
                m_grouping.Forget(gate.Key);
                m_gates.RemoveAt(i);
            }
            m_eventScratch.Clear();
            foreach (var reference in m_events.Values) {
                if (reference.LevelId == levelId)
                    m_eventScratch.Add(reference);
            }
            foreach (var reference in m_eventScratch) {
                m_events.Remove(reference.Event);
                m_eventsByKey.Remove((reference.Key, reference.Index));
            }
            m_eventScratch.Clear();
            level.GetComponentsInChildren(true, m_doorScratch);
            foreach (var door in m_doorScratch) {
                if (door == null || !m_doorKeys.Remove(door.GetInstanceID(), out var key)) continue;
                m_doors.Remove(key);
                m_startedDoors.Remove(door.GetInstanceID());
            }
            m_doorScratch.Clear();
        }

        #endregion

        #region Events

        /// <summary>
        /// Called when any UnityEvent is set off. Events with no world part run as usual. Otherwise a client runs only the local
        /// part and sends the host the events its own player set off; the host runs the world part once and tells everyone.
        /// Returns false when the game's own invocation must not run.
        /// </summary>
        public bool OnInvoke(UnityEventBase unityEvent, Transform? argument) {
            if (IsApplying || !m_events.TryGetValue(unityEvent, out var reference)) return true;
            if (reference.LoadPull != WKTLoadPull.None)
                PullForLoading(reference, argument);
            // Racers never wait for, or are pulled to, whoever got there first.
            if (!reference.GroupStart || m_network.Session.IsRace) return Dispatch(reference, argument);
            // Something the group starts together waits for everyone around whoever set it off (or in its room), on their
            // machine. Once it started, the others setting it off again as they are pulled in do nothing.
            if (IsRecentGroupStart(reference) || Time.unscaledTime - m_lastPulled < c_group_start_cooldown) return false;
            // A room's door shuts for good once everyone was in: walking through its trigger again later waits for nobody.
            if (reference.Zone != null && m_groupStarts.ContainsKey((reference.Key, reference.Index))) return Dispatch(reference, argument);
            if (!IsGathered(reference, out _, out _)) {
                if (!IsWaiting(reference))
                    m_waiting.Add((reference, argument));
                return false;
            }
            return StartGroup(reference, argument);
        }

        /// <summary>Whether everyone a group start waits for is there: inside its zone, or else around the local player.</summary>
        private static bool IsGathered(EventRef reference, out int near, out int living) =>
            reference.Zone != null
                ? WKTGatherCheck.AllInZone(reference.Zone, reference.ZoneBelow, out near, out living)
                : WKTGatherCheck.AllNearLocal(out near, out living);

        /// <summary>A group start runs: the others are pulled to the local player first if it builds a branch.</summary>
        private bool StartGroup(EventRef reference, Transform? argument) {
            m_groupStarts[(reference.Key, reference.Index)] = Time.unscaledTime;
            if (reference.PullsGroup && ENT_Player.playerObject is { } local)
                SendGatherPull(local.transform.position, WKTLoadPull.None, string.Empty, reference);
            return Dispatch(reference, argument);
        }

        /// <summary>
        /// The local player set off level scripting that takes levels away: whoever is in them and more than
        /// <see cref="WKTGatherCheck.RADIUS"/> from the local player is brought over first, once per event. Deleting the other
        /// branches keeps the trigger's own (see <see cref="WKTLevelScripting"/>), so only whoever is on another is brought.
        /// </summary>
        private void PullForLoading(EventRef reference, Transform? argument) {
            if (m_network.Session.IsRace || ENT_Player.playerObject is not { dead: false } local || !IsFromLocalPlayer(argument) ||
                !m_loadPulls.Add((reference.Key, reference.Index))) return;
            var keptBranch = reference.LoadPull == WKTLoadPull.OtherBranches ? WKTLevelScripting.GetBranchId(reference.Owner) ?? string.Empty : string.Empty;
            WKT.Instance.Logger.LogInfo($"Set off level scripting ({reference.LoadPull}) at {reference.Owner.name}. Bringing over whoever it takes levels from.");
            SendGatherPull(local.transform.position, reference.LoadPull, keptBranch, reference);
        }

        /// <summary>Whether a group start already ran moments ago, here or anywhere the host said.</summary>
        private bool IsRecentGroupStart(EventRef reference) =>
            m_groupStarts.TryGetValue((reference.Key, reference.Index), out var time) && Time.unscaledTime - time < c_group_start_cooldown;

        private void SendGatherPull(Vector3 position, WKTLoadPull pull, string keptBranch, EventRef reference) {
            var writer = m_network.BeginMessage(WKTMessageId.GatherPull);
            WriteGatherPull(writer, WKTWorldOffset.ToWire(position, WKTWorldOffset.Get()), pull, keptBranch, reference.Key, reference.Index);
            if (IsHost)
                m_network.Broadcast(writer, SendType.Reliable);
            else
                m_network.SendToHost(writer, SendType.Reliable);
        }

        private static void WriteGatherPull(NetDataWriter writer, Vector3 wirePosition, WKTLoadPull pull, string keptBranch, ulong key, byte index) {
            WKTWire.Put(writer, wirePosition);
            writer.Put((byte)pull);
            writer.Put(keptBranch);
            writer.Put(key);
            writer.Put(index);
        }

        /// <summary>
        /// Whoever started a shortcut pulls the others near them onto spots around them, so nobody is left on the route it
        /// deletes; whoever set off level scripting that takes levels away brings over whoever is in them.
        /// </summary>
        private void OnGatherPull(WKTPlayer sender, NetDataReader reader) {
            if (!WKTWire.TryGetPosition(reader, out var wirePosition) || !reader.TryGetByte(out var pullValue) || pullValue > (byte)WKTLoadPull.OtherBranches ||
                !reader.TryGetString(out var keptBranch) || !reader.TryGetULong(out var key) || !reader.TryGetByte(out var index)) return;
            var pull = (WKTLoadPull)pullValue;
            if (IsHost) {
                if (sender.IsHost) return;
                var relay = m_network.BeginMessage(WKTMessageId.GatherPull);
                WriteGatherPull(relay, wirePosition, pull, keptBranch, key, index);
                m_network.Broadcast(relay, SendType.Reliable, sender);
            }
            else if (!sender.IsHost) return;
            var center = WKTWorldOffset.FromWire(wirePosition, WKTWorldOffset.Get());
            if (pull != WKTLoadPull.None) {
                // This player's own pass through the same trigger brings nobody again.
                m_loadPulls.Add((key, index));
                var loader = WorldLoader.instance;
                var localBranch = loader != null ? loader.GetCurrentBranch()?.id : null;
                if (WKTGatherCheck.IsPulledByLoading(pull, localBranch, keptBranch))
                    WKTGatherCheck.PullLocalFar(center, m_network.Session.LocalSlot);
                return;
            }
            m_lastPulled = Time.unscaledTime;
            WKTGatherCheck.PullLocal(center, m_network.Session.LocalSlot);
        }

        /// <summary>
        /// Each group start the local player set off runs once everyone is near, even when its trigger only fires once. It is
        /// dropped if the local player dies, or walks off from one gathered around them; one gathered in a room keeps waiting
        /// however the local player comes and goes, as its door must still close once everyone is in.
        /// </summary>
        private void UpdateWaitingStarts() {
            for (var i = m_waiting.Count - 1; i >= 0; i--) {
                var (reference, argument) = m_waiting[i];
                if (reference.Owner == null || ENT_Player.playerObject is not { dead: false } local || IsRecentGroupStart(reference) ||
                    (reference.Zone == null &&
                     (local.transform.position - reference.Owner.transform.position).sqrMagnitude > WKTGatherCheck.LEAVE_RADIUS * WKTGatherCheck.LEAVE_RADIUS)) {
                    m_waiting.RemoveAt(i);
                    continue;
                }
                if (!IsGathered(reference, out var near, out var living)) {
                    WKTWaitHud.Report(reference.Zone != null ? WKTGatherCheck.WAIT_IN_ROOM : WKTGatherCheck.WAIT_NEAR, near, living);
                    continue;
                }
                m_waiting.RemoveAt(i);
                if (!StartGroup(reference, argument)) continue;
                Apply(reference, argument, true);
                Apply(reference, argument, false);
            }
        }

        private bool IsWaiting(EventRef reference) {
            foreach (var (waiting, _) in m_waiting) {
                if (waiting == reference) return true;
            }
            return false;
        }

        /// <summary>Runs an event set off here the synced way; false when the game's own invocation must not run.</summary>
        private bool Dispatch(EventRef reference, Transform? argument) {
            var masks = reference.Masks;
            if (!masks.HasWorld && !masks.HasSkipped) return true;
            if (IsHost) return OnHostInvoke(reference, argument);
            // A client's own timers never fire: the host's do, for everyone.
            if (reference.Autonomous) return false;
            if (masks.HasLocal)
                Apply(reference, argument, false);
            if (masks.HasWorld && IsFromLocalPlayer(argument)) {
                var writer = m_network.BeginMessage(WKTMessageId.EventRelay);
                writer.Put(reference.Key);
                writer.Put(reference.Index);
                m_network.SendToHost(writer, SendType.Reliable);
            }
            return false;
        }

        /// <summary>
        /// The host set off an event itself. Its world part runs here and on every client, unless it is a once-only event a
        /// client already set off, in which case only the local part runs now. Listeners the grouping owns never run.
        /// </summary>
        private bool OnHostInvoke(EventRef reference, Transform? argument) {
            var masks = reference.Masks;
            var runWorld = masks.HasWorld;
            if (runWorld) {
                var once = IsOnce(reference);
                if (once && m_applied.WasApplied(reference.Key, reference.Index))
                    runWorld = false;
                else {
                    m_applied.Record(reference.Key, reference.Index, reference.LevelId, once);
                    BroadcastApplied(reference);
                }
            }
            if (runWorld && !masks.HasSkipped) return true;
            if (runWorld)
                Apply(reference, argument, true);
            if (masks.HasLocal)
                Apply(reference, argument, false);
            return false;
        }

        /// <summary>A transform argument is whoever set off a trigger: only the local player's body or hands count.</summary>
        private static bool IsFromLocalPlayer(Transform? argument) {
            if (argument is not { } transform) return true;
            if (transform.GetComponentInParent<ENT_Player>(true) != null) return true;
            return transform.TryGetComponent<ObjectTagger>(out var tagger) && tagger.HasTag("PlayerHand");
        }

        private void OnEventRelay(WKTPlayer sender, NetDataReader reader) {
            if (!IsHost || sender.IsHost || !reader.TryGetULong(out var key) || !reader.TryGetByte(out var index)) return;
            if (!m_eventsByKey.TryGetValue((key, index), out var reference) || !reference.Masks.HasWorld || reference.Owner == null) return;
            if (reference.GroupStart) {
                if (IsRecentGroupStart(reference)) return;
                m_groupStarts[(reference.Key, reference.Index)] = Time.unscaledTime;
            }
            if (!m_applied.Record(reference.Key, reference.Index, reference.LevelId, IsOnce(reference))) return;
            Apply(reference, reference.Owner.transform, true);
            BroadcastApplied(reference);
        }

        private void BroadcastApplied(EventRef reference) {
            var writer = m_network.BeginMessage(WKTMessageId.EventApplied);
            writer.Put(reference.Key);
            writer.Put(reference.Index);
            m_network.Broadcast(writer, SendType.Reliable);
        }

        private void OnEventApplied(WKTPlayer sender, NetDataReader reader) {
            if (IsHost || !sender.IsHost || !reader.TryGetULong(out var key) || !reader.TryGetByte(out var index)) return;
            if (!m_eventsByKey.TryGetValue((key, index), out var reference)) {
                // A level this client has not built yet: applied once it is indexed.
                m_pendingApplied.Add((key, index));
                return;
            }
            ApplyFromHost(reference);
        }

        /// <summary>Client only: runs the world part of an event the host applied, once for once-only events.</summary>
        private void ApplyFromHost(EventRef reference) {
            if (reference.Owner == null || !m_applied.Record(reference.Key, reference.Index, reference.LevelId, IsOnce(reference))) return;
            if (reference.GroupStart)
                m_groupStarts[(reference.Key, reference.Index)] = Time.unscaledTime;
            Apply(reference, reference.Owner.transform, true);
            // A timer's local part runs here too, since this client's own timer never fires.
            if (reference.Autonomous && reference.Masks.HasLocal)
                Apply(reference, reference.Owner.transform, false);
        }

        /// <summary>
        /// Runs the events the host applied in levels indexed since. Level scripting that changes the route's levels waits
        /// until the loader is done: levels are indexed while it generates, and it must not lose levels under it.
        /// </summary>
        private void ApplyPendingEvents() {
            var loader = WorldLoader.instance;
            var loading = loader != null && WKTHostStreaming.IsLoading(loader);
            for (var i = m_pendingApplied.Count - 1; i >= 0; i--) {
                if (!m_eventsByKey.TryGetValue(m_pendingApplied[i], out var reference) || (loading && reference.Loads)) continue;
                m_pendingApplied.RemoveAt(i);
                ApplyFromHost(reference);
            }
        }

        /// <summary>Runs the world or the local listeners of an event on this machine.</summary>
        private static void Apply(EventRef reference, Transform? argument, bool world) {
            IsApplying = true;
            try {
                WKTUnityEventFields.InvokeFiltered(reference.Event, argument, reference.Masks, world);
            }
            catch (Exception e) {
                WKT.Instance.Logger.LogError($"Event {reference.FieldName} of {reference.Owner.name} threw: {e}");
            }
            finally {
                IsApplying = false;
            }
        }

        /// <summary>Whether the event runs only once: its own trigger only ever fires it once, or it changes the shared goo.</summary>
        private static bool IsOnce(EventRef reference) {
            if (reference.DrivesGoo) return true;
            var owner = reference.Owner;
            if (owner == null) return false;
            var runOnce = GetOnceFields(owner.GetType()).RunOnce;
            return runOnce != null && runOnce(owner);
        }

        private static OnceFields GetOnceFields(Type type) {
            if (s_onceFields.TryGetValue(type, out var fields)) return fields;
            fields = new OnceFields(FieldRef(type, "runOnce"));
            s_onceFields[type] = fields;
            return fields;
        }

        private static AccessTools.FieldRef<object, bool>? FieldRef(Type type, string name) {
            var field = AccessTools.Field(type, name);
            return field != null && field.FieldType == typeof(bool) ? AccessTools.FieldRefAccess<object, bool>(field) : null;
        }

        #endregion

        #region Doors

        /// <summary>
        /// Whether a door may open or close here: always while setting itself up, on the host unless it is a gate the grouping
        /// drives, and on a client only when the host says so.
        /// </summary>
        public bool AllowDoorChange(UT_Door door) {
            if (DoorStartDepth > 0 || IsDriving) return true;
            return IsHost && !m_gateDoors.Contains(door.GetInstanceID());
        }

        /// <summary>A door finished setting itself up. The host records its state; a client applies the host's if it has one.</summary>
        public void OnDoorStarted(UT_Door door) {
            m_startedDoors.Add(door.GetInstanceID());
            if (!m_doorKeys.TryGetValue(door.GetInstanceID(), out var key)) return;
            if (IsHost) {
                m_doorStates[key] = door.IsOpen() ? WKTGateState.Open : WKTGateState.Closed;
                return;
            }
            if (m_doorStates.TryGetValue(key, out var state))
                SetDoor(door, state == WKTGateState.Open);
        }

        /// <summary>Host only: a door opened or closed, so clients follow.</summary>
        public void OnHostDoorChanged(UT_Door door) {
            // A gate's state is the grouping's.
            if (!IsHost || m_gateDoors.Contains(door.GetInstanceID()) || !m_doorKeys.TryGetValue(door.GetInstanceID(), out var key)) return;
            SendDoorState(key, door.IsOpen() ? WKTGateState.Open : WKTGateState.Closed);
        }

        private void SendDoorState(ulong key, WKTGateState state) {
            if (m_doorStates.TryGetValue(key, out var known) && known == state) return;
            m_doorStates[key] = state;
            var writer = m_network.BeginMessage(WKTMessageId.DoorState);
            writer.Put(key);
            writer.Put((byte)state);
            m_network.Broadcast(writer, SendType.Reliable);
        }

        private void OnDoorState(WKTPlayer sender, NetDataReader reader) {
            if (IsHost || !sender.IsHost || !reader.TryGetULong(out var key) || !reader.TryGetByte(out var value) || value > (byte)WKTGateState.Closed) return;
            var state = (WKTGateState)value;
            m_doorStates[key] = state;
            if (m_doors.TryGetValue(key, out var door) && door != null && m_startedDoors.Contains(door.GetInstanceID()))
                SetDoor(door, state == WKTGateState.Open);
            else if (m_animatedDoors.TryGetValue(key, out var animated))
                SetAnimatedDoor(animated, state);
        }

        private void HandlePlayerConnected(WKTPlayer player) {
            if (!IsHost || player.IsHost) return;
            foreach (var pair in m_doorStates) {
                var writer = m_network.BeginMessage(WKTMessageId.DoorState);
                writer.Put(pair.Key);
                writer.Put((byte)pair.Value);
                m_network.SendToPlayer(player, writer, SendType.Reliable);
            }
            foreach (var entry in m_applied.Entries) {
                var writer = m_network.BeginMessage(WKTMessageId.EventApplied);
                writer.Put(entry.Key);
                writer.Put(entry.Index);
                m_network.SendToPlayer(player, writer, SendType.Reliable);
            }
            Buttons.SendAll(player);
        }

        private static void SetDoor(UT_Door door, bool open) {
            if (door.IsOpen() == open) return;
            IsDriving = true;
            try {
                if (open)
                    door.Open();
                else
                    door.Close();
            }
            finally {
                IsDriving = false;
            }
        }

        /// <summary>Moves an animated door.</summary>
        private static void SetAnimatedDoor(AnimatedDoor door, WKTGateState state) {
            // The level only ever closes these doors: its close animation starts their sound, and opening plays none.
            if (state == WKTGateState.Open && door.State != WKTGateState.Open)
                door.PlayMoveSound();
            door.State = state;
            door.Apply();
        }

        /// <summary>An animator only takes a bool while it runs: doors in levels that were inactive catch up here.</summary>
        private void ReconcileAnimatedDoors() {
            if (m_animatedDoors.Count == 0) return;
            m_keyScratch.Clear();
            foreach (var pair in m_animatedDoors) {
                if (pair.Value.Controller == null)
                    m_keyScratch.Add(pair.Key);
                else
                    pair.Value.Apply();
            }
            foreach (var key in m_keyScratch)
                m_animatedDoors.Remove(key);
            m_keyScratch.Clear();
        }

        #endregion

        #region Grouping

        private void UpdateGrouping() {
            var now = Time.unscaledTime;
            var deltaTime = m_lastGrouping < 0f ? 0f : Mathf.Min(now - m_lastGrouping, 1f);
            m_lastGrouping = now;
            for (var i = m_gates.Count - 1; i >= 0; i--) {
                if (m_gates[i].IsAlive) continue;
                m_grouping.Forget(m_gates[i].Key);
                m_gates.RemoveAt(i);
            }
            if (m_gates.Count == 0 || !CollectPlayers(out var capsule)) return;
            m_groupGates.Clear();
            foreach (var gate in m_gates) {
                var zone = gate.Zone;
                var zoneMin = zone - gate.ZoneExtents;
                var zoneMax = zone + gate.ZoneExtents;
                var room = GetGateRoom(gate);
                var doorway = gate.Doorway;
                m_groupGates.Add(new WKTGroupGate(gate.Key, zoneMin - capsule.Below, zoneMax + capsule.Above, doorway - gate.DoorwayExtents,
                    doorway + gate.DoorwayExtents, room, gate.IsOpen));
            }
            m_grouping.Evaluate(m_groupGates, m_groupPlayers, deltaTime, m_open, m_passed);
            for (var i = 0; i < m_gates.Count; i++) {
                var gate = m_gates[i];
                var room = m_groupGates[i].Room;
                var state = m_open[i] ? WKTGateState.Open : WKTGateState.Closed;
                if (gate.Animated != null) {
                    if (state == gate.Animated.State) continue;
                    LogGateChange(gate.Key, gate.Animated.Controller, state, room);
                    SetAnimatedDoor(gate.Animated, state);
                    SendDoorState(gate.Key, state);
                    continue;
                }
                if (!m_startedDoors.Contains(gate.Door!.GetInstanceID())) continue;
                if (m_open[i] != m_groupGates[i].Current) {
                    LogGateChange(gate.Key, gate.Door, state, room);
                    SetDoor(gate.Door, m_open[i]);
                }
                SendDoorState(gate.Key, state);
            }
        }

        /// <summary>The route room a gate leads into: its zone's, looked up just above the zone's bottom, capped by its level's.</summary>
        private static int GetGateRoom(in Gate gate) {
            var zone = gate.Zone;
            var probe = new Vector3(zone.x, zone.y - gate.ZoneExtents.y + c_room_probe_height, zone.z);
            return WKTDoorGrouping.GetGateRoom(WKTLevelAccess.GetRouteRoom(probe), WKTLevelAccess.GetLevelRoom(gate.Level));
        }

        /// <summary>Every gate the grouping moves, with the rooms it decided by and who it waits for, so play tests show what it did.</summary>
        private void LogGateChange(ulong key, Component door, WKTGateState state, int room) {
            var waiting = string.Empty;
            if (state == WKTGateState.Open) {
                foreach (var player in m_groupPlayers) {
                    if (player.Alive && !m_grouping.IsThrough(key, player.Id))
                        waiting += $" {player.Id} (room {player.Room}, y {player.Position.y:F1})";
                }
            }
            var suffix = waiting.Length > 0 ? $" Not through:{waiting}." : string.Empty;
            WKT.Instance.Logger.LogInfo($"Gate {door.name} (room {room}): {state}.{suffix}");
        }

        /// <summary>
        /// Every player this tick, with their route rooms, and how far the local player's capsule reaches from their position. A
        /// player counts as dead when either their pose or the run's deaths say so. False before the local player exists.
        /// </summary>
        private bool CollectPlayers(out CapsuleReach capsule) {
            capsule = default;
            m_groupPlayers.Clear();
            var local = ENT_Player.playerObject;
            var localPlayer = m_network.LocalPlayer;
            if (local == null || localPlayer == null) return false;
            capsule = CapsuleReach.Of(local.cCon);
            AddPlayer(localPlayer.Id, local.transform.position, local.dead);
            for (var i = 0; i < WKTPlayerPositions.RemoteCount; i++)
                AddPlayer(WKTPlayerPositions.GetRemoteId(i), WKTPlayerPositions.GetRemotePosition(i), WKTPlayerPositions.IsRemoteDead(i));
            return true;
        }

        private void AddPlayer(byte id, Vector3 position, bool dead) {
            var alive = !dead && !WKTPlayerDeaths.IsDead(id);
            var room = WKTDoorGrouping.RememberRoom(WKTLevelAccess.GetRouteRoom(position), ref m_lastRooms[id]);
            m_groupPlayers.Add(new WKTGroupPlayer(id, position, room, alive));
        }

        #endregion

        private sealed class EventRef {
            public readonly ulong Key;
            public readonly byte Index;
            public readonly UnityEventBase Event;
            public readonly MonoBehaviour Owner;
            /// <summary>Which listeners change the shared world, and so run because the host says so, and which act locally.</summary>
            public readonly WKTListenerMasks Masks;
            public readonly string FieldName;
            public readonly int LevelId;
            /// <summary>Fires by itself rather than from a player, so only the host's fires (see <see cref="s_inputTypes"/>).</summary>
            public readonly bool Autonomous;
            /// <summary>Starts something every living player must be near for (see <see cref="WKTGatherCheck"/>).</summary>
            public readonly bool GroupStart;
            /// <summary>Builds a branch, a shortcut: the others are pulled to whoever started it.</summary>
            public readonly bool PullsGroup;
            /// <summary>For a group start, the box every living player must be inside of, rather than around whoever set it off.</summary>
            public readonly BoxCollider? Zone;
            /// <summary>How many meters under <see cref="Zone"/> still count as in it.</summary>
            public readonly float ZoneBelow;
            /// <summary>Changes the shared goo, so runs once however many players set it off (see <see cref="WKTWorldWiring.IsGooTarget"/>).</summary>
            public readonly bool DrivesGoo;
            /// <summary>Changes which levels the route has, so never runs while the loader generates (see <see cref="WKTGatherCheck.IsLoading"/>).</summary>
            public readonly bool Loads;
            /// <summary>Whom it brings to whoever set it off (see <see cref="WKTGatherCheck.GetLoadPull(UnityEventBase)"/>).</summary>
            public readonly WKTLoadPull LoadPull;

            public EventRef(ulong key, byte index, UnityEventBase unityEvent, MonoBehaviour owner, WKTListenerMasks masks, string fieldName, int levelId,
                            bool groupStart = false, bool pullsGroup = false, bool drivesGoo = false, BoxCollider? zone = null, float zoneBelow = 0f,
                            bool loads = false, WKTLoadPull loadPull = WKTLoadPull.None) {
                GroupStart = groupStart;
                Loads = loads;
                LoadPull = loadPull;
                Zone = zone;
                ZoneBelow = zoneBelow;
                PullsGroup = pullsGroup;
                DrivesGoo = drivesGoo;
                Key = key;
                Index = index;
                Event = unityEvent;
                Owner = owner;
                Masks = masks;
                FieldName = fieldName;
                LevelId = levelId;
                Autonomous = owner is UT_Timer or UT_RandomTimer;
            }
        }

        /// <summary>A trigger's own once-only flag, where it has one.</summary>
        private sealed class OnceFields {
            public readonly AccessTools.FieldRef<object, bool>? RunOnce;

            public OnceFields(AccessTools.FieldRef<object, bool>? runOnce) => RunOnce = runOnce;
        }

        /// <summary>A door an animator moves through one bool, which the grouping sets instead of the level's trigger.</summary>
        private sealed class AnimatedDoor {
            public readonly UT_SimpleAnimatorController Controller;
            private readonly Animator m_animator;
            private readonly int m_parameter;
            /// <summary>The bool's value while the door is closed: what the level's own trigger sets.</summary>
            private readonly bool m_closedValue;
            private readonly AudioSource? m_sound;
            public WKTGateState State;

            public bool IsOpen => State == WKTGateState.Open;

            public AnimatedDoor(UT_SimpleAnimatorController controller, Animator animator, int parameter, bool closedValue) {
                Controller = controller;
                m_animator = animator;
                m_parameter = parameter;
                m_closedValue = closedValue;
                m_sound = controller.GetComponent<AudioSource>();
            }

            /// <summary>
            /// Plays the door's own machinery sound, the one its close animation starts, if it is in a running level and the
            /// local camera is within <see cref="OPEN_SOUND_RANGE"/>: someone waiting under it. Its sound carries hundreds of
            /// meters, so from afar it would play like an echo of another door.
            /// </summary>
            public void PlayMoveSound() {
                if (m_sound == null || m_sound.clip == null || !m_sound.isActiveAndEnabled) return;
                var listener = Camera.main;
                if (listener == null || (listener.transform.position - m_sound.transform.position).sqrMagnitude > OPEN_SOUND_RANGE * OPEN_SOUND_RANGE) return;
                m_sound.Play();
            }

            /// <summary>Sets the bool to match <see cref="State"/> when the animator runs and differs.</summary>
            public void Apply() {
                if (m_animator == null || !m_animator.isActiveAndEnabled || m_animator.runtimeAnimatorController == null) return;
                var value = IsOpen ? !m_closedValue : m_closedValue;
                if (m_animator.GetBool(m_parameter) != value)
                    m_animator.SetBool(m_parameter, value);
            }
        }

        private readonly struct Gate {
            public readonly ulong Key;
            /// <summary>The level the gate's door is part of.</summary>
            public readonly M_Level Level;
            private readonly Transform m_level;
            /// <summary>The middle of the trigger at which a player is through, relative to its level, which the world offset moves with.</summary>
            private readonly Vector3 m_localZone;
            /// <summary>The middle of the doorway in which a player blocks the gate, relative to its level.</summary>
            private readonly Vector3 m_localDoorway;
            public readonly Vector3 ZoneExtents;
            public readonly Vector3 DoorwayExtents;
            public readonly int LevelId;
            public readonly UT_Door? Door;
            public readonly AnimatedDoor? Animated;

            public Vector3 Zone => m_level.TransformPoint(m_localZone);
            public Vector3 Doorway => m_level.TransformPoint(m_localDoorway);
            public bool IsAlive => m_level != null && (Door != null || (Animated != null && Animated.Controller != null));
            public bool IsOpen => Door != null ? Door.IsOpen() : Animated!.IsOpen;

            public Gate(ulong key, M_Level level, Vector3 localZone, Vector3 zoneExtents, Vector3 localDoorway, Vector3 doorwayExtents, int levelId,
                        UT_Door? door, AnimatedDoor? animated) {
                Key = key;
                Level = level;
                m_level = level.transform;
                m_localZone = localZone;
                ZoneExtents = zoneExtents;
                m_localDoorway = localDoorway;
                DoorwayExtents = doorwayExtents;
                LevelId = levelId;
                Door = door;
                Animated = animated;
            }
        }

        /// <summary>
        /// How far a player's capsule reaches from their position, below and above, so a trigger can be grown by it: a player
        /// whose position is inside the grown box touches the trigger.
        /// </summary>
        private readonly struct CapsuleReach {
            public readonly Vector3 Below;
            public readonly Vector3 Above;

            private CapsuleReach(Vector3 below, Vector3 above) {
                Below = below;
                Above = above;
            }

            public static CapsuleReach Of(CharacterController? controller) {
                if (controller == null) return default;
                var half = controller.height * 0.5f;
                var center = controller.center.y;
                var radius = controller.radius;
                // The capsule's top touches the trigger's bottom first; its bottom leaves the trigger's top last.
                return new CapsuleReach(new Vector3(radius, center + half, radius), new Vector3(radius, half - center, radius));
            }
        }
    }
}
