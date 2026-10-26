using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using WhiteKnuckleTogether.Networking.LiteNetLib;
using SendType = Steamworks.Data.SendType;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// Keeps the level's buttons themselves alike on every machine in a synced co-op run: how often each was pressed (a
    /// one-press button turns grey for everyone), whether it can be pressed, and a toggle button's side (a fan switch shows
    /// off for everyone once someone turned the fans off). The events a press sets off travel through <see cref="WKTDoorSync"/>;
    /// only the button's state travels here, whatever pressed it: a hand, a rebar, a hammer or a grappling hook.
    /// <para>
    /// The machine where a button was pressed tells the host its new state; the host takes it and tells everyone. The host
    /// also tells everyone when level scripting turns one of its buttons on or off, and replays every state it knows to players
    /// who join. Owned by <see cref="WKTDoorSync"/>.
    /// </para>
    /// <para>
    /// A button that starts a mover (an elevator, a lift: <c>UT_PathMover.StartMove</c>) only presses once every living player
    /// is near it, so nobody is left below, and so do the buttons <see cref="WKTGatherCheck.CollectGatheredButtons"/> lists.
    /// </para>
    /// </summary>
    internal sealed class WKTButtonSync {
        /// <summary>How close to a button that starts a mover every living player must be for it to press.</summary>
        public const float MOVER_GATHER_RADIUS = 10f;
        public const string WAIT_LABEL = "Waiting for everyone to come to the button";
        private const string c_start_move = nameof(UT_PathMover.StartMove);
        /// <summary>How long the wait panel keeps saying who is missing after a button would not press.</summary>
        private const float c_notice_seconds = 4f;
        private readonly WKTNetwork m_network;
        private readonly WKTOneWayDoors m_oneWayDoors;
        private readonly Dictionary<ulong, CL_Button> m_buttons = new();
        private readonly Dictionary<int, ulong> m_keys = new();
        /// <summary>Host: every button's last state. Client: the host's states for buttons not indexed yet.</summary>
        private readonly Dictionary<ulong, WKTButtonState> m_states = new();
        private readonly Dictionary<int, bool> m_startsMover = new();
        /// <summary>By button instance id, whether it sets off a security field (see <see cref="WKTGatherCheck.IsPassStart"/>).</summary>
        private readonly Dictionary<int, bool> m_passStarts = new();
        /// <summary>By button instance id, what the listed buttons wait for.</summary>
        private readonly Dictionary<int, Gather> m_gathers = new();
        private readonly List<(string Path, float Radius, string? Zone, bool Above, string? Computer)> m_gatherScratch = new();
        /// <summary>Who picked a perk at which computer this run, for the buttons that wait for everyone to (see <see cref="Gather.Computer"/>).</summary>
        private readonly HashSet<(string Computer, byte Player)> m_perkPicks = new();
        private readonly List<CL_Button> m_scratch = new();
        private readonly HashSet<int> m_computerLevers = new();
        private CL_Button? m_noticeButton;
        private Gather m_noticeGather;
        private float m_noticeUntil = float.NegativeInfinity;

        public WKTButtonSync(WKTNetwork network, WKTOneWayDoors oneWayDoors) {
            m_network = network;
            m_oneWayDoors = oneWayDoors;
        }

        /// <summary>Set while a state from another machine is put on a button here, which is never sent back.</summary>
        public static bool IsApplying { get; private set; }
        private bool IsHost => m_network.Mode == NetworkMode.Host;

        public void IndexLevel(M_Level level) {
            level.GetComponentsInChildren(true, m_scratch);
            WKTComputerLevers.Collect(level, m_computerLevers);
            foreach (var button in m_scratch) {
                // A creature's own buttons run on the machine simulating it (see WKTEntityEvents), and a computer's lever is
                // each player's own (see WKTComputerLevers).
                if (button == null || (button is not CL_ToggleButton && button.GetComponentInParent<Denizen>(true) != null) ||
                    m_computerLevers.Contains(button.GetInstanceID()) || !WKTContentSeeding.TryGetKey(button, out var key)) continue;
                m_buttons[key] = button;
                m_keys[button.GetInstanceID()] = key;
                if (!IsHost && m_states.TryGetValue(key, out var state))
                    Apply(button, state.AsSettled());
            }
            m_scratch.Clear();
            m_computerLevers.Clear();
            IndexGatheredButtons(level);
        }

        private void IndexGatheredButtons(M_Level level) {
            WKTGatherCheck.CollectGatheredButtons(level.name, m_gatherScratch);
            foreach (var (path, radius, zonePath, above, computer) in m_gatherScratch) {
                var root = level.transform;
                if (root.Find(path) is not { } found || !found.TryGetComponent<CL_Button>(out var button)) {
                    WKT.Instance.Logger.LogWarning($"Level {level.name} has no gathering button {path}.");
                    continue;
                }
                BoxCollider? zone = null;
                if (zonePath != null && (root.Find(zonePath) is not { } zoneObject || !zoneObject.TryGetComponent(out zone))) {
                    WKT.Instance.Logger.LogWarning($"Level {level.name} has no gathering zone {zonePath} for {path}.");
                    continue;
                }
                m_gathers[button.GetInstanceID()] = new Gather(radius, zone, above, computer, this);
            }
            m_gatherScratch.Clear();
        }

        public void ForgetLevel(M_Level level) {
            level.GetComponentsInChildren(true, m_scratch);
            foreach (var button in m_scratch) {
                if (button == null) continue;
                var id = button.GetInstanceID();
                m_startsMover.Remove(id);
                m_passStarts.Remove(id);
                m_gathers.Remove(id);
                if (m_keys.Remove(id, out var key))
                    m_buttons.Remove(key);
            }
            m_scratch.Clear();
        }

        /// <summary>A button here took a press: everyone sees it pressed.</summary>
        public void OnLocalPress(CL_Button button) {
            if (!TryGetState(button, true, out var state)) return;
            if (IsHost)
                HostTake(state, null);
            else
                Send(state);
        }

        /// <summary>Host only: level scripting turned a button on or off here.</summary>
        public void OnHostInteractableChanged(CL_Button button) {
            if (IsHost && TryGetState(button, false, out var state))
                HostTake(state, null);
        }

        public void OnMessage(WKTPlayer sender, NetDataReader reader) {
            if (!WKTButtonState.TryRead(reader, out var state)) return;
            if (IsHost) {
                if (sender.IsHost) return;
                if (m_buttons.TryGetValue(state.Key, out var button) && button != null)
                    Apply(button, state);
                HostTake(state, sender);
                return;
            }
            if (!sender.IsHost) return;
            if (m_buttons.TryGetValue(state.Key, out var shown) && shown != null)
                Apply(shown, state);
            m_states[state.Key] = state.AsSettled();
        }

        /// <summary>Host only: every button's state and the perks picked so far, for a player who just joined.</summary>
        public void SendAll(WKTPlayer player) {
            foreach (var state in m_states.Values) {
                var writer = m_network.BeginMessage(WKTMessageId.ButtonState);
                state.Write(writer);
                m_network.SendToPlayer(player, writer, SendType.Reliable);
            }
            foreach (var (computer, picker) in m_perkPicks)
                m_network.SendToPlayer(player, WritePerkPicked(picker, computer), SendType.Reliable);
        }

        /// <summary>The local player picked a perk at the computer <paramref name="computerId"/>: everyone hears of it.</summary>
        public void OnLocalPerkPicked(string computerId) {
            if (string.IsNullOrEmpty(computerId) || m_network.LocalPlayer is not { } local) return;
            m_perkPicks.Add((computerId, local.Id));
            if (IsHost) {
                m_network.Broadcast(WritePerkPicked(local.Id, computerId), SendType.Reliable);
                return;
            }
            var writer = m_network.BeginMessage(WKTMessageId.PerkPicked);
            writer.Put(computerId);
            m_network.SendToHost(writer, SendType.Reliable);
        }

        public void OnPerkPickedMessage(WKTPlayer sender, NetDataReader reader) {
            if (IsHost) {
                if (sender.IsHost || !reader.TryGetString(out var computer) || string.IsNullOrEmpty(computer)) return;
                m_perkPicks.Add((computer, sender.Id));
                m_network.Broadcast(WritePerkPicked(sender.Id, computer), SendType.Reliable, sender);
                return;
            }
            if (sender.IsHost && reader.TryGetByte(out var picker) && reader.TryGetString(out var picked) && !string.IsNullOrEmpty(picked))
                m_perkPicks.Add((picked, picker));
        }

        private NetDataWriter WritePerkPicked(byte picker, string computerId) {
            var writer = m_network.BeginMessage(WKTMessageId.PerkPicked);
            writer.Put(picker);
            writer.Put(computerId);
            return writer;
        }

        /// <summary>How many living players picked a perk at <paramref name="computerId"/>.</summary>
        private int CountLivingPickers(string computerId) {
            var picked = 0;
            if (ENT_Player.playerObject is { dead: false } && m_network.LocalPlayer is { } local && m_perkPicks.Contains((computerId, local.Id)))
                picked++;
            for (var i = 0; i < WKTPlayerPositions.RemoteCount; i++) {
                if (!WKTPlayerPositions.IsRemoteDead(i) && m_perkPicks.Contains((computerId, WKTPlayerPositions.GetRemoteId(i))))
                    picked++;
            }
            return picked;
        }

        /// <summary>A new host owns the buttons as they are on its machine.</summary>
        public void OnHostChanged() {
            m_states.Clear();
            if (!IsHost) return;
            foreach (var button in m_buttons.Values) {
                if (button != null && TryGetState(button, false, out var state))
                    m_states[state.Key] = state;
            }
        }

        /// <summary>
        /// Whether a press of <paramref name="button"/> may go ahead here: a button that starts a mover waits for every living
        /// player to be near it, a listed one for them to be near it or in its room, and one that shuts a hatch for good for
        /// every living player to be above it (see <see cref="WKTOneWayDoors"/>). The wait panel says who is missing.
        /// </summary>
        public bool AllowPress(CL_Button button) {
            if (m_network.Session.IsRace) return true;
            if (!m_oneWayDoors.AllowPress(button)) return false;
            // A security field's button presses once everyone is up on its floor, without a word: the last one up presses it.
            if (IsPassStart(button) && !WKTGatherCheck.AllAbove(WKTGatherCheck.GetPassHeight(button))) return false;
            if (!TryGetGather(button, out var gather) || gather.IsMet(button, out _, out _)) return true;
            m_noticeButton = button;
            m_noticeGather = gather;
            m_noticeUntil = Time.unscaledTime + c_notice_seconds;
            return false;
        }

        /// <summary>For a while after a button would not press, the wait panel says who is still missing.</summary>
        public void Update() {
            if (m_noticeButton == null || Time.unscaledTime >= m_noticeUntil) return;
            if (m_noticeGather.IsMet(m_noticeButton, out var near, out var living)) {
                m_noticeUntil = float.NegativeInfinity;
                return;
            }
            WKTWaitHud.Report(m_noticeGather.Computer != null ? WKTGatherCheck.WAIT_PERK : m_noticeGather.Zone != null ? WKTGatherCheck.WAIT_IN_ROOM : WAIT_LABEL, near,
                living);
        }

        /// <summary>What a press of <paramref name="button"/> waits for, if anything.</summary>
        private bool TryGetGather(CL_Button button, out Gather gather) {
            if (m_gathers.TryGetValue(button.GetInstanceID(), out gather)) return true;
            gather = new Gather(MOVER_GATHER_RADIUS, null, false, null, this);
            return StartsMover(button);
        }

        private bool IsPassStart(CL_Button button) {
            var id = button.GetInstanceID();
            if (m_passStarts.TryGetValue(id, out var passStart)) return passStart;
            var level = button.GetComponentInParent<M_Level>(true);
            passStart = level != null && WKTGatherCheck.IsPassStart(level.name, WKTGatherCheck.PathBelow(button.transform, level.transform));
            m_passStarts[id] = passStart;
            return passStart;
        }

        private bool StartsMover(CL_Button button) {
            var id = button.GetInstanceID();
            if (m_startsMover.TryGetValue(id, out var starts)) return starts;
            // A Bazaar's hatches take everyone in or out, so they wait like a mover's button (see WKTBazaar).
            starts = CallsStartMove(button.activeEvent) || button is CL_ToggleButton toggle && (CallsStartMove(toggle.onEvent) || CallsStartMove(toggle.offEvent)) ||
                     WKTBazaar.IsHatch(button);
            m_startsMover[id] = starts;
            return starts;
        }

        /// <summary>Whether an event starts a mover, such as an elevator.</summary>
        public static bool CallsStartMove(UnityEngine.Events.UnityEventBase? unityEvent) {
            if (unityEvent == null) return false;
            var count = unityEvent.GetPersistentEventCount();
            for (var i = 0; i < count; i++) {
                if (unityEvent.GetPersistentTarget(i) is UT_PathMover && unityEvent.GetPersistentMethodName(i) == c_start_move) return true;
            }
            return false;
        }

        private void HostTake(WKTButtonState state, WKTPlayer? sender) {
            m_states[state.Key] = state.AsSettled();
            var writer = m_network.BeginMessage(WKTMessageId.ButtonState);
            state.Write(writer);
            m_network.Broadcast(writer, SendType.Reliable, sender);
        }

        private void Send(WKTButtonState state) {
            var writer = m_network.BeginMessage(WKTMessageId.ButtonState);
            state.Write(writer);
            m_network.SendToHost(writer, SendType.Reliable);
        }

        private bool TryGetState(CL_Button button, bool justPressed, out WKTButtonState state) {
            if (!m_keys.TryGetValue(button.GetInstanceID(), out var key)) {
                state = default;
                return false;
            }
            state = new WKTButtonState(key, Fields.Presses(button), button.hasBeenPressed, button.active, button is CL_ToggleButton { state: true }, justPressed);
            return true;
        }

        /// <summary>Puts a state on a button here without setting off its events, which arrive on their own.</summary>
        private static void Apply(CL_Button button, WKTButtonState state) {
            IsApplying = true;
            try {
                Fields.Presses(button) = state.Presses;
                button.hasBeenPressed = state.HasBeenPressed;
                button.active = state.IsActive;
                if (state.IsJustPressed) {
                    AudioManager.PlaySound(button.pressSound, button.transform.position);
                    if (button.isActiveAndEnabled && !Fields.Cooldown(button) && Fields.CooldownRoutine(button) is { } routine)
                        button.StartCoroutine(routine);
                }
                if (button is CL_ToggleButton toggle)
                    toggle.SetStateWithoutEvent(state.IsToggledOn);
                else
                    Fields.ChooseMaterial(button);
            }
            finally {
                IsApplying = false;
            }
        }

        /// <summary>
        /// Everyone a button waits for: inside <see cref="Zone"/> when set (with <see cref="Above"/>, anywhere above its floor),
        /// else within <see cref="Radius"/> of it. With a
        /// <see cref="Computer"/>, everyone having picked a perk at that computer will do too, and <c>near</c> counts the pickers.
        /// </summary>
        private readonly struct Gather {
            public readonly float Radius;
            public readonly BoxCollider? Zone;
            public readonly bool Above;
            public readonly string? Computer;
            private readonly WKTButtonSync m_owner;

            public Gather(float radius, BoxCollider? zone, bool above, string? computer, WKTButtonSync owner) {
                Radius = radius;
                Zone = zone;
                Above = above;
                Computer = computer;
                m_owner = owner;
            }

            public bool IsMet(CL_Button button, out int near, out int living) {
                var met = Zone == null ? WKTGatherCheck.AllNear(button.transform.position, Radius, out near, out living)
                    : Above ? WKTGatherCheck.AllAbove(WKTGatherCheck.GetFloor(Zone), out near, out living)
                    : WKTGatherCheck.AllInZone(Zone, 0f, out near, out living);
                if (met || Computer == null) return met;
                near = m_owner.CountLivingPickers(Computer);
                return near >= living;
            }
        }

        /// <summary>The button's private parts, found when first used: the test runtime cannot load the game's types eagerly.</summary>
        private static class Fields {
            public static readonly AccessTools.FieldRef<CL_Button, int> Presses = AccessTools.FieldRefAccess<CL_Button, int>("presses");
            public static readonly AccessTools.FieldRef<CL_Button, bool> Cooldown = AccessTools.FieldRefAccess<CL_Button, bool>("cooldown");
            private static readonly Func<CL_Button, IEnumerator> s_cooldown =
                AccessTools.MethodDelegate<Func<CL_Button, IEnumerator>>(AccessTools.Method(typeof(CL_Button), "Cooldown"));
            private static readonly Action<CL_Button> s_chooseMaterial =
                AccessTools.MethodDelegate<Action<CL_Button>>(AccessTools.Method(typeof(CL_Button), "ChooseMaterial"));

            public static IEnumerator? CooldownRoutine(CL_Button button) => s_cooldown(button);

            public static void ChooseMaterial(CL_Button button) => s_chooseMaterial(button);
        }
    }
}
