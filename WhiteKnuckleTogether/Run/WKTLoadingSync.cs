using System.Collections.Generic;
using JetBrains.Annotations;
using UnityEngine;
using WhiteKnuckleTogether.Networking.LiteNetLib;
using SendType = Steamworks.Data.SendType;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// Keeps a synced co-op run in step while any machine loads. The game freezes itself while it generates the next levels
    /// ("Loading...", <c>CL_GameManager.SetLoading</c>), which each machine does when its own player reaches the end of what is
    /// built. Each machine tells the host when it starts and stops loading; while anyone is loading, the host holds every
    /// machine frozen too, showing how many players it waits for, and lets everyone go together. A machine that never says it
    /// finished is let go after <see cref="TIMEOUT"/> seconds.
    /// </summary>
    internal sealed class WKTLoadingSync : MonoBehaviour {
        public const float TIMEOUT = 30f;
        private const float c_toast_interval = 2.5f;
        private readonly Dictionary<byte, float> m_loading = new();
        private readonly List<byte> m_scratch = new();
        private WKTNetwork m_network = null!;
        private System.Action<WKTPlayer> m_onPlayerDisconnected = null!;
        private bool m_localLoading;
        private byte m_sentCount;
        private float m_nextToast;

        public static WKTLoadingSync? Instance { get; private set; }
        /// <summary>Players the run waits for, as the host last said. Above zero, every machine is frozen.</summary>
        public static int Waiting { get; private set; }

        [UsedImplicitly]
        private void Awake() {
            m_network = WKT.Instance.Network;
            if (!m_network.Session.IsSyncedWorld) {
                enabled = false;
                return;
            }
            Instance = this;
            m_network.Messages.Register(WKTMessageId.LoadingState, OnLoadingState);
            m_network.Messages.Register(WKTMessageId.LoadingHold, OnLoadingHold);
            m_onPlayerDisconnected = OnPlayerDisconnected;
            m_network.OnPlayerDisconnected += m_onPlayerDisconnected;
        }

        [UsedImplicitly]
        private void OnDestroy() {
            if (Instance != this) return;
            Instance = null;
            SetWaiting(0);
            if (WKT.InstanceOrNull == null) return;
            m_network.Messages.Unregister(WKTMessageId.LoadingState);
            m_network.Messages.Unregister(WKTMessageId.LoadingHold);
            m_network.OnPlayerDisconnected -= m_onPlayerDisconnected;
        }

        [UsedImplicitly]
        private void Update() {
            if (m_network.Mode == NetworkMode.Host) {
                m_scratch.Clear();
                foreach (var pair in m_loading) {
                    if (Time.unscaledTime - pair.Value > TIMEOUT)
                        m_scratch.Add(pair.Key);
                }
                foreach (var id in m_scratch) {
                    m_loading.Remove(id);
                    WKT.Instance.Logger.LogWarning($"Player {id} did not finish loading within {TIMEOUT} s. The run goes on without waiting.");
                }
                if (m_scratch.Count > 0)
                    Broadcast();
            }
            if (Waiting == 0 || Time.unscaledTime < m_nextToast) return;
            m_nextToast = Time.unscaledTime + c_toast_interval;
            WKTToast.Show(Waiting == 1 ? "Waiting for 1 player to load..." : $"Waiting for {Waiting} players to load...");
        }

        /// <summary>This machine started or finished loading.</summary>
        public void OnLocalLoading(bool loading) {
            if (loading == m_localLoading) return;
            m_localLoading = loading;
            if (m_network.Mode == NetworkMode.Host) {
                if (m_network.LocalPlayer is { } local)
                    SetLoading(local.Id, loading);
                return;
            }
            var writer = m_network.BeginMessage(WKTMessageId.LoadingState);
            writer.Put(loading);
            m_network.SendToHost(writer, SendType.Reliable);
        }

        private void OnLoadingState(WKTPlayer sender, NetDataReader reader) {
            if (m_network.Mode != NetworkMode.Host || sender.IsHost || !reader.TryGetBool(out var loading)) return;
            SetLoading(sender.Id, loading);
        }

        private void SetLoading(byte playerId, bool loading) {
            var changed = loading ? m_loading.TryAdd(playerId, Time.unscaledTime) : m_loading.Remove(playerId);
            if (changed)
                Broadcast();
        }

        private void OnPlayerDisconnected(WKTPlayer player) {
            if (m_network.Mode == NetworkMode.Host && m_loading.Remove(player.Id))
                Broadcast();
        }

        /// <summary>Host only: tells everyone how many players the run waits for.</summary>
        private void Broadcast() {
            var count = (byte)Mathf.Min(m_loading.Count, byte.MaxValue);
            if (count == m_sentCount && count == Waiting) return;
            m_sentCount = count;
            SetWaiting(count);
            var writer = m_network.BeginMessage(WKTMessageId.LoadingHold);
            writer.Put(count);
            m_network.Broadcast(writer, SendType.Reliable);
        }

        private void OnLoadingHold(WKTPlayer sender, NetDataReader reader) {
            if (!sender.IsHost || m_network.Mode == NetworkMode.Host || !reader.TryGetByte(out var count)) return;
            SetWaiting(count);
        }

        private void SetWaiting(int count) {
            if (count == Waiting) return;
            var wasHolding = Waiting > 0;
            Waiting = count;
            // The game's own "Loading..." stays up while this machine loads; otherwise it shows only while held.
            if (wasHolding != count > 0 && !m_localLoading)
                CL_UIManager.SetLoading(count > 0);
            m_nextToast = 0f;
            CL_GameManager.RefreshTimescale();
        }
    }
}
