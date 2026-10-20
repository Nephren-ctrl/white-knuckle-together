using System;
using System.Collections.Generic;
using HarmonyLib;
using Steamworks.Data;
using UnityEngine;
using WhiteKnuckleTogether.Networking.LiteNetLib;
using TongueState = Barnacle_Tongue_Core.TongueState;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// Barnacle tongues in a synced co-op run. The host decides what each tongue does: when it reaches, where it aims and when
    /// it pulls back, and tells clients whenever that changes. A client's puppet tongue animates from that, and checks only
    /// whether it hits that client's own player. When it does, the client grabs its player at once and tells the host, which
    /// gives the tongue to the first player to claim it. A player who breaks free on their own screen tells the host too, and
    /// so does a player who breaks a tongue by hitting it, whoever it held.
    /// Owned by <see cref="WKTWorldSync"/>.
    /// </summary>
    internal sealed class WKTTongueSync {
        /// <summary>How far above a remote player's feet a tongue holding them reaches.</summary>
        private const float c_remote_hold_height = 1f;
        private const string c_latch_sound = "barnacle:latch";
        private static readonly AccessTools.FieldRef<Barnacle_Tongue_Core, Barnacle_Tongue_Interface_Module_Basic> s_interface =
            AccessTools.FieldRefAccess<Barnacle_Tongue_Core, Barnacle_Tongue_Interface_Module_Basic>("barnacleInterface");
        private static readonly AccessTools.FieldRef<Barnacle_Tongue_Basic, TongueState> s_state = AccessTools.FieldRefAccess<Barnacle_Tongue_Basic, TongueState>("state");
        private static readonly AccessTools.FieldRef<Barnacle_Tongue_Basic, Vector3> s_targetPoint = AccessTools.FieldRefAccess<Barnacle_Tongue_Basic, Vector3>("targetPoint");
        private static readonly AccessTools.FieldRef<Barnacle_Tongue_Basic, Vector3> s_aimPoint = AccessTools.FieldRefAccess<Barnacle_Tongue_Basic, Vector3>("aimPoint");
        private static readonly AccessTools.FieldRef<Barnacle_Tongue_Basic, Vector3> s_reachPoint = AccessTools.FieldRefAccess<Barnacle_Tongue_Basic, Vector3>("reachPoint");
        private static readonly AccessTools.FieldRef<Barnacle_Tongue_Basic, bool> s_hasLatched = AccessTools.FieldRefAccess<Barnacle_Tongue_Basic, bool>("hasLatched");
        private static readonly AccessTools.FieldRef<Barnacle_Tongue_Basic, GameEntity> s_grabbedEntity = AccessTools.FieldRefAccess<Barnacle_Tongue_Basic, GameEntity>("grabbedEntity");
        private static readonly AccessTools.FieldRef<Barnacle_Tongue_Basic, Vector3> s_grabbedLocalPosition =
            AccessTools.FieldRefAccess<Barnacle_Tongue_Basic, Vector3>("grabbedLocalPosition");
        private static readonly AccessTools.FieldRef<Barnacle_Tongue_Basic, float> s_pullStrength = AccessTools.FieldRefAccess<Barnacle_Tongue_Basic, float>("pullStrength");
        private static readonly Action<Barnacle_Tongue_Basic> s_scanForHitEntity = AccessTools.MethodDelegate<Action<Barnacle_Tongue_Basic>>(
            AccessTools.Method(typeof(Barnacle_Tongue_Basic), "ScanForHitEntity"));
        private static readonly Action<Barnacle_Tongue_Basic> s_pullTarget = AccessTools.MethodDelegate<Action<Barnacle_Tongue_Basic>>(
            AccessTools.Method(typeof(Barnacle_Tongue_Basic), "PullTarget"));
        private readonly WKTWorldSync m_sync;
        private readonly WKTNetwork m_network;
        /// <summary>Host: what each tongue last told clients. Client: what the host last said about each tongue.</summary>
        private readonly Dictionary<(ushort Handle, byte Tongue), TongueRecord> m_records = new();
        private readonly List<(ushort, byte)> m_keyScratch = new();

        public WKTTongueSync(WKTWorldSync sync, WKTNetwork network) {
            m_sync = sync;
            m_network = network;
        }

        /// <summary>The tongues of a barnacle, when it has the basic kind every barnacle uses.</summary>
        private static List<Barnacle_Tongue_Basic>? GetTongues(DEN_Barnacle barnacle) =>
            barnacle.tongueInterface is Barnacle_Tongue_Interface_Module_Basic basic ? basic.tongues : null;

        private static DEN_Barnacle? GetBarnacle(Barnacle_Tongue_Basic tongue) => s_interface(tongue)?.GetBarnacle();

        #region Host

        /// <summary>Host only: tells clients about every tongue of <paramref name="barnacle"/> that changed. Called each send tick.</summary>
        public void HostPoll(ushort handle, DEN_Barnacle barnacle) {
            var tongues = GetTongues(barnacle);
            if (tongues == null) return;
            var offset = WKTWorldOffset.Get();
            for (var i = 0; i < tongues.Count && i <= byte.MaxValue; i++) {
                var tongue = tongues[i];
                if (tongue == null) continue;
                var state = (byte)s_state(tongue);
                var grabbed = GetPlayerId(s_grabbedEntity(tongue));
                var target = WKTWorldOffset.ToWire(s_targetPoint(tongue), offset);
                var key = (handle, (byte)i);
                if (!m_records.TryGetValue(key, out var record)) {
                    record = new TongueRecord();
                    m_records.Add(key, record);
                }
                else if (record.State == state && record.Grabbed == grabbed && (record.Target - target).sqrMagnitude < 0.0001f) continue;
                record.State = state;
                record.Grabbed = grabbed;
                record.Target = target;
                var writer = m_network.BeginMessage(WKTMessageId.TongueState);
                new WKTTongueEvent(handle, (byte)i, state, target, grabbed).Write(writer);
                m_network.Broadcast(writer, SendType.Reliable);
            }
        }

        /// <summary>The player a tongue holds: the host's own, or the remote player a proxy stands in for.</summary>
        private byte GetPlayerId(GameEntity? entity) {
            if (entity == null) return WKTTongueEvent.NOBODY;
            if (entity is WKTPlayerProxy proxy) return proxy.PlayerId;
            var local = m_network.LocalPlayer;
            return local != null && entity == ENT_Player.playerObject ? local.Id : WKTTongueEvent.NOBODY;
        }

        /// <summary>Host only: a client's player was grabbed by, broke free of, or broke one of a barnacle's tongues.</summary>
        public void OnPlayerHit(WKTPlayer sender, DEN_Barnacle barnacle, in WKTPlayerHit hit) {
            var tongues = GetTongues(barnacle);
            if (tongues == null || hit.Part >= tongues.Count || tongues[hit.Part] == null || barnacle.dead) return;
            var tongue = tongues[hit.Part];
            WKTPlayerProxy.TryGet(sender.Id, out var proxy);
            var held = s_grabbedEntity(tongue);
            switch (hit.Kind) {
                case WKTPlayerHitKind.Grabbed:
                    if (proxy == null || held == proxy) return;
                    if (held != null || s_state(tongue) == TongueState.retracting) {
                        // Someone was first, or the tongue already let go: the client lets go too.
                        SendCurrent(sender, hit.Handle, hit.Part, tongue);
                        return;
                    }
                    s_grabbedEntity(tongue) = proxy;
                    s_grabbedLocalPosition(tongue) = Vector3.zero;
                    s_pullStrength(tongue) = 0f;
                    s_state(tongue) = TongueState.grabbing;
                    WKTEntitySounds.PlayLocal(barnacle.clipHandler, "barnacle:grabbed-object");
                    barnacle.OnGrab();
                    return;
                case WKTPlayerHitKind.Released:
                    if (proxy != null && held == proxy)
                        tongue.DropTarget();
                    return;
                case WKTPlayerHitKind.Broken:
                    // The game's own break: the tongue lets go of whoever it holds and pulls back, which reaches every client.
                    if (s_state(tongue) is TongueState.reaching or TongueState.grabbing)
                        tongue.KillTongue();
                    return;
            }
        }

        private void SendCurrent(WKTPlayer player, ushort handle, byte index, Barnacle_Tongue_Basic tongue) {
            var writer = m_network.BeginMessage(WKTMessageId.TongueState);
            new WKTTongueEvent(handle, index, (byte)s_state(tongue), WKTWorldOffset.ToWire(s_targetPoint(tongue), WKTWorldOffset.Get()),
                GetPlayerId(s_grabbedEntity(tongue))).Write(writer);
            m_network.SendToPlayer(player, writer, SendType.Reliable);
        }

        /// <summary>Host only: tells a player who (re)connected where every tongue stands.</summary>
        public void SendAll(WKTPlayer player) {
            foreach (var pair in m_records) {
                var record = pair.Value;
                var writer = m_network.BeginMessage(WKTMessageId.TongueState);
                new WKTTongueEvent(pair.Key.Handle, pair.Key.Tongue, record.State, record.Target, record.Grabbed).Write(writer);
                m_network.SendToPlayer(player, writer, SendType.Reliable);
            }
        }

        /// <summary>
        /// Host only: whether a tongue's own hit check may run. It never grabs a remote player itself: that is decided on their
        /// screen and claimed with a <see cref="WKTPlayerHit"/>.
        /// </summary>
        public static bool AllowHostScan(Barnacle_Tongue_Basic tongue) {
            var tongueInterface = s_interface(tongue);
            if (tongueInterface == null || tongue.tongue == null) return true;
            return !Physics.Linecast(tongue.tongue.position, s_reachPoint(tongue), out var hit, tongueInterface.playerMask) ||
                !WKTPlayerProxy.IsProxyCollider(hit.collider);
        }

        #endregion

        #region Client

        public void OnTongueEvent(WKTPlayer sender, NetDataReader reader) {
            if (m_sync.IsHost || !sender.IsHost || !WKTTongueEvent.TryRead(reader, out var tongueEvent)) return;
            var key = (tongueEvent.Handle, tongueEvent.Tongue);
            if (!m_records.TryGetValue(key, out var record)) {
                record = new TongueRecord();
                m_records.Add(key, record);
            }
            record.State = tongueEvent.State;
            record.Grabbed = tongueEvent.Grabbed;
            record.Target = tongueEvent.Target;
            if (!m_sync.TryGetIdentity(tongueEvent.Handle, out var identity) || identity!.Entity is not DEN_Barnacle barnacle || barnacle.dead) return;
            var tongues = GetTongues(barnacle);
            if (tongues == null || tongueEvent.Tongue >= tongues.Count || tongues[tongueEvent.Tongue] == null) return;
            // A barnacle in a level that just loaded here may not have set its tongues up yet; the record waits for the next.
            if (s_interface(tongues[tongueEvent.Tongue]) == null) return;
            // The puppet's reach and retract sounds play here, as the host's tongue made them.
            WKTLocalReplayScope.Enter();
            try {
                Apply(tongueEvent.Handle, tongueEvent.Tongue, tongues[tongueEvent.Tongue], record);
            }
            finally {
                WKTLocalReplayScope.Exit();
            }
        }

        /// <summary>Client only: makes a puppet tongue do what the host's does.</summary>
        private void Apply(ushort handle, byte index, Barnacle_Tongue_Basic tongue, TongueRecord record) {
            var local = ENT_Player.playerObject;
            var localId = m_network.LocalPlayer?.Id ?? WKTTongueEvent.NOBODY;
            var holdsLocal = local != null && s_grabbedEntity(tongue) == local;
            var target = WKTWorldOffset.FromWire(record.Target, WKTWorldOffset.Get());
            switch ((TongueState)record.State) {
                case TongueState.reaching:
                    if (s_state(tongue) != TongueState.reaching && !holdsLocal)
                        Extend(tongue, target);
                    else
                        s_targetPoint(tongue) = target;
                    break;
                case TongueState.grabbing when record.Grabbed == localId:
                    record.ClaimPending = false;
                    // The host gave this tongue our player, but it no longer holds us here: let it go there too.
                    if (!holdsLocal)
                        SendHit(handle, WKTPlayerHitKind.Released, index);
                    break;
                case TongueState.grabbing:
                    if (holdsLocal)
                        Release(tongue, record);
                    record.ClaimPending = false;
                    if (s_state(tongue) != TongueState.reaching)
                        Extend(tongue, target);
                    s_hasLatched(tongue) = true;
                    break;
                default:
                    record.ClaimPending = false;
                    if (holdsLocal)
                        Release(tongue, record);
                    else if (s_state(tongue) is TongueState.reaching or TongueState.grabbing)
                        tongue.Retract();
                    break;
            }
        }

        /// <summary>The tongue's latch sound, as <c>Barnacle_Tongue_Basic</c> plays it where the tongue lands.</summary>
        private static void PlayLatch(DEN_Barnacle barnacle, Vector3 point) {
            if (barnacle.clipHandler == null) return;
            WKTLocalReplayScope.Enter();
            try {
                barnacle.clipHandler.PlaySound(c_latch_sound, point);
            }
            finally {
                WKTLocalReplayScope.Exit();
            }
        }

        private static void Extend(Barnacle_Tongue_Basic tongue, Vector3 target) {
            s_aimPoint(tongue) = target;
            tongue.Extend();
        }

        /// <summary>Lets go of the local player because the host said so, which is not reported back.</summary>
        private static void Release(Barnacle_Tongue_Basic tongue, TongueRecord record) {
            record.HeldLocal = false;
            tongue.DropTarget();
        }

        /// <summary>
        /// Client only: runs instead of a puppet tongue's own AI. It never decides where to reach; it follows the host, hits only
        /// the local player, pulls the local player while holding them, and reports grabs and escapes.
        /// </summary>
        public void PuppetTick(Barnacle_Tongue_Basic tongue, DEN_Barnacle barnacle, float distance) {
            if (!barnacle.TryGetComponent<WKTNetIdentity>(out var identity) || identity.Handle == 0) return;
            var tongues = GetTongues(barnacle);
            var index = tongues != null ? tongues.IndexOf(tongue) : -1;
            if (index < 0 || index > byte.MaxValue) return;
            var key = (identity.Handle, (byte)index);
            if (!m_records.TryGetValue(key, out var record)) {
                record = new TongueRecord();
                m_records.Add(key, record);
            }
            var local = ENT_Player.playerObject;
            var localId = m_network.LocalPlayer?.Id ?? WKTTongueEvent.NOBODY;
            switch (s_state(tongue)) {
                case TongueState.retracting:
                    if (Vector3.Distance(s_reachPoint(tongue), s_targetPoint(tongue)) < 0.5f) {
                        s_state(tongue) = TongueState.searching;
                        if (tongue.hideWhenRetracted && tongue.tongue != null)
                            tongue.tongue.gameObject.SetActive(false);
                    }
                    break;
                case TongueState.searching:
                    if (tongue.hideWhenRetracted && tongue.tongue != null)
                        tongue.tongue.gameObject.SetActive(false);
                    break;
                case TongueState.reaching when record.Grabbed != WKTTongueEvent.NOBODY && record.Grabbed != localId:
                    // Holding another player: the tongue follows them.
                    if (TryGetRemotePosition(record.Grabbed, out var held))
                        s_targetPoint(tongue) = held + Vector3.up * c_remote_hold_height;
                    s_hasLatched(tongue) = true;
                    break;
                case TongueState.reaching:
                    if (distance < 0.2f && !s_hasLatched(tongue)) {
                        s_hasLatched(tongue) = true;
                        s_reachPoint(tongue) = s_targetPoint(tongue);
                        PlayLatch(barnacle, s_reachPoint(tongue));
                    }
                    if (local != null && !local.dead && HitsLocalPlayer(tongue, local))
                        ScanLocal(tongue);
                    if (local != null && s_grabbedEntity(tongue) == local && !record.HeldLocal) {
                        record.ClaimPending = true;
                        SendHit(identity.Handle, WKTPlayerHitKind.Grabbed, (byte)index);
                    }
                    break;
                case TongueState.grabbing:
                    if (local != null && s_grabbedEntity(tongue) == local)
                        s_pullTarget(tongue);
                    break;
            }
            var holdsLocal = local != null && s_grabbedEntity(tongue) == local;
            if (record.HeldLocal && !holdsLocal && !record.ClaimPending)
                SendHit(identity.Handle, WKTPlayerHitKind.Released, (byte)index);
            record.HeldLocal = holdsLocal;
        }

        /// <summary>The game's own hit check, limited to the local player: a hit on anything else is the host's to decide.</summary>
        private static void ScanLocal(Barnacle_Tongue_Basic tongue) {
            WKTPuppetHitScope.Enter();
            try {
                s_scanForHitEntity(tongue);
            }
            finally {
                WKTPuppetHitScope.Exit();
            }
        }

        private static bool HitsLocalPlayer(Barnacle_Tongue_Basic tongue, ENT_Player local) {
            var tongueInterface = s_interface(tongue);
            return tongueInterface != null && tongue.tongue != null &&
                Physics.Linecast(tongue.tongue.position, s_reachPoint(tongue), out var hit, tongueInterface.playerMask) &&
                hit.collider.GetComponentInParent<ENT_Player>() == local;
        }

        private static bool TryGetRemotePosition(byte playerId, out Vector3 position) {
            for (var i = 0; i < WKTPlayerPositions.RemoteCount; i++) {
                if (WKTPlayerPositions.GetRemoteId(i) != playerId) continue;
                position = WKTPlayerPositions.GetRemotePosition(i);
                return true;
            }
            position = default;
            return false;
        }

        private void SendHit(ushort handle, WKTPlayerHitKind kind, byte part) {
            var writer = m_network.BeginMessage(WKTMessageId.PlayerHit);
            new WKTPlayerHit(handle, kind, part).Write(writer);
            m_network.SendToHost(writer, SendType.Reliable);
        }

        /// <summary>
        /// Client only: a puppet tongue broke here, from the local player's hits on it. The host's tongue breaks the same way.
        /// The host's word only ever retracts a tongue, never breaks it, so this is always the local player's doing.
        /// </summary>
        public void OnPuppetTongueKilled(Barnacle_Tongue_Basic tongue, DEN_Barnacle barnacle) {
            if (!barnacle.TryGetComponent<WKTNetIdentity>(out var identity) || identity.Handle == 0) return;
            var tongues = GetTongues(barnacle);
            var index = tongues != null ? tongues.IndexOf(tongue) : -1;
            if (index < 0 || index > byte.MaxValue) return;
            SendHit(identity.Handle, WKTPlayerHitKind.Broken, (byte)index);
        }

        /// <summary>Whether a tongue belongs to a client puppet, whose own AI must not run.</summary>
        public static bool IsPuppetTongue(Barnacle_Tongue_Basic tongue, out DEN_Barnacle? barnacle) {
            barnacle = GetBarnacle(tongue);
            return barnacle != null && WKTWorldSync.IsPuppet(barnacle);
        }

        #endregion

        /// <summary>Forgets a barnacle that went away.</summary>
        public void Forget(ushort handle) {
            m_keyScratch.Clear();
            foreach (var key in m_records.Keys) {
                if (key.Handle == handle)
                    m_keyScratch.Add(key);
            }
            foreach (var key in m_keyScratch)
                m_records.Remove(key);
            m_keyScratch.Clear();
        }

        public void Clear() => m_records.Clear();

        private sealed class TongueRecord {
            public byte State;
            public byte Grabbed = WKTTongueEvent.NOBODY;
            /// <summary>Wire position of where the tongue reaches.</summary>
            public Vector3 Target;
            /// <summary>Client only: the tongue held the local player last frame.</summary>
            public bool HeldLocal;
            /// <summary>Client only: a grab was reported and the host has not answered yet.</summary>
            public bool ClaimPending;
        }
    }
}
