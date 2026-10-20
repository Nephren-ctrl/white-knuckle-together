using System;
using System.Collections.Generic;
using Steamworks;
using Steamworks.Data;
using WhiteKnuckleTogether.Networking.LiteNetLib;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// The client side of the session. Sends Hello on connect, mirrors the host's player list, and forwards gameplay
    /// messages to the dispatcher once welcomed. All callbacks run on the main thread.
    /// </summary>
    public sealed class WKTClient : IConnectionManager {
        public event Action<WKTPlayer>? PlayerConnectedEvent;
        public event Action<WKTPlayer>? PlayerDisconnectedEvent;
        public event Action<WKTRejectReason>? RejectedEvent;
        public event Action? DisconnectedEvent;
        /// <summary>The host is leaving on purpose and named the player who takes over.</summary>
        public event Action<WKTPlayer>? HostMigrateEvent;
        /// <summary>Another player became the host, and this client is connected to them.</summary>
        public event Action? HostChangedEvent;
        private readonly Dictionary<byte, WKTPlayer> m_playerMap = new();
        private readonly List<WKTPlayerEntry> m_entryBuffer = new(WKTLobbyConsts.MAX_PLAYERS);
        private readonly List<WKTPlayer> m_removedBuffer = new(WKTLobbyConsts.MAX_PLAYERS);
        private readonly List<WKTPlayer> m_addedBuffer = new(WKTLobbyConsts.MAX_PLAYERS);
        private readonly NetDataWriter m_writer = new();
        private readonly WKTMessageDispatcher m_dispatcher;
        private readonly Action<string> m_log;
        private readonly string m_gameVersion;
        private readonly string m_modVersion;
        private readonly WKTProgressionProfile m_localProfile;
        private ConnectionManager? m_connection;

        public WKTPlayer? LocalPlayer { get; private set; }
        public WKTPlayer? HostPlayer { get; private set; }
        public bool IsWelcomed => LocalPlayer != null;
        public int PlayerCount => m_playerMap.Count;
        /// <summary>
        /// Between <see cref="BeginReconnect"/> and the new host's Welcome. The player list is kept meanwhile, so nobody
        /// disappears while the session moves to the new host.
        /// </summary>
        public bool IsReconnecting { get; private set; }
        /// <summary>
        /// Gameplay messages are only sent once the host has welcomed this client. While reconnecting, Steam would queue
        /// them ahead of the Hello, and the new host would take the first one for a malformed Hello.
        /// </summary>
        public bool CanSendGameplay => IsWelcomed && !IsReconnecting;

        public WKTClient(WKTMessageDispatcher dispatcher, string gameVersion, string modVersion,
                         WKTProgressionProfile localProfile, Action<string> log) {
            m_dispatcher = dispatcher;
            m_gameVersion = gameVersion;
            m_modVersion = modVersion;
            m_localProfile = localProfile;
            m_log = log;
        }

        public void Attach(ConnectionManager? connection) => m_connection = connection;

        /// <summary>
        /// Call before attaching the connection to the new host. Its Welcome then updates the player list instead of being
        /// ignored as a duplicate.
        /// </summary>
        public void BeginReconnect() => IsReconnecting = IsWelcomed;

        public void OnConnecting(ConnectionInfo info) { }

        public void OnConnected(ConnectionInfo info) {
            WriteHello(m_writer);
            SendRaw(m_writer, SendType.Reliable);
            m_log("Connected to host. Sent hello.");
        }

        public void OnDisconnected(ConnectionInfo info) {
            m_log($"Disconnected from host ({info.EndReason}).");
            DisconnectedEvent?.Invoke();
        }

        public void OnMessage(IntPtr data, int size, long messageNum, long recvTime, int channel) =>
            HandleMessage(m_dispatcher.Load(data, size));

        public void ClearEvents() {
            PlayerConnectedEvent = default;
            PlayerDisconnectedEvent = default;
            RejectedEvent = default;
            DisconnectedEvent = default;
            HostMigrateEvent = default;
            HostChangedEvent = default;
        }

        public bool TryGetPlayer(byte id, out WKTPlayer player) => m_playerMap.TryGetValue(id, out player);

        public IEnumerable<WKTPlayer> GetPlayersEnumerable() => m_playerMap.Values;

        /// <summary>Who takes over if the current host leaves without saying so. See <see cref="WKTHostSuccession"/>.</summary>
        public bool TryPickSuccessor(out WKTPlayer? successor) {
            successor = null;
            return HostPlayer != null && WKTHostSuccession.TryPickSuccessor(m_playerMap, HostPlayer.Id, out successor);
        }

        /// <summary>Sends a gameplay message to the host. Dropped until <see cref="CanSendGameplay"/>.</summary>
        public void Send(NetDataWriter writer, SendType sendType) {
            if (CanSendGameplay)
                SendRaw(writer, sendType);
        }

        /// <summary>
        /// Hello followed by the local progression profile. A profile too large to send is replaced by an empty one,
        /// which locks more content for everyone instead of failing to join.
        /// </summary>
        internal void WriteHello(NetDataWriter writer) {
            WKTMessageDispatcher.BeginMessage(writer, WKTMessageId.Hello);
            new WKTHelloMessage(WKTNetworkConsts.PROTOCOL_VERSION, m_gameVersion, m_modVersion).Write(writer);
            var profileStart = writer.Length;
            m_localProfile.Write(writer);
            if (writer.Length <= WKTNetworkConsts.MAX_MESSAGE_SIZE) return;
            m_log($"Progression profile is too large to send ({writer.Length} bytes). Sending an empty one.");
            writer.SetPosition(profileStart);
            new WKTProgressionProfile().Write(writer);
        }

        internal void HandleMessage(NetDataReader reader) {
            if (!reader.TryGetByte(out var rawId)) return;
            var id = (WKTMessageId)rawId;
            switch (id) {
                case WKTMessageId.Reject:
                    HandleReject(reader);
                    return;
                case WKTMessageId.Welcome:
                    HandleWelcome(reader);
                    return;
                case WKTMessageId.PlayerJoined:
                    HandlePlayerJoined(reader);
                    return;
                case WKTMessageId.PlayerLeft:
                    HandlePlayerLeft(reader);
                    return;
                case WKTMessageId.HostMigrate:
                    HandleHostMigrate(reader);
                    return;
            }
            if (HostPlayer == null) return;
            m_dispatcher.Dispatch(HostPlayer, id, reader);
        }

        private void HandleReject(NetDataReader reader) {
            var reason = reader.TryGetByte(out var rawReason) ? (WKTRejectReason)rawReason : WKTRejectReason.Malformed;
            m_log($"Rejected by host: {reason}.");
            RejectedEvent?.Invoke(reason);
        }

        private void HandleWelcome(NetDataReader reader) {
            if (IsReconnecting) {
                HandleReconnectWelcome(reader);
                return;
            }
            if (IsWelcomed) {
                m_log("Ignored duplicate welcome.");
                return;
            }
            if (!WKTWelcomeMessage.TryRead(reader, m_entryBuffer, out var localPlayerId)) {
                m_log("Ignored malformed welcome.");
                return;
            }
            foreach (var entry in m_entryBuffer) {
                var player = CreatePlayer(entry);
                m_playerMap.Add(player.Id, player);
                if (player.IsHost)
                    HostPlayer = player;
                if (player.Id == localPlayerId)
                    LocalPlayer = player;
            }
            m_entryBuffer.Clear();
            m_log($"Welcomed by host as player {localPlayerId} with {m_playerMap.Count} players.");
            foreach (var player in m_playerMap.Values)
                PlayerConnectedEvent?.Invoke(player);
        }

        private void HandlePlayerJoined(NetDataReader reader) {
            if (!IsWelcomed) return;
            if (m_playerMap.Count >= WKTLobbyConsts.MAX_PLAYERS || !WKTPlayerEntry.TryRead(reader, out var entry) ||
                entry.IsHost || m_playerMap.ContainsKey(entry.Id)) {
                m_log("Ignored malformed player joined message.");
                return;
            }
            var player = CreatePlayer(entry);
            m_playerMap.Add(player.Id, player);
            PlayerConnectedEvent?.Invoke(player);
        }

        private void HandlePlayerLeft(NetDataReader reader) {
            if (!IsWelcomed) return;
            if (!reader.TryGetByte(out var playerId)) return;
            // The host and the local player never "leave" through this message. Losing the host is a disconnect.
            if (playerId == HostPlayer!.Id || playerId == LocalPlayer!.Id) return;
            if (!m_playerMap.Remove(playerId, out var player)) return;
            PlayerDisconnectedEvent?.Invoke(player);
        }

        /// <summary>
        /// The new host's Welcome after a migration. Players in both lists keep their objects, so nothing about them is
        /// rebuilt. Only players the new host does not know are removed, and only players this client did not know are added.
        /// </summary>
        private void HandleReconnectWelcome(NetDataReader reader) {
            if (!WKTWelcomeMessage.TryRead(reader, m_entryBuffer, out var localPlayerId) || localPlayerId != LocalPlayer!.Id) {
                m_entryBuffer.Clear();
                m_log("Ignored malformed welcome from the new host.");
                return;
            }
            foreach (var player in m_playerMap.Values) {
                if (!TryGetEntry(player.Id, out var entry) || entry.SteamId != player.friend.Id.Value)
                    m_removedBuffer.Add(player);
            }
            foreach (var player in m_removedBuffer)
                m_playerMap.Remove(player.Id);
            foreach (var entry in m_entryBuffer) {
                if (!m_playerMap.TryGetValue(entry.Id, out var player)) {
                    player = CreatePlayer(entry);
                    m_playerMap.Add(player.Id, player);
                    m_addedBuffer.Add(player);
                }
                player.IsHost = entry.IsHost;
                if (player.IsHost)
                    HostPlayer = player;
            }
            m_entryBuffer.Clear();
            IsReconnecting = false;
            m_log($"Reconnected to the new host, player {HostPlayer!.Id}, with {m_playerMap.Count} players.");
            foreach (var player in m_removedBuffer)
                PlayerDisconnectedEvent?.Invoke(player);
            foreach (var player in m_addedBuffer)
                PlayerConnectedEvent?.Invoke(player);
            m_removedBuffer.Clear();
            m_addedBuffer.Clear();
            HostChangedEvent?.Invoke();
        }

        private bool TryGetEntry(byte id, out WKTPlayerEntry entry) {
            foreach (var candidate in m_entryBuffer) {
                if (candidate.Id != id) continue;
                entry = candidate;
                return true;
            }
            entry = default;
            return false;
        }

        private void HandleHostMigrate(NetDataReader reader) {
            if (!IsWelcomed || IsReconnecting) return;
            if (!reader.TryGetByte(out var successorId) || successorId == HostPlayer!.Id ||
                !m_playerMap.TryGetValue(successorId, out var successor)) {
                m_log("Ignored malformed host migrate message.");
                return;
            }
            m_log($"Host is leaving. Player {successorId} takes over.");
            HostMigrateEvent?.Invoke(successor);
        }

        private void SendRaw(NetDataWriter writer, SendType sendType) =>
            m_connection?.Connection.SendMessage(writer.Data, 0, writer.Length, sendType);

        private static WKTPlayer CreatePlayer(in WKTPlayerEntry entry) =>
            new(default, default, new Friend(entry.SteamId), entry.Id, entry.IsHost);
    }
}
