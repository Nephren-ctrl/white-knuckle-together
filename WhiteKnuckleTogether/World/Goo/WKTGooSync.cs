using System;
using JetBrains.Annotations;
using Steamworks.Data;
using UnityEngine;
using WhiteKnuckleTogether.Networking.LiteNetLib;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// Shares the death goo of a synced co-op run. The host's goo rises as the game makes it, keeping up with the players
    /// picked in the lobby (see <see cref="WKTGooRubberband"/>), and the host streams its height. Each client's goo sits at
    /// that height, and the client's own game still decides whether the goo got its player. Lives next to
    /// <see cref="WKTRunController"/> and only exists during a synced co-op run.
    /// </summary>
    internal sealed class WKTGooSync : MonoBehaviour {
        private const SendType c_send_type = SendType.Unreliable | SendType.NoNagle;
        private readonly WKTGooSnapshotBuffer m_buffer = new();
        private readonly WKTSendClock m_sendClock = new();
        private WKTNetwork m_network = null!;
        private Action m_onHostChanged = null!;
        /// <summary>Client only: the newest state the host sent, carried on if this machine takes over.</summary>
        private WKTGooPose m_latest;
        private bool m_hasLatest;

        /// <summary>The goo sync of the synced co-op run in progress. Null everywhere else, including singleplayer.</summary>
        public static WKTGooSync? Instance { get; private set; }
        private bool IsHost => m_network.Mode == NetworkMode.Host;

        [UsedImplicitly]
        private void Awake() {
            m_network = WKT.Instance.Network;
            if (!m_network.Session.IsSyncedWorld) {
                enabled = false;
                return;
            }
            Instance = this;
            m_network.Messages.Register(WKTMessageId.GooState, OnGooState);
            m_onHostChanged = OnHostChanged;
            m_network.OnHostChanged += m_onHostChanged;
        }

        [UsedImplicitly]
        private void OnDestroy() {
            if (Instance != this) return;
            Instance = null;
            if (WKT.InstanceOrNull == null) return;
            m_network.Messages.Unregister(WKTMessageId.GooState);
            m_network.OnHostChanged -= m_onHostChanged;
        }

        [UsedImplicitly]
        private void Update() {
            if (!m_network.IsMultiplayerRun || !IsHost || !m_sendClock.TryAdvance(Time.realtimeSinceStartupAsDouble, out var sequence)) return;
            var goo = DEN_DeathFloor.instance;
            if (goo == null || !goo.gameObject.activeInHierarchy) return;
            byte flags = 0;
            if (WKTGooFields.Active(goo))
                flags |= WKTGooPose.FLAG_ACTIVE;
            if (WKTGooFields.HasStarted(goo))
                flags |= WKTGooPose.FLAG_STARTED;
            var height = WKTWorldOffset.ToWire(goo.transform.position, WKTWorldOffset.Get()).y;
            var writer = m_network.BeginMessage(WKTMessageId.GooState);
            new WKTGooPose(height, goo.speed, flags).Write(writer, sequence);
            m_network.Broadcast(writer, c_send_type);
        }

        /// <summary>
        /// Client only: puts <paramref name="goo"/> where the host's is, under the local player. Called around the goo's own
        /// tick, so the game's kill check, sounds and corruption height use the shared height and its own movement is undone.
        /// </summary>
        public static void ApplyOnClient(DEN_DeathFloor goo) {
            var sync = Instance;
            if (sync != null && !sync.IsHost)
                sync.Apply(goo);
        }

        private void Apply(DEN_DeathFloor goo) {
            if (!m_buffer.TrySample(Time.realtimeSinceStartupAsDouble, out var pose)) return;
            var gooTransform = goo.transform;
            var position = gooTransform.position;
            var player = ENT_Player.playerObject;
            if (player != null) {
                var playerPosition = player.transform.position;
                position.x = playerPosition.x;
                position.z = playerPosition.z;
            }
            position.y = WKTWorldOffset.FromWire(pose.Position, WKTWorldOffset.Get()).y;
            gooTransform.position = position;
            // A world recentre switches the goo off for a moment on this machine alone.
            if (!WKTGooFields.IsRecentering(goo))
                WKTGooFields.Active(goo) = pose.IsActive;
        }

        private void OnGooState(WKTPlayer sender, NetDataReader reader) {
            if (IsHost || !sender.IsHost || !WKTGooPose.TryRead(reader, out var sequence, out var pose)) return;
            if (!m_buffer.Add(sequence, pose, Time.realtimeSinceStartupAsDouble)) return;
            m_latest = pose;
            m_hasLatest = true;
        }

        /// <summary>
        /// Each host counts its own sequence, so states from the old one are dropped. A client that took over keeps the goo
        /// where it is, at the old host's height, and carries on rising at the old host's speed.
        /// </summary>
        private void OnHostChanged() {
            m_buffer.Clear();
            m_sendClock.Reset();
            if (IsHost && m_hasLatest) {
                var goo = DEN_DeathFloor.instance;
                if (goo != null) {
                    goo.speed = m_latest.Speed;
                    WKTGooFields.HasStarted(goo) = m_latest.HasStarted;
                    if (!WKTGooFields.IsRecentering(goo))
                        WKTGooFields.Active(goo) = m_latest.IsActive;
                }
            }
            m_hasLatest = false;
        }
    }
}
