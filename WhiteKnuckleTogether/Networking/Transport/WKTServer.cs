using System;
using System.Collections.Generic;
using Steamworks;
using Steamworks.Data;
using WhiteKnuckleTogether.Networking.LiteNetLib;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// The host side of the session. Connections stay pending until they send a valid Hello, and only then become
    /// players. All callbacks run on the main thread from <see cref="SocketManager.Receive"/> or Steam callbacks.
    /// </summary>
    public sealed class WKTServer : ISocketManager {
        public event Action<WKTPlayer>? PlayerConnectedEvent;
        public event Action<WKTPlayer>? PlayerDisconnectedEvent;
        /// <summary>The shared progression was worked out again, as a player joined or left.</summary>
        public event Action? SharedProgressionChangedEvent;
        private readonly Dictionary<byte, WKTPlayer> m_playerMap = new();
        private readonly Dictionary<uint, byte> m_connectionToPlayerIdMap = new();
        private readonly Dictionary<uint, PendingConnection> m_pendingConnections = new();
        private readonly List<uint> m_expiredConnections = new();
        private readonly List<WKTPlayer> m_expiredPlayers = new();
        private readonly List<WKTProgressionProfile> m_profileBuffer = new(WKTLobbyConsts.MAX_PLAYERS);
        private readonly WKTProgressionProfile m_helloProfile = new();
        private readonly WKTReconnectRoster m_reconnectRoster = new();
        /// <summary>The Steam accounts the host banned this session, turned away if they try to join again.</summary>
        private readonly HashSet<ulong> m_banned = new();
        private readonly ByteIdPool m_playerIdPool = new();
        private readonly NetDataWriter m_writer = new();
        private readonly WKTMessageDispatcher m_dispatcher;
        private readonly Action<string> m_log;
        private readonly string m_gameVersion;
        private readonly string m_modVersion;
        private readonly WKTProgressionProfile m_localProfile;
        private readonly Func<byte, bool> m_isIdInUse;
        private readonly IWKTConnectionSender m_sender;
        private float m_time;

        public WKTPlayer? HostPlayer { get; private set; }
        public int PlayerCount => m_playerMap.Count;
        /// <summary>Players of an adopted session that have not reconnected yet. See <see cref="AdoptSession"/>.</summary>
        public int AwaitingReconnectCount => m_reconnectRoster.Count;
        /// <summary>While true, new clients are rejected: they could not join the run already being played.</summary>
        public bool IsRunInProgress { get; set; }
        /// <summary>
        /// What every admitted player has unlocked, recomputed whenever a player joins or leaves. Content outside it must
        /// not be shown in a multiplayer run, so the least progressed player is never spoiled.
        /// </summary>
        public WKTProgressionProfile SharedProgression { get; } = new();

        public WKTServer(WKTMessageDispatcher dispatcher, string gameVersion, string modVersion,
                         WKTProgressionProfile localProfile, Action<string> log)
            : this(dispatcher, gameVersion, modVersion, localProfile, log, WKTSteamConnectionSender.Instance) { }

        internal WKTServer(WKTMessageDispatcher dispatcher, string gameVersion, string modVersion,
                           WKTProgressionProfile localProfile, Action<string> log, IWKTConnectionSender sender) {
            m_dispatcher = dispatcher;
            m_sender = sender;
            m_gameVersion = gameVersion;
            m_modVersion = modVersion;
            m_localProfile = localProfile;
            m_log = log;
            m_isIdInUse = IsIdInUse;
        }

        public void OnConnecting(Connection connection, ConnectionInfo info) {
            // Capacity and versions are checked on Hello so the client can be told why it was rejected.
            connection.Accept();
        }

        public void OnConnected(Connection connection, ConnectionInfo info) =>
            HandleConnected(connection, info, info.Identity.SteamId);

        public void OnDisconnected(Connection connection, ConnectionInfo info) => HandleDisconnected(connection);

        public void OnMessage(Connection connection, NetIdentity identity, IntPtr data,
                              int size, long messageNum, long recvTime, int channel) =>
            HandleMessage(connection, m_dispatcher.Load(data, size));

        internal void HandleConnected(Connection connection, ConnectionInfo info, SteamId steamId) {
            m_pendingConnections[connection.Id] = new PendingConnection(connection, info, steamId, m_time);
        }

        internal void HandleDisconnected(Connection connection) {
            if (m_pendingConnections.Remove(connection.Id)) return;
            if (!m_connectionToPlayerIdMap.Remove(connection.Id, out var playerId)) return;
            if (!m_playerMap.Remove(playerId, out var player)) return;
            OnPlayerRemoved(player, "left");
        }

        internal void HandleMessage(Connection connection, NetDataReader reader) {
            if (m_connectionToPlayerIdMap.TryGetValue(connection.Id, out var playerId) &&
                m_playerMap.TryGetValue(playerId, out var player)) {
                m_dispatcher.Dispatch(player, reader);
                return;
            }
            // Anything ahead of the Hello, like gameplay a reconnecting client queued, is dropped. The connection stays
            // pending, so it still has to say Hello before the handshake times out.
            if (!IsHello(reader) || !m_pendingConnections.Remove(connection.Id, out var pending)) return;
            HandleHello(pending, reader);
        }

        /// <summary>Expires connections that never completed the handshake, and players that never reconnected.</summary>
        public void Tick(float time) {
            m_time = time;
            m_reconnectRoster.CollectExpired(time, m_expiredPlayers);
            if (m_expiredPlayers.Count > 0) {
                foreach (var player in m_expiredPlayers)
                    OnPlayerRemoved(player, "did not reconnect to the new host");
                m_expiredPlayers.Clear();
            }
            if (m_pendingConnections.Count == 0) return;
            foreach (var pair in m_pendingConnections) {
                if (time - pair.Value.connectTime >= WKTNetworkConsts.HANDSHAKE_TIMEOUT)
                    m_expiredConnections.Add(pair.Key);
            }
            foreach (var connectionId in m_expiredConnections) {
                if (m_pendingConnections.Remove(connectionId, out var pending))
                    Reject(pending.connection, WKTRejectReason.HandshakeTimeout);
            }
            m_expiredConnections.Clear();
        }

        public void ClearEvents() {
            PlayerConnectedEvent = default;
            PlayerDisconnectedEvent = default;
            SharedProgressionChangedEvent = default;
        }

        public void CreateHostPlayer() {
            if (HostPlayer != null) return;
            var player = new WKTPlayer(default, default, new Friend(SteamClient.SteamId), m_playerIdPool.GetNext(), true);
            player.Profile.CopyFrom(m_localProfile);
            HostPlayer = player;
            m_playerMap.Add(player.Id, player);
            RecomputeSharedProgression();
            PlayerConnectedEvent?.Invoke(player);
        }

        /// <summary>
        /// Takes over the session of a host that left, in place of <see cref="CreateHostPlayer"/>: the local player becomes
        /// the host, and everyone else but the old host keeps their id and player object, and has
        /// <see cref="WKTNetworkConsts.RECONNECT_TIMEOUT"/> to reconnect. No events are raised for anyone kept.
        /// </summary>
        public void AdoptSession(WKTPlayer localPlayer, IEnumerable<WKTPlayer> players, byte oldHostId, float time) {
            if (HostPlayer != null) return;
            m_time = time;
            localPlayer.IsHost = true;
            localPlayer.Rebind(default, default);
            localPlayer.Profile.CopyFrom(m_localProfile);
            HostPlayer = localPlayer;
            m_playerMap.Add(localPlayer.Id, localPlayer);
            var maxId = localPlayer.Id;
            foreach (var player in players) {
                if (player.Id == localPlayer.Id || player.Id == oldHostId) continue;
                player.IsHost = false;
                player.Rebind(default, default);
                m_reconnectRoster.Add(player, time + WKTNetworkConsts.RECONNECT_TIMEOUT);
                if (player.Id > maxId)
                    maxId = player.Id;
            }
            m_playerIdPool.SkipInUse(maxId, m_isIdInUse);
            RecomputeSharedProgression();
            m_log($"Took over as host. Waiting for {m_reconnectRoster.Count} players to reconnect.");
        }

        /// <summary>
        /// Called when the local host leaves on purpose. Tells every connected client who takes over, then closes their
        /// connections once the message is delivered. False, doing nothing, when no other player is connected.
        /// </summary>
        public bool TryHandOver(out WKTPlayer? successor) {
            if (HostPlayer == null || !WKTHostSuccession.TryPickSuccessor(m_playerMap, HostPlayer.Id, out successor)) {
                successor = null;
                return false;
            }
            WKTMessageDispatcher.BeginMessage(m_writer, WKTMessageId.HostMigrate);
            m_writer.Put(successor!.Id);
            Broadcast(m_writer, SendType.Reliable);
            foreach (var player in m_playerMap.Values) {
                if (!player.IsHost)
                    m_sender.Close(player.Connection, true, "Host left");
            }
            m_log($"Handed the session over to player {successor.Id} ({successor.friend.Id}).");
            return true;
        }

        /// <summary>
        /// Removes a player right away, without waiting for their connection to time out, such as when they left the
        /// Steam lobby. False if no player has <paramref name="steamId"/>.
        /// </summary>
        public bool Kick(SteamId steamId) {
            if (m_reconnectRoster.TryClaim(steamId.Value, out var awaiting)) {
                OnPlayerRemoved(awaiting, "left before reconnecting");
                return true;
            }
            foreach (var player in m_playerMap.Values) {
                if (player.IsHost || player.friend.Id != steamId) continue;
                var connection = player.Connection;
                m_connectionToPlayerIdMap.Remove(connection.Id);
                m_playerMap.Remove(player.Id);
                m_sender.Close(connection, false, "Left the lobby");
                OnPlayerRemoved(player, "left the lobby");
                return true;
            }
            return false;
        }

        /// <summary>
        /// The host throws <paramref name="player"/> out: they are told so and removed at once. A kicked player may join
        /// again; a banned one is turned away for the rest of the session. False for the host or a player not in the session.
        /// </summary>
        public bool KickPlayer(WKTPlayer player, bool ban) {
            if (player.IsHost || !m_playerMap.TryGetValue(player.Id, out var current) || current != player) return false;
            if (ban)
                m_banned.Add(player.friend.Id.Value);
            m_connectionToPlayerIdMap.Remove(player.Connection.Id);
            m_playerMap.Remove(player.Id);
            Reject(player.Connection, ban ? WKTRejectReason.Banned : WKTRejectReason.Kicked);
            OnPlayerRemoved(player, ban ? "was banned" : "was kicked");
            return true;
        }

        /// <summary>The Steam accounts banned this session.</summary>
        public IReadOnlyCollection<ulong> Banned => m_banned;

        /// <summary>Turns a Steam account away for the rest of the session, such as one the old host banned before this machine took over.</summary>
        public void Ban(ulong steamId) => m_banned.Add(steamId);

        public bool TryGetPlayer(byte id, out WKTPlayer player) => m_playerMap.TryGetValue(id, out player);

        public IEnumerable<WKTPlayer> GetPlayersEnumerable() => m_playerMap.Values;

        /// <summary>Sends to one remote player. Sending to the host player is a no-op since it is local.</summary>
        public void Send(WKTPlayer player, NetDataWriter writer, SendType sendType) {
            if (player.IsHost) return;
            m_sender.Send(player.Connection, writer.Data, writer.Length, sendType);
        }

        /// <summary>Sends to every remote player, optionally skipping one.</summary>
        public void Broadcast(NetDataWriter writer, SendType sendType, WKTPlayer? except = null) {
            foreach (var player in m_playerMap.Values) {
                if (player.IsHost || ReferenceEquals(player, except)) continue;
                m_sender.Send(player.Connection, writer.Data, writer.Length, sendType);
            }
        }

        private void HandleHello(in PendingConnection pending, NetDataReader reader) {
            var steamId = pending.steamId;
            if (!TryReadHello(reader, out var hello)) {
                m_log($"Rejected {steamId}: malformed hello.");
                Reject(pending.connection, WKTRejectReason.Malformed);
                return;
            }
            // A player of an adopted session already has a place: they are let back in even mid-run or when full.
            var isReconnecting = m_reconnectRoster.Contains(steamId.Value);
            var reason = WKTHandshake.Validate(hello, m_gameVersion, m_modVersion,
                isReconnecting ? 0 : m_playerMap.Count + m_reconnectRoster.Count, WKTLobbyConsts.MAX_PLAYERS,
                IsSteamIdConnected(steamId), !isReconnecting && IsRunInProgress, m_banned.Contains(steamId.Value));
            if (reason != WKTRejectReason.None) {
                m_log($"Rejected {steamId}: {reason} (protocol {hello.ProtocolVersion}, game '{hello.GameVersion}', " +
                    $"mod '{hello.ModVersion}').");
                Reject(pending.connection, reason);
                return;
            }
            // Read after validation so a client on another protocol is told so, rather than that its hello is malformed.
            if (!WKTProgressionProfile.TryRead(reader, m_helloProfile)) {
                m_log($"Rejected {steamId}: malformed progression profile.");
                Reject(pending.connection, WKTRejectReason.Malformed);
                return;
            }
            WKTPlayer player;
            if (isReconnecting) {
                m_reconnectRoster.TryClaim(steamId.Value, out player);
                player.Rebind(pending.connection, pending.info);
            } else {
                player = new WKTPlayer(pending.connection, pending.info, new Friend(steamId), m_playerIdPool.GetNext(), false);
                // Announce to existing players before adding, so the new player is not told about itself twice.
                WKTMessageDispatcher.BeginMessage(m_writer, WKTMessageId.PlayerJoined);
                ToEntry(player).Write(m_writer);
                Broadcast(m_writer, SendType.Reliable);
            }
            player.Profile.CopyFrom(m_helloProfile);
            m_helloProfile.Clear();
            m_playerMap.Add(player.Id, player);
            m_connectionToPlayerIdMap.Add(pending.connection.Id, player.Id);
            RecomputeSharedProgression();
            // Players still reconnecting are included, so nobody drops them only to be told about them again.
            WKTMessageDispatcher.BeginMessage(m_writer, WKTMessageId.Welcome);
            WKTWelcomeMessage.WriteHeader(m_writer, player.Id, (byte)(m_playerMap.Count + m_reconnectRoster.Count));
            foreach (var existing in m_playerMap.Values)
                ToEntry(existing).Write(m_writer);
            foreach (var awaiting in m_reconnectRoster.Players)
                ToEntry(awaiting).Write(m_writer);
            Send(player, m_writer, SendType.Reliable);
            if (isReconnecting) {
                m_log($"Player {player.Id} ({steamId}) reconnected.");
                return;
            }
            m_log($"Player {player.Id} ({steamId}) joined.");
            PlayerConnectedEvent?.Invoke(player);
        }

        /// <summary>Whether the message is a Hello, without consuming anything.</summary>
        private static bool IsHello(NetDataReader reader) {
            var position = reader.Position;
            var isHello = reader.TryGetByte(out var id) && id == (byte)WKTMessageId.Hello;
            reader.SetPosition(position);
            return isHello;
        }

        private static bool TryReadHello(NetDataReader reader, out WKTHelloMessage hello) {
            hello = default;
            if (!reader.TryGetByte(out var id) || id != (byte)WKTMessageId.Hello) return false;
            return WKTHelloMessage.TryRead(reader, out hello);
        }

        /// <summary>Everything that follows a player leaving, once they are out of the player map and roster.</summary>
        private void OnPlayerRemoved(WKTPlayer player, string reason) {
            m_playerIdPool.Recycle(player.Id);
            RecomputeSharedProgression();
            WKTMessageDispatcher.BeginMessage(m_writer, WKTMessageId.PlayerLeft);
            m_writer.Put(player.Id);
            Broadcast(m_writer, SendType.Reliable);
            m_log($"Player {player.Id} ({player.friend.Id}) {reason}.");
            PlayerDisconnectedEvent?.Invoke(player);
        }

        private void RecomputeSharedProgression() {
            foreach (var player in m_playerMap.Values)
                m_profileBuffer.Add(player.Profile);
            SharedProgression.SetToIntersection(m_profileBuffer);
            m_profileBuffer.Clear();
            m_log($"Shared progression across {m_playerMap.Count} players: {SharedProgression.UnlockCount} unlocks, " +
                $"{SharedProgression.AchievementCount} achievements, {SharedProgression.FlagCount} flags, " +
                $"{SharedProgression.UpgradeCount} upgrades.");
            SharedProgressionChangedEvent?.Invoke();
        }

        private bool IsSteamIdConnected(SteamId steamId) {
            foreach (var player in m_playerMap.Values) {
                if (player.friend.Id == steamId)
                    return true;
            }
            return false;
        }

        private bool IsIdInUse(byte id) {
            if (m_playerMap.ContainsKey(id)) return true;
            foreach (var player in m_reconnectRoster.Players) {
                if (player.Id == id)
                    return true;
            }
            return false;
        }

        private void Reject(Connection connection, WKTRejectReason reason) {
            WKTMessageDispatcher.BeginMessage(m_writer, WKTMessageId.Reject);
            m_writer.Put((byte)reason);
            m_sender.Send(connection, m_writer.Data, m_writer.Length, SendType.Reliable);
            // Lingering lets the reliable reject message flush before the connection is torn down.
            m_sender.Close(connection, true, $"Rejected: {reason}");
        }

        private static WKTPlayerEntry ToEntry(WKTPlayer player) => new(player.Id, player.friend.Id, player.IsHost);

        private readonly struct PendingConnection {
            public readonly Connection connection;
            public readonly ConnectionInfo info;
            public readonly SteamId steamId;
            public readonly float connectTime;

            public PendingConnection(Connection connection, ConnectionInfo info, SteamId steamId, float connectTime) {
                this.connection = connection;
                this.info = info;
                this.steamId = steamId;
                this.connectTime = connectTime;
            }
        }
    }
}
