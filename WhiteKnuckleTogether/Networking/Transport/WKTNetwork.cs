using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Steamworks;
using Steamworks.Data;
using UnityEngine;
using UnityEngine.SceneManagement;
using WhiteKnuckleTogether.Networking.LiteNetLib;

namespace WhiteKnuckleTogether {
    public sealed class WKTNetwork : IDisposable {
        public event Action? OnHostStarted;
        public event Action? OnClientStarted;
        public event Action? OnNetworkStopping;
        public event Action<WKTPlayer>? OnPlayerConnected;
        public event Action<WKTPlayer>? OnPlayerDisconnected;
        public event Action<WKTRejectReason>? OnRejected;
        /// <summary>The lobby modifiers changed: the local host changed them, or a client saw the host do so. See <see cref="HasLobbyModifier"/>.</summary>
        public event Action? OnLobbyModifiersChanged;
        /// <summary>The lobby play mode changed: the local host changed it, or a client saw the host do so. See <see cref="LobbyPlayMode"/>.</summary>
        public event Action? OnLobbyPlayModeChanged;
        /// <summary>What the lobby's goo follows changed: the local host changed it, or a client saw the host do so. See <see cref="LobbyGooFollow"/>.</summary>
        public event Action? OnLobbyGooFollowChanged;
        /// <summary>The race orb's size changed: the local host changed it, or a client saw the host do so. See <see cref="LobbyOrbRadius"/>.</summary>
        public event Action? OnLobbyOrbRadiusChanged;
        /// <summary>How players collide changed: the local host changed it, or a client saw the host do so. See <see cref="LobbyPlayerCollision"/>.</summary>
        public event Action? OnLobbyPlayerCollisionChanged;
        /// <summary>
        /// Which shortcuts every player has unlocked, or which the host ticked, changed: the shared progression changed or the
        /// local host ticked one, or a client saw the host's lobby data change. See <see cref="LobbyShortcutsTicked"/>.
        /// </summary>
        public event Action? OnLobbyShortcutsChanged;
        /// <summary>Which trinkets everyone has unlocked changed: the host's shared progression changed, or a client saw it.</summary>
        public event Action? OnLobbyTrinketsChanged;
        /// <summary>A player in the session changed their own lobby member data, such as <see cref="WKTLobbyConsts.KEY_VOICES"/>.</summary>
        public event Action<WKTPlayer>? OnPlayerMemberDataChanged;
        /// <summary>Another player took over as host after the old one left, or the local player did. See <see cref="HostPlayer"/>.</summary>
        public event Action? OnHostChanged;
        /// <summary>Someone readied or unreadied, or the ready countdown started or stopped. See <see cref="CountReady"/>.</summary>
        public event Action? OnReadyChanged;
        /// <summary>The host took the lobby to the trinket screen or back. See <see cref="LobbyPicking"/>.</summary>
        public event Action? OnLobbyPickingChanged;
        /// <summary>The lobby got its join code, or a client saw it. See <see cref="LobbyJoinCode"/>.</summary>
        public event Action? OnLobbyJoinCodeChanged;
        private const string host_lobby_data = "host";
        private const string connect_rich_presence = "connect";
        /// <summary>Real seconds a restart waits on the main menu before starting the next run.</summary>
        private const float c_restart_menu_delay = 0.5f;
        /// <summary>Real seconds between tries to publish the host's ping location, which Steam measures after startup.</summary>
        private const float c_ping_location_retry_delay = 2f;
        /// <summary>Real seconds a lobby join counts as under way, in case Steam never answers.</summary>
        private const float c_join_timeout = 10f;
        /// <summary>Codes tried for a join code lobby before one another lobby already has is used anyway.</summary>
        private const int c_join_code_attempts = 5;
        /// <summary>Its own generator, so picking a code never moves the game's own random state.</summary>
        private static readonly System.Random s_joinCodeRandom = new();
        private static readonly Action<string> s_logInfo = message => WKT.Instance.Logger.LogInfo(message);
        private static readonly Action<string> s_logError = message => WKT.Instance.Logger.LogError(message);
        private readonly HashSet<ModifierId> m_modifiers = new();
        private readonly StringBuilder m_sb = new();
        private readonly NetDataWriter m_writer = new();
        private readonly WKTProgressionProfile m_localProfile = new();
        private readonly WKTRunStart m_runBuffer = new();
        /// <summary>Client only. Taking over from a host that left.</summary>
        private readonly WKTMigration m_migration = new();
        private SocketManager? m_socket;
        private WKTServer? m_server;
        private ConnectionManager? m_connection;
        private WKTClient? m_client;
        private bool m_lobbyExists;
        private Lobby m_lobby;
        private NetworkMode m_nextMode;
        private string m_gameVersion = string.Empty;
        private bool m_stopRequested;
        /// <summary>The host's modifiers as a client sees them in the lobby data.</summary>
        private ushort m_lobbyModifierMask;
        /// <summary>Host only. The play mode written to the lobby data.</summary>
        private PlayModeId m_playMode;
        /// <summary>The host's play mode as a client sees it in the lobby data.</summary>
        private PlayModeId m_lobbyPlayMode;
        /// <summary>Host only. What the goo follows, as written to the lobby data.</summary>
        private GooFollowId m_gooFollow;
        /// <summary>What the host's goo follows as a client sees it in the lobby data.</summary>
        private GooFollowId m_lobbyGooFollow;
        /// <summary>Host only. The race orb's size, as written to the lobby data.</summary>
        private OrbRadiusId m_orbRadius = OrbRadiusId.Medium;
        /// <summary>The host's race orb size as a client sees it in the lobby data.</summary>
        private OrbRadiusId m_lobbyOrbRadius = OrbRadiusId.Medium;
        /// <summary>Host only. How players collide, as written to the lobby data.</summary>
        private PlayerCollisionId m_playerCollision = PlayerCollisionId.Soft;
        /// <summary>How the host's players collide as a client sees it in the lobby data.</summary>
        private PlayerCollisionId m_lobbyPlayerCollision = PlayerCollisionId.Soft;
        /// <summary>The shortcuts every player has unlocked, as a client sees them in the lobby data.</summary>
        private byte m_lobbyShortcutsAvailable;
        /// <summary>Client only: the trinkets the host says everyone has unlocked.</summary>
        private readonly HashSet<string> m_lobbyTrinkets = new();
        private readonly HashSet<string> m_trinketScratch = new();
        /// <summary>The shortcuts the host ticked, as a client sees them in the lobby data.</summary>
        private byte m_lobbyShortcutsTicked;
        /// <summary>A lobby to join once the main menu loads. Zero when there is none.</summary>
        private ulong m_pendingLobbyId;
        private float m_time;
        /// <summary>Whether this host has written itself into the lobby data, which only the lobby owner can do.</summary>
        private bool m_isLobbyClaimed = true;
        private float m_nextLobbyOwnerCheckTime;
        /// <summary>Whether to load the main menu once the session has stopped, because it ended mid-run.</summary>
        private bool m_returnToMenuRequested;
        /// <summary>Why the last session ended on its own. Read and reset by <see cref="ConsumeEndReason"/>.</summary>
        private WKTSessionEndReason m_pendingEndReason;
        /// <summary>
        /// The host restarts the run for everyone through the main menu. On the host, a new run of
        /// <see cref="m_restartGamemodeId"/> starts once the menu is ready. On a client, the host's StartRun may arrive
        /// before its menu has loaded, and waits for it.
        /// </summary>
        private bool m_restartPending;
        private GamemodeId m_restartGamemodeId;
        /// <summary>When the pending restart is handled. The menu needs a moment to set itself up after it loads.</summary>
        private float m_restartTime = float.MaxValue;
        /// <summary>Client only. The host's StartRun arrived while a restart was still on its way to the menu.</summary>
        private M_Gamemode? m_bufferedRunGamemode;
        private bool m_localReady;
        /// <summary>When the ready countdown runs out, or <see cref="float.MaxValue"/> while it is not counting.</summary>
        private float m_countdownEnd = float.MaxValue;
        /// <summary>Host: the last countdown number written to the lobby data. Client: the last one seen there.</summary>
        private string m_countdownToken = string.Empty;
        private int m_countdownCount;
        /// <summary>Host only: whether the lobby is on the trinket screen, as written to the lobby data.</summary>
        private bool m_picking;
        /// <summary>Whether the host's lobby is on the trinket screen, as a client sees it in the lobby data.</summary>
        private bool m_lobbyPicking;
        /// <summary>Host only: the names of the players in the session, the host first, for <see cref="WKTLobbyConsts.KEY_MEMBERS"/>.</summary>
        private readonly List<string> m_memberNames = new(WKTLobbyConsts.MAX_PLAYERS);
        /// <summary>Host only: whether this host's ping location is in the lobby data.</summary>
        private bool m_isPingLocationPublished;
        private float m_nextPingLocationTime;
        /// <summary>When the last lobby join started, or <see cref="float.NegativeInfinity"/> once Steam answered.</summary>
        private float m_joinStartTime = float.NegativeInfinity;
        /// <summary>Host only: this join code lobby's code, written to the lobby data. Empty for other lobbies, or until it is picked.</summary>
        private string m_joinCode = string.Empty;
        /// <summary>The host's join code as a client sees it in the lobby data.</summary>
        private string m_lobbyJoinCode = string.Empty;

        public NetworkMode Mode { get; private set; }
        /// <summary>The player hosting the session. Null while connecting or offline.</summary>
        public WKTPlayer? HostPlayer => Mode switch {
            NetworkMode.Host => m_server?.HostPlayer,
            NetworkMode.Client => m_client?.HostPlayer,
            _ => null
        };
        /// <summary>Register gameplay message handlers here. Handlers persist across sessions.</summary>
        public WKTMessageDispatcher Messages { get; } = new(s_logError);
        /// <summary>The local player once the session is established. Null while connecting or offline.</summary>
        public WKTPlayer? LocalPlayer => Mode switch {
            NetworkMode.Host => m_server?.HostPlayer,
            NetworkMode.Client => m_client?.LocalPlayer,
            _ => null
        };
        /// <summary>
        /// Host only: what every player in the session has unlocked. Null on clients, which receive the host's computed
        /// set when a run starts.
        /// </summary>
        public WKTProgressionProfile? SharedProgression => Mode == NetworkMode.Host ? m_server?.SharedProgression : null;
        /// <summary>The multiplayer run in progress. See <see cref="IsMultiplayerRun"/>.</summary>
        public WKTSession Session { get; } = new();
        /// <summary>Each player's round trip to the host, for the lobby.</summary>
        internal WKTPings Pings { get; private set; } = null!;
        /// <summary>The gate every run feature checks first. Never true in singleplayer.</summary>
        public bool IsMultiplayerRun => Session.IsMultiplayerRun(Mode);
        /// <summary>Host only: players of the old session still reconnecting after a migration. Zero on clients.</summary>
        internal int AwaitingReconnectCount => Mode == NetworkMode.Host && m_server != null ? m_server.AwaitingReconnectCount : 0;
        /// <summary>Whether the local player is ready to start the run.</summary>
        public bool IsLocalReady => m_localReady;
        /// <summary>Seconds until the run starts once everyone is ready, or -1 while the countdown is not running.</summary>
        public float CountdownLeft => m_countdownEnd == float.MaxValue ? -1f : Mathf.Max(0f, m_countdownEnd - m_time);
        /// <summary>Whether the host took the lobby to the trinket screen, where everyone picks and readies.</summary>
        public bool LobbyPicking => Mode switch {
            NetworkMode.Host => m_picking,
            NetworkMode.Client => m_lobbyPicking,
            _ => false
        };
        /// <summary>The code that finds this lobby, if it is a join code lobby. Empty otherwise, and until the host has picked one.</summary>
        public string LobbyJoinCode => Mode switch {
            NetworkMode.Host => m_joinCode,
            NetworkMode.Client => m_lobbyJoinCode,
            _ => string.Empty
        };
        /// <summary>Whether a lobby join is waiting for Steam to answer.</summary>
        public bool IsJoiningLobby => m_time - m_joinStartTime < c_join_timeout;
        public string GameVersion => m_gameVersion;

        public void Initialize() {
            m_gameVersion = Application.version;
            SteamNetworkingUtils.InitRelayNetworkAccess();
            WKT.Instance.Logger.LogInfo("Initialized Steam relay network access.");
            SteamFriends.OnGameLobbyJoinRequested += OnGameLobbyJoinRequested;
            SteamMatchmaking.OnLobbyCreated += OnLobbyCreated;
            SteamMatchmaking.OnLobbyEntered += OnLobbyEntered;
            SteamMatchmaking.OnLobbyDataChanged += OnLobbyDataChanged;
            SteamMatchmaking.OnLobbyMemberDataChanged += OnLobbyMemberDataChanged;
            SteamMatchmaking.OnLobbyMemberLeave += OnLobbyMemberLeft;
            SteamMatchmaking.OnLobbyMemberDisconnected += OnLobbyMemberLeft;
            Pings = new WKTPings(this);
            Messages.Register(WKTMessageId.PlayerPings, Pings.OnPings);
            Messages.Register(WKTMessageId.StartRun, OnStartRunMessage);
            Messages.Register(WKTMessageId.ReturnToLobby, OnReturnToLobbyMessage);
            Messages.Register(WKTMessageId.RestartRun, OnRestartRunMessage);
            // An invite accepted while the game was closed launches it with the lobby on the command line.
            if (WKTLobbyData.TryGetConnectLobbyId(Environment.GetCommandLineArgs(), out m_pendingLobbyId))
                WKT.Instance.Logger.LogInfo($"Launched from a Steam invite. Joining lobby with id {m_pendingLobbyId} once the main menu loads.");
        }

        public void Dispose() {
            SteamMatchmaking.OnLobbyCreated -= OnLobbyCreated;
            SteamFriends.OnGameLobbyJoinRequested -= OnGameLobbyJoinRequested;
            SteamMatchmaking.OnLobbyEntered -= OnLobbyEntered;
            SteamMatchmaking.OnLobbyDataChanged -= OnLobbyDataChanged;
            SteamMatchmaking.OnLobbyMemberDataChanged -= OnLobbyMemberDataChanged;
            SteamMatchmaking.OnLobbyMemberLeave -= OnLobbyMemberLeft;
            SteamMatchmaking.OnLobbyMemberDisconnected -= OnLobbyMemberLeft;
            Stop();
            OnPlayerConnected = default;
            OnPlayerDisconnected = default;
            OnRejected = default;
            OnPlayerMemberDataChanged = default;
            OnHostChanged = default;
            Messages.Clear();
        }

        /// <summary>Pumps incoming messages. Must be called once per frame on the main thread.</summary>
        public void Update(float realtime) {
            m_time = realtime;
            if (m_server != null) {
                m_server.Tick(realtime);
                m_socket?.Receive();
            }
            if (m_client != null)
                m_connection?.Receive();
            // Deferred so connection objects are never torn down from inside their own callbacks.
            if (m_stopRequested) {
                Stop();
                if (m_returnToMenuRequested)
                    ReturnToMainMenu();
                return;
            }
            if (m_migration.IsActive)
                UpdateMigration();
            UpdateLobbyOwnership();
            UpdatePingLocation();
            UpdateReadyCountdown();
            Pings.Update(realtime);
            if (m_restartPending && m_time >= m_restartTime)
                ContinueRestart();
        }

        /// <summary>The reason the last session ended on its own, once. None if it was already read or the player left.</summary>
        public WKTSessionEndReason ConsumeEndReason() {
            var reason = m_pendingEndReason;
            m_pendingEndReason = WKTSessionEndReason.None;
            return reason;
        }

        public void StartHost() {
            if (Mode != NetworkMode.None) {
                WKT.Instance.Logger.LogError("Cannot start host. The network is already active.");
                return;
            }
            if (!WKTPlugin.IsMultiplayerAvailable) {
                WKT.Instance.Logger.LogError("Cannot start host. Multiplayer is disabled because a critical patch failed.");
                return;
            }
            WKT.Instance.Logger.LogInfo("Starting host.");
            m_nextMode = NetworkMode.Host;
            m_socket = SteamNetworkingSockets.CreateRelaySocket<SocketManager>();
            // Snapshotted before any session exists, so it is always the player's real progression.
            WKTProgressionProfileBuilder.BuildLocal(m_localProfile, WKT.Instance.Logger);
            CreateServer();
            WKT.Instance.Logger.LogInfo("Started host via Steam relay.");
            SteamMatchmaking.CreateLobbyAsync(WKTLobbyConsts.MAX_PLAYERS);
            Mode = NetworkMode.Host;
        }

        public void StartClient(string ip) {
            if (Mode != NetworkMode.None) {
                WKT.Instance.Logger.LogError("Cannot start client. The network is already active.");
                return;
            }
            if (!WKTPlugin.IsMultiplayerAvailable) {
                WKT.Instance.Logger.LogError("Cannot start client. Multiplayer is disabled because a critical patch failed.");
                return;
            }
            WKT.Instance.Logger.LogInfo("Starting client.");
            m_nextMode = NetworkMode.Client;
            m_connection = SteamNetworkingSockets.ConnectRelay<ConnectionManager>(ulong.Parse(ip));
            WKTProgressionProfileBuilder.BuildLocal(m_localProfile, WKT.Instance.Logger);
            m_client = new WKTClient(Messages, m_gameVersion, WKTPlugin.VERSION, m_localProfile, s_logInfo);
            m_client.PlayerConnectedEvent += p => OnPlayerConnected?.Invoke(p);
            m_client.PlayerDisconnectedEvent += p => OnPlayerDisconnected?.Invoke(p);
            m_client.RejectedEvent += OnClientRejected;
            m_client.DisconnectedEvent += OnClientDisconnected;
            m_client.HostMigrateEvent += BeginMigration;
            m_client.HostChangedEvent += OnClientHostChanged;
            m_client.Attach(m_connection);
            m_connection.Interface = m_client;
            Mode = NetworkMode.Client;
            OnClientStarted?.Invoke();
        }

        public void Stop() {
            m_stopRequested = false;
            if (Mode == NetworkMode.None) return;
            OnNetworkStopping?.Invoke();
            TryHandOverHost();
            m_migration.Clear();
            ClearRestart();
            m_isLobbyClaimed = true;
            // Only the run state ends here. Progression stays locked until the main menu rolls the run back, because
            // the game still saves on the way out of the run scene.
            Session.End();
            m_modifiers.Clear();
            m_lobbyModifierMask = 0;
            m_playMode = default;
            m_lobbyPlayMode = default;
            m_gooFollow = default;
            m_lobbyGooFollow = default;
            m_orbRadius = OrbRadiusId.Medium;
            m_lobbyOrbRadius = OrbRadiusId.Medium;
            m_playerCollision = PlayerCollisionId.Soft;
            m_lobbyPlayerCollision = PlayerCollisionId.Soft;
            m_lobbyShortcutsAvailable = 0;
            m_lobbyShortcutsTicked = 0;
            m_lobbyTrinkets.Clear();
            m_localReady = false;
            m_countdownEnd = float.MaxValue;
            m_countdownToken = string.Empty;
            m_picking = false;
            m_lobbyPicking = false;
            m_isPingLocationPublished = false;
            m_nextPingLocationTime = 0f;
            m_joinCode = string.Empty;
            m_lobbyJoinCode = string.Empty;
            Pings.Clear();
            m_nextMode = NetworkMode.None;
            if (m_lobbyExists) {
                m_lobby.Leave();
                m_lobbyExists = false;
                WKT.Instance.Logger.LogInfo($"Left Steam lobby with id {m_lobby.Id}.");
                m_lobby = default;
            }
            if (m_socket != null) {
                m_socket.Close();
                m_socket = null;
                m_server?.ClearEvents();
                m_server = null;
                WKT.Instance.Logger.LogInfo("Stopped host.");
            }
            if (m_connection != null || m_client != null) {
                CloseConnection();
                m_client?.ClearEvents();
                m_client = null;
                WKT.Instance.Logger.LogInfo("Stopped client.");
            }
            SteamFriends.SetRichPresence(connect_rich_presence, null);
            Mode = NetworkMode.None;
        }

        /// <summary>
        /// Called when Play is pressed on a gamemode. Returns whether the game's own LoadGamemode should run. Offline it
        /// always does, untouched.
        /// </summary>
        internal bool HandlePlayRequest(M_Gamemode? gamemode) {
            var isMultiplayerGamemode = WKTMultiplayerGamemodes.TryGetId(gamemode, out var gamemodeId);
            switch (WKTPlayRequest.Decide(Mode, isMultiplayerGamemode)) {
                case WKTPlayAction.RunOriginal:
                    return true;
                case WKTPlayAction.LeaveLobbyThenRunOriginal:
                    WKT.Instance.Logger.LogInfo("Playing a singleplayer gamemode. Leaving the multiplayer session first.");
                    Stop();
                    return true;
                case WKTPlayAction.Block:
                    WKT.Instance.Logger.LogInfo("Only the host can start a multiplayer run.");
                    return false;
                case WKTPlayAction.StartMultiplayerRun:
                    TryStartHostRun(gamemode!, gamemodeId);
                    return false;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Call when the main menu loads, after its UI exists. Joins the lobby of an invite that could not be joined
        /// when it was accepted, or that launched the game.
        /// </summary>
        internal void JoinPendingLobby() {
            if (m_pendingLobbyId == 0) return;
            var lobbyId = m_pendingLobbyId;
            m_pendingLobbyId = 0;
            HandleJoinRequest(lobbyId);
        }

        /// <summary>Call when the main menu loads: any run is over, and the lobby accepts new players again.</summary>
        internal void EndRun() {
            Session.End();
            if (m_server != null) {
                m_server.IsRunInProgress = false;
                PublishLobbyInRun();
            }
            // Back in the lobby, everyone readies again for the next run, from the lobby's first screen.
            SetLocalReady(false);
            StopCountdown();
            if (Mode == NetworkMode.Host && m_lobbyExists)
                SetLobbyPicking(false);
            if (m_restartPending)
                m_restartTime = m_time + c_restart_menu_delay;
        }

        /// <summary>Host only, in a run. Takes every player back to the lobby, still connected.</summary>
        internal void ReturnAllToLobby() {
            if (Mode != NetworkMode.Host || !IsMultiplayerRun) return;
            WKT.Instance.Logger.LogInfo("Taking every player back to the lobby.");
            Broadcast(BeginMessage(WKTMessageId.ReturnToLobby), SendType.Reliable);
            ReturnToMainMenu();
        }

        /// <summary>
        /// Host only, in a run. Starts the same gamemode again for every player, with a new seed. Everyone goes back to the
        /// main menu first, so the new run starts exactly like any other.
        /// </summary>
        internal void RestartRunForAll() {
            if (Mode != NetworkMode.Host || !IsMultiplayerRun || m_restartPending) return;
            WKT.Instance.Logger.LogInfo("Restarting the run for every player.");
            m_restartPending = true;
            m_restartGamemodeId = Session.Run.Gamemode;
            Broadcast(BeginMessage(WKTMessageId.RestartRun), SendType.Reliable);
            ReturnToMainMenu();
        }

        /// <summary>Leaves the session, and the run if one is being played.</summary>
        internal void DisconnectAndReturnToMenu() {
            Stop();
            ReturnToMainMenu();
        }

        public void SetLobbyVisibility(LobbyVisibilityId visibilityId) {
            WKT.Instance.Logger.LogInfo("Attempting to set Steam lobby visibility. " +
                $"Mode is {m_nextMode}. Lobby is {(m_lobbyExists ? "valid" : "invalid")}.");
            if (m_nextMode != NetworkMode.Host) return;
            if (!m_lobbyExists) return;
            switch (visibilityId) {
                case LobbyVisibilityId.InviteOnly:
                    m_lobby.SetPrivate();
                    break;
                case LobbyVisibilityId.FriendsOnly:
                    m_lobby.SetFriendsOnly();
                    break;
                case LobbyVisibilityId.Public:
                case LobbyVisibilityId.JoinCode:
                    // Steam only searches public lobbies, so a join code lobby is one too, kept out of the browser below.
                    m_lobby.SetPublic();
                    break;
            }
            m_lobby.SetData(WKTLobbyConsts.KEY_LISTED, visibilityId == LobbyVisibilityId.Public ? "1" : string.Empty);
            WKT.Instance.Logger.LogInfo($"Set Steam lobby visibility to {visibilityId}.");
            if (visibilityId == LobbyVisibilityId.JoinCode && m_joinCode.Length == 0)
                AssignJoinCode();
        }

        /// <summary>
        /// Host only: gives a join code lobby a code no other lobby of this mod has, checked against Steam's lobby list. After
        /// a few taken codes, or when Steam cannot be asked, the last one is used anyway.
        /// </summary>
        private async void AssignJoinCode() {
            var lobbyId = m_lobby.Id;
            var code = string.Empty;
            for (var attempt = 0; attempt < c_join_code_attempts; attempt++) {
                code = WKTLobbyData.FormatJoinCode(s_joinCodeRandom.Next(WKTLobbyConsts.JOIN_CODE_COUNT));
                try {
                    if (await FindJoinCodeLobbyAsync(code) == null) break;
                }
                catch (Exception e) {
                    WKT.Instance.Logger.LogWarning($"Could not check whether join code {code} is taken: {e.Message}");
                    break;
                }
            }
            // The session may have ended, or moved to another lobby, while Steam answered.
            if (!m_lobbyExists || m_lobby.Id != lobbyId || m_nextMode != NetworkMode.Host) return;
            m_lobby.SetData(WKTLobbyConsts.KEY_JOIN_CODE, code);
            m_joinCode = code;
            WKT.Instance.Logger.LogInfo($"Set the lobby's join code to {code}.");
            OnLobbyJoinCodeChanged?.Invoke();
        }

        /// <summary>Looks for the lobby with join code <paramref name="code"/>. Null if there is none; throws if Steam cannot be asked.</summary>
        public static async System.Threading.Tasks.Task<Lobby?> FindJoinCodeLobbyAsync(string code) {
            var lobbies = await SteamMatchmaking.LobbyList
                .FilterDistanceWorldwide()
                .WithKeyValue(WKTLobbyConsts.KEY_MOD, WKTLobbyConsts.MOD_ID)
                .WithKeyValue(WKTLobbyConsts.KEY_JOIN_CODE, code)
                .WithMaxResults(1)
                .RequestAsync();
            return lobbies != null && lobbies.Length > 0 ? lobbies[0] : null;
        }

        /// <summary>
        /// Publishes the local player's active voice cosmetics as member data, so everyone else hears their voice. Call
        /// whenever the cosmetics may have changed.
        /// </summary>
        public void PublishLocalVoices() {
            if (m_lobbyExists)
                PublishLocalVoices(m_lobby);
        }

        /// <summary>Whether <paramref name="player"/> turned voice chat off, as their lobby member data says.</summary>
        public bool HasVoiceChatOff(WKTPlayer player) => GetMemberData(player, WKTLobbyConsts.KEY_VOICE_CHAT_OFF) == "1";

        /// <summary>Null outside a lobby, and empty for a key the player never set.</summary>
        public string? GetMemberData(WKTPlayer player, string key) => m_lobbyExists ? m_lobby.GetMemberData(player.friend, key) : null;

        /// <summary>Opens the Steam overlay's invite dialog for the current lobby. Invites work for every lobby visibility.</summary>
        public void OpenInviteOverlay() {
            if (!m_lobbyExists) {
                WKT.Instance.Logger.LogError("Cannot invite friends. Lobby does not exist.");
                return;
            }
            SteamFriends.OpenGameInviteOverlay(m_lobby.Id);
        }

        public bool HasLobbyModifier(ModifierId modifierId) => Mode switch {
            NetworkMode.Host => m_modifiers.Contains(modifierId),
            NetworkMode.Client => (m_lobbyModifierMask & WKTRunStart.GetModifierBit(modifierId)) != 0,
            _ => false
        };

        public PlayModeId LobbyPlayMode => Mode switch {
            NetworkMode.Host => m_playMode,
            NetworkMode.Client => m_lobbyPlayMode,
            _ => WKT.Instance.Settings.LocalPlayModeId
        };

        public GooFollowId LobbyGooFollow => Mode switch {
            NetworkMode.Host => m_gooFollow,
            NetworkMode.Client => m_lobbyGooFollow,
            _ => WKT.Instance.Settings.LocalGooFollowId
        };

        public OrbRadiusId LobbyOrbRadius => Mode switch {
            NetworkMode.Host => m_orbRadius,
            NetworkMode.Client => m_lobbyOrbRadius,
            _ => WKT.Instance.Settings.LocalOrbRadiusId
        };

        public PlayerCollisionId LobbyPlayerCollision => Mode switch {
            NetworkMode.Host => m_playerCollision,
            NetworkMode.Client => m_lobbyPlayerCollision,
            _ => WKT.Instance.Settings.LocalPlayerCollisionId
        };

        /// <summary>
        /// The shortcuts every player in the lobby has unlocked, one bit per <see cref="ShortcutId"/>. The rest show redacted.
        /// A client also checks its own unlocks: the host's lobby data may still be from before the client joined, and must
        /// not show the client a shortcut it has not found, even for a moment.
        /// </summary>
        public byte LobbyShortcutsAvailable => Mode switch {
            NetworkMode.Host => m_server != null ? WKTShortcuts.GetAvailableMask(m_server.SharedProgression) : (byte)0,
            NetworkMode.Client => (byte)(m_lobbyShortcutsAvailable & WKTShortcuts.GetAvailableMask(m_localProfile)),
            _ => 0
        };

        /// <summary>The available shortcuts the host ticked, one bit per <see cref="ShortcutId"/>. These are open in the run.</summary>
        public byte LobbyShortcutsTicked => Mode switch {
            NetworkMode.Host => WKTShortcuts.GetTicked(LobbyShortcutsAvailable, WKT.Instance.Settings.LocalUntickedShortcuts),
            NetworkMode.Client => (byte)(m_lobbyShortcutsTicked & LobbyShortcutsAvailable),
            _ => 0
        };

        public IEnumerable<WKTPlayer> GetPlayersEnumerable() => Mode switch {
            NetworkMode.Host => m_server?.GetPlayersEnumerable() ?? Enumerable.Empty<WKTPlayer>(),
            NetworkMode.Client => m_client?.GetPlayersEnumerable() ?? Enumerable.Empty<WKTPlayer>(),
            _ => Enumerable.Empty<WKTPlayer>()
        };

        public bool TryGetPlayer(byte id, out WKTPlayer? player) {
            player = null;
            return Mode switch {
                NetworkMode.Host => m_server != null && m_server.TryGetPlayer(id, out player!),
                NetworkMode.Client => m_client != null && m_client.TryGetPlayer(id, out player!),
                _ => false
            };
        }

        /// <summary>Resets the shared writer and writes the message id. Write the payload, then pass it to a send method.</summary>
        public NetDataWriter BeginMessage(WKTMessageId id) {
            WKTMessageDispatcher.BeginMessage(m_writer, id);
            return m_writer;
        }

        /// <summary>Client only. Sends to the host.</summary>
        public void SendToHost(NetDataWriter writer, SendType sendType) => m_client?.Send(writer, sendType);

        /// <summary>Host only. Sends to one remote player.</summary>
        public void SendToPlayer(WKTPlayer player, NetDataWriter writer, SendType sendType) => m_server?.Send(player, writer, sendType);

        /// <summary>Host only. Sends to every remote player, optionally skipping one.</summary>
        public void Broadcast(NetDataWriter writer, SendType sendType, WKTPlayer? except = null) =>
            m_server?.Broadcast(writer, sendType, except);

        public void SetLobbyGamemode(GamemodeId gamemodeId) {
            if (!CanSetLobbyData()) return;
            m_lobby.SetData(WKTLobbyConsts.KEY_GAMEMODE, gamemodeId.ToString());
            WKT.Instance.Logger.LogInfo($"Set gamemode to {gamemodeId} in lobby data.");
        }

        public void SetLobbyPlayMode(PlayModeId playModeId) {
            if (!CanSetLobbyData()) return;
            m_lobby.SetData(WKTLobbyConsts.KEY_PLAY_MODE, playModeId.ToString());
            WKT.Instance.Logger.LogInfo($"Set play mode to {playModeId} in lobby data.");
            if (m_playMode == playModeId) return;
            m_playMode = playModeId;
            OnLobbyPlayModeChanged?.Invoke();
        }

        public void SetLobbyGooFollow(GooFollowId gooFollowId) {
            if (!CanSetLobbyData()) return;
            m_lobby.SetData(WKTLobbyConsts.KEY_GOO_FOLLOW, gooFollowId.ToString());
            WKT.Instance.Logger.LogInfo($"Set goo follow to {gooFollowId} in lobby data.");
            if (m_gooFollow == gooFollowId) return;
            m_gooFollow = gooFollowId;
            OnLobbyGooFollowChanged?.Invoke();
        }

        public void SetLobbyOrbRadius(OrbRadiusId orbRadiusId) {
            if (!CanSetLobbyData()) return;
            m_lobby.SetData(WKTLobbyConsts.KEY_ORB_RADIUS, orbRadiusId.ToString());
            WKT.Instance.Logger.LogInfo($"Set orb radius to {orbRadiusId} in lobby data.");
            if (m_orbRadius == orbRadiusId) return;
            m_orbRadius = orbRadiusId;
            OnLobbyOrbRadiusChanged?.Invoke();
        }

        public void SetLobbyPlayerCollision(PlayerCollisionId playerCollisionId) {
            if (!CanSetLobbyData()) return;
            m_lobby.SetData(WKTLobbyConsts.KEY_PLAYER_COLLISION, playerCollisionId.ToString());
            WKT.Instance.Logger.LogInfo($"Set player collision to {playerCollisionId} in lobby data.");
            if (m_playerCollision == playerCollisionId) return;
            m_playerCollision = playerCollisionId;
            OnLobbyPlayerCollisionChanged?.Invoke();
        }

        /// <summary>Host only, in the lobby or a run. Kicks <paramref name="player"/> out of the session. They may join again.</summary>
        public void KickPlayer(WKTPlayer player) => RemovePlayer(player, false);

        /// <summary>Host only, in the lobby or a run. Kicks <paramref name="player"/> out, and turns them away until the session ends.</summary>
        public void BanPlayer(WKTPlayer player) => RemovePlayer(player, true);

        private void RemovePlayer(WKTPlayer player, bool ban) {
            if (Mode != NetworkMode.Host || m_server == null || !m_server.KickPlayer(player, ban)) return;
            WKT.Instance.Logger.LogInfo($"{(ban ? "Banned" : "Kicked")} player {player.Id} ({player.friend.Id}).");
            WKTChat.PostEvent($"{WKTChatText.PlayerName(player.Id, player.friend.Name)} was {(ban ? "banned" : "kicked")}.");
        }

        /// <summary>Host only. Takes every player in the lobby to the trinket screen, or back to the lobby's first screen.</summary>
        public void SetLobbyPicking(bool picking) {
            if (!CanSetLobbyData()) return;
            m_lobby.SetData(WKTLobbyConsts.KEY_PICKING, picking ? "1" : string.Empty);
            if (m_picking == picking) return;
            m_picking = picking;
            WKT.Instance.Logger.LogInfo(picking ? "Took the lobby to the trinket screen." : "Took the lobby back from the trinket screen.");
            OnLobbyPickingChanged?.Invoke();
        }

        /// <summary>Readies or unreadies the local player, as their lobby member data, which everyone sees.</summary>
        public void SetLocalReady(bool ready) {
            if (!m_lobbyExists) return;
            m_lobby.SetMemberData(WKTLobbyConsts.KEY_READY, ready ? "1" : "0");
            if (m_localReady == ready) return;
            m_localReady = ready;
            OnReadyChanged?.Invoke();
        }

        /// <summary>How many players in the session are ready; <paramref name="total"/> is how many there are.</summary>
        public int CountReady(out int total) {
            total = 0;
            var ready = 0;
            var local = LocalPlayer;
            foreach (var player in GetPlayersEnumerable()) {
                total++;
                if (player == local ? m_localReady : GetMemberData(player, WKTLobbyConsts.KEY_READY) == "1")
                    ready++;
            }
            return ready;
        }

        /// <summary>
        /// Host only, on the main menu: starts the ready countdown once everyone is ready, stops it when someone no longer is,
        /// and starts the lobby's run when it runs out.
        /// </summary>
        private void UpdateReadyCountdown() {
            if (Mode != NetworkMode.Host || m_server == null || !m_lobbyExists) return;
            var counting = m_countdownEnd != float.MaxValue;
            if (m_server.IsRunInProgress || !m_picking || SceneManager.GetActiveScene().name != WKTSceneConsts.MAIN_MENU) {
                if (counting)
                    StopCountdown();
                return;
            }
            var ready = CountReady(out var total);
            switch (WKTReadyCheck.Step(ready, total, counting, m_time, m_countdownEnd)) {
                case WKTReadyStep.Start:
                    m_countdownEnd = m_time + WKTLobbyConsts.READY_COUNTDOWN;
                    m_countdownToken = (++m_countdownCount).ToString();
                    m_lobby.SetData(WKTLobbyConsts.KEY_COUNTDOWN, m_countdownToken);
                    WKT.Instance.Logger.LogInfo("Everyone is ready. Starting the run in a moment.");
                    OnReadyChanged?.Invoke();
                    break;
                case WKTReadyStep.Cancel:
                    StopCountdown();
                    break;
                case WKTReadyStep.Launch:
                    StopCountdown();
                    LaunchReadyRun();
                    break;
            }
        }

        /// <summary>Host only: the countdown ran out. If the run cannot start, the host unreadies, so it does not count down again at once.</summary>
        private void LaunchReadyRun() {
            var gamemodeId = WKT.Instance.Settings.LocalSelectedGamemodeId;
            if (WKTMultiplayerGamemodes.TryGet(gamemodeId, out var gamemode) && TryStartHostRun(gamemode, gamemodeId)) return;
            WKTToast.Show("The run could not start.");
            SetLocalReady(false);
        }

        private void StopCountdown() {
            var wasCounting = m_countdownEnd != float.MaxValue;
            m_countdownEnd = float.MaxValue;
            if (Mode == NetworkMode.Host && m_lobbyExists && m_nextMode == NetworkMode.Host)
                m_lobby.SetData(WKTLobbyConsts.KEY_COUNTDOWN, string.Empty);
            if (wasCounting)
                OnReadyChanged?.Invoke();
        }

        /// <summary>Host only. Ticks or unticks a shortcut. Only shortcuts every player has unlocked can be ticked.</summary>
        public void SetLobbyShortcutTicked(ShortcutId id, bool ticked) {
            if (!CanSetLobbyData()) return;
            var settings = WKT.Instance.Settings;
            var unticked = WKTShortcuts.SetUnticked(settings.LocalUntickedShortcuts, id, ticked);
            if (unticked == settings.LocalUntickedShortcuts) return;
            settings.LocalUntickedShortcuts = unticked;
            WKT.Instance.Logger.LogInfo((ticked ? "Ticked" : "Unticked") + " shortcut " + id + ".");
            PublishLobbyShortcuts();
        }

        /// <summary>
        /// Host only. Writes which shortcuts every player has unlocked, and which of them are ticked, to the lobby data. Runs
        /// whenever the shared progression changes. Quiet while this machine is not yet the host, such as mid-migration.
        /// </summary>
        private void PublishLobbyShortcuts() {
            if (!m_lobbyExists || m_nextMode != NetworkMode.Host || m_server == null) return;
            m_lobby.SetData(WKTLobbyConsts.KEY_SHORTCUTS_AVAILABLE, WKTLobbyData.FormatShortcutMask(LobbyShortcutsAvailable, m_sb));
            m_lobby.SetData(WKTLobbyConsts.KEY_SHORTCUTS, WKTLobbyData.FormatShortcutMask(LobbyShortcutsTicked, m_sb));
            OnLobbyShortcutsChanged?.Invoke();
            PublishLobbyTrinkets();
        }

        /// <summary>Host only: writes which trinkets every player has unlocked to the lobby data.</summary>
        private void PublishLobbyTrinkets() {
            if (m_server == null || !WKTMultiplayerGamemodes.TryGet(GamemodeId.Campaign, out var gamemode)) return;
            WKTTrinkets.GetAvailable(gamemode.availableTrinkets, m_server.SharedProgression, m_trinketScratch);
            m_lobby.SetData(WKTLobbyConsts.KEY_TRINKETS_AVAILABLE, WKTTrinkets.FormatNames(m_trinketScratch, m_sb));
            m_trinketScratch.Clear();
            OnLobbyTrinketsChanged?.Invoke();
        }

        /// <summary>
        /// Whether <paramref name="trinket"/> may be picked in this lobby: every player has unlocked it. The host knows the
        /// shared progression; clients go by what the host published.
        /// </summary>
        public bool IsLobbyTrinketAvailable(Trinket trinket) {
            switch (Mode) {
                case NetworkMode.Host:
                    var unlock = trinket.progressionUnlock;
                    return m_server != null && WKTTrinkets.IsAvailable(unlock != null ? unlock.id : null, unlock != null, m_server.SharedProgression.HasUnlock);
                case NetworkMode.Client:
                    return m_lobbyTrinkets.Contains(trinket.name);
                default:
                    return trinket.IsUnlocked();
            }
        }

        public void AddLobbyModifier(ModifierId modifierId) {
            if (!CanSetLobbyData()) return;
            if (!m_modifiers.Add(modifierId)) return;
            m_lobby.SetData(WKTLobbyConsts.KEY_MODIFIER, GetModifierString());
            WKT.Instance.Logger.LogInfo($"Added modifier {modifierId} to lobby data.");
            OnLobbyModifiersChanged?.Invoke();
        }

        public void AddLobbyModifiers(IEnumerable<ModifierId> modifierIds) {
            if (!CanSetLobbyData()) return;
            var addedAny = false;
            foreach (var modifierId in modifierIds) {
                if (!m_modifiers.Add(modifierId)) continue;
                addedAny = true;
                WKT.Instance.Logger.LogInfo($"Added modifier {modifierId} to lobby data.");
            }
            if (!addedAny) return;
            m_lobby.SetData(WKTLobbyConsts.KEY_MODIFIER, GetModifierString());
            OnLobbyModifiersChanged?.Invoke();
        }

        public void RemoveLobbyModifier(ModifierId modifierId) {
            if (!CanSetLobbyData()) return;
            if (!m_modifiers.Remove(modifierId)) return;
            m_lobby.SetData(WKTLobbyConsts.KEY_MODIFIER, GetModifierString());
            WKT.Instance.Logger.LogInfo($"Removed modifier {modifierId} from lobby data.");
            OnLobbyModifiersChanged?.Invoke();
        }

        private string GetModifierString() {
            if (m_modifiers.Count == 0) return string.Empty;
            foreach (var modifier in m_modifiers)
                m_sb.Append($"{modifier},");
            var str = m_sb.ToString();
            m_sb.Clear();
            return str;
        }

        private bool CanSetLobbyData() {
            if (!m_lobbyExists) {
                WKT.Instance.Logger.LogError("Cannot set lobby data. Lobby does not exist.");
                return false;
            }
            if (m_nextMode != NetworkMode.Host) {
                WKT.Instance.Logger.LogError("Cannot set lobby data. Local player is not the host.");
                return false;
            }
            return true;
        }

        private void RequestStop() => m_stopRequested = true;

        /// <summary>Once back on the main menu after a restart: the host starts the new run, and clients the one they got.</summary>
        private void ContinueRestart() {
            var gamemodeId = m_restartGamemodeId;
            var bufferedGamemode = m_bufferedRunGamemode;
            ClearRestart();
            if (SceneManager.GetActiveScene().name != WKTSceneConsts.MAIN_MENU) return;
            switch (Mode) {
                case NetworkMode.Host when WKTMultiplayerGamemodes.TryGet(gamemodeId, out var gamemode):
                    TryStartHostRun(gamemode, gamemodeId);
                    break;
                case NetworkMode.Host:
                    WKT.Instance.Logger.LogError($"Cannot restart the run. Gamemode {gamemodeId} is unavailable.");
                    break;
                case NetworkMode.Client when bufferedGamemode != null:
                    StartClientRun(bufferedGamemode);
                    break;
            }
        }

        private void ClearRestart() {
            m_restartPending = false;
            m_restartTime = float.MaxValue;
            m_bufferedRunGamemode = null;
        }

        /// <summary>Ends the session for a reason the player did not choose, and leaves the run if one is being played.</summary>
        private void EndSession(WKTSessionEndReason reason) {
            if (m_pendingEndReason == WKTSessionEndReason.None)
                m_pendingEndReason = reason;
            m_returnToMenuRequested = SceneManager.GetActiveScene().name != WKTSceneConsts.MAIN_MENU;
            WKT.Instance.Logger.LogInfo($"Session ended: {reason}.");
            RequestStop();
        }

        private void ReturnToMainMenu() {
            m_returnToMenuRequested = false;
            if (SceneManager.GetActiveScene().name == WKTSceneConsts.MAIN_MENU) return;
            WKT.Instance.Logger.LogInfo("Returning to the main menu.");
            // The same as the game's own UT_GameStateController.LoadMainMenu, so the run is cleaned up as usual.
            CL_GameManager.gamemode = null;
            if (CL_GameManager.gMan != null)
                CL_GameManager.gMan.LoadScene(WKTSceneConsts.MAIN_MENU, false);
            else
                SceneManager.LoadScene(WKTSceneConsts.MAIN_MENU);
        }

        private void CreateServer() {
            m_server = new WKTServer(Messages, m_gameVersion, WKTPlugin.VERSION, m_localProfile, s_logInfo);
            m_server.PlayerConnectedEvent += p => {
                PublishLobbyMembers();
                OnPlayerConnected?.Invoke(p);
            };
            m_server.PlayerDisconnectedEvent += p => {
                PublishLobbyMembers();
                OnPlayerDisconnected?.Invoke(p);
            };
            m_server.SharedProgressionChangedEvent += PublishLobbyShortcuts;
            m_socket!.Interface = m_server;
        }

        /// <summary>Closes the client's connection without its callbacks reaching the client.</summary>
        private void CloseConnection() {
            if (m_connection == null) return;
            m_connection.Interface = null;
            m_connection.Close();
            m_connection = null;
            m_client?.Attach(null);
        }

        /// <summary>Host only. Before leaving, tells the clients who takes over and makes that player the lobby owner.</summary>
        private void TryHandOverHost() {
            if (Mode != NetworkMode.Host || m_server == null || !m_server.TryHandOver(out var successor)) return;
            if (!m_lobbyExists) return;
            m_lobby.SetData(host_lobby_data, successor!.friend.Id.ToString());
            m_lobby.Owner = successor.friend;
        }

        private void OnClientDisconnected() {
            // A failed attempt to reach the new host. Try again until the migration times out.
            if (m_migration.IsActive) {
                m_migration.OnAttemptFailed(m_time);
                return;
            }
            if (m_client == null || !m_client.IsWelcomed || !m_client.TryPickSuccessor(out var successor)) {
                EndSession(WKTSessionEndReason.HostDisconnected);
                return;
            }
            BeginMigration(successor!);
        }

        private void BeginMigration(WKTPlayer successor) {
            if (m_client == null || !m_migration.TryBegin(successor, m_time)) return;
            WKT.Instance.Logger.LogInfo($"Host left. Player {successor.Id} ({successor.friend.Id}) takes over.");
        }

        private void UpdateMigration() {
            if (Mode != NetworkMode.Client || m_client == null) {
                m_migration.Clear();
                return;
            }
            switch (m_migration.Update(m_time, m_client.LocalPlayer)) {
                case WKTMigrationStep.Promote:
                    PromoteToHost();
                    break;
                case WKTMigrationStep.GiveUp:
                    WKT.Instance.Logger.LogError("Could not reach the new host.");
                    EndSession(WKTSessionEndReason.HostDisconnected);
                    break;
                case WKTMigrationStep.Connect:
                    ConnectToSuccessor(m_migration.Successor!);
                    break;
            }
        }

        private void ConnectToSuccessor(WKTPlayer successor) {
            CloseConnection();
            m_migration.OnAttemptStarted();
            m_client!.BeginReconnect();
            m_connection = SteamNetworkingSockets.ConnectRelay<ConnectionManager>(successor.friend.Id);
            m_client.Attach(m_connection);
            m_connection.Interface = m_client;
            WKT.Instance.Logger.LogInfo($"Connecting to the new host {successor.friend.Id}.");
        }

        private void OnClientHostChanged() {
            m_migration.Clear();
            OnHostChanged?.Invoke();
        }

        /// <summary>
        /// Takes over the session from a host that left. Everyone keeps their id, spawn slot and the run in progress, and
        /// the other players reconnect to this machine.
        /// </summary>
        private void PromoteToHost() {
            var client = m_client!;
            var localPlayer = client.LocalPlayer!;
            var oldHost = client.HostPlayer!;
            m_migration.Clear();
            CloseConnection();
            client.ClearEvents();
            m_client = null;
            m_socket = SteamNetworkingSockets.CreateRelaySocket<SocketManager>();
            CreateServer();
            m_server!.AdoptSession(localPlayer, client.GetPlayersEnumerable(), oldHost.Id, m_time);
            m_server.IsRunInProgress = Session.IsRunActive;
            m_modifiers.Clear();
            WKTLobbyData.AddModifiers(m_lobbyModifierMask, m_modifiers);
            m_lobbyModifierMask = 0;
            m_playMode = m_lobbyPlayMode;
            m_lobbyPlayMode = default;
            m_gooFollow = m_lobbyGooFollow;
            m_lobbyGooFollow = default;
            m_orbRadius = m_lobbyOrbRadius;
            m_lobbyOrbRadius = OrbRadiusId.Medium;
            m_playerCollision = m_lobbyPlayerCollision;
            m_lobbyPlayerCollision = PlayerCollisionId.Soft;
            m_lobbyShortcutsAvailable = 0;
            m_lobbyShortcutsTicked = 0;
            m_lobbyTrinkets.Clear();
            m_picking = m_lobbyPicking;
            m_lobbyPicking = false;
            // The code stays in the lobby data, so the lobby keeps it under its new host.
            m_joinCode = m_lobbyJoinCode;
            m_lobbyJoinCode = string.Empty;
            m_countdownEnd = float.MaxValue;
            m_isLobbyClaimed = false;
            m_nextLobbyOwnerCheckTime = m_time;
            m_nextMode = NetworkMode.Host;
            Mode = NetworkMode.Host;
            WKT.Instance.Logger.LogInfo("Took over as host.");
            PublishLobbyShortcuts();
            OnPlayerDisconnected?.Invoke(oldHost);
            OnHostChanged?.Invoke();
        }

        /// <summary>
        /// Steam picks a new lobby owner on its own when the old one crashed. Only the owner can write lobby data, so the
        /// host must own it: a client that ended up owning it hands it on, and the host writes itself in once it owns it.
        /// </summary>
        private void UpdateLobbyOwnership() {
            if (!m_lobbyExists || m_time < m_nextLobbyOwnerCheckTime) return;
            m_nextLobbyOwnerCheckTime = m_time + WKTNetworkConsts.MIGRATION_RETRY_DELAY;
            var ownsLobby = m_lobby.IsOwnedBy(SteamClient.SteamId);
            switch (Mode) {
                case NetworkMode.Host when ownsLobby && !m_isLobbyClaimed:
                    m_isLobbyClaimed = true;
                    m_lobby.SetData(host_lobby_data, SteamClient.SteamId.ToString());
                    // What the lobby browser shows came from the old host, who may have left before it was up to date.
                    PublishLobbyMembers();
                    PublishLobbyInRun();
                    m_isPingLocationPublished = false;
                    m_nextPingLocationTime = m_time;
                    m_lobby.SetJoinable(true);
                    WKT.Instance.Logger.LogInfo($"Claimed Steam lobby with id {m_lobby.Id}.");
                    break;
                case NetworkMode.Client when ownsLobby:
                    var host = m_migration.Successor ?? m_client?.HostPlayer;
                    if (host == null || host.friend.Id == SteamClient.SteamId) break;
                    m_lobby.Owner = host.friend;
                    WKT.Instance.Logger.LogInfo($"Handed Steam lobby ownership to the host {host.friend.Id}.");
                    break;
            }
        }

        /// <summary>Host only: writes who is in the session, the host first, to the lobby data for the lobby browser.</summary>
        private void PublishLobbyMembers() {
            if (!m_lobbyExists || m_nextMode != NetworkMode.Host || m_server == null) return;
            var host = m_server.HostPlayer;
            if (host != null)
                m_memberNames.Add(host.friend.Name);
            foreach (var player in m_server.GetPlayersEnumerable()) {
                if (!player.IsHost)
                    m_memberNames.Add(player.friend.Name);
            }
            m_lobby.SetData(WKTLobbyConsts.KEY_MEMBERS, WKTLobbyData.FormatMemberNames(m_memberNames, m_sb));
            m_memberNames.Clear();
        }

        /// <summary>Host only: writes whether a run is under way to the lobby data, so the lobby browser shows it cannot be joined.</summary>
        private void PublishLobbyInRun() {
            if (!m_lobbyExists || m_nextMode != NetworkMode.Host || m_server == null) return;
            m_lobby.SetData(WKTLobbyConsts.KEY_IN_RUN, m_server.IsRunInProgress ? "1" : string.Empty);
        }

        /// <summary>Host only: writes the host's ping location to the lobby data once Steam has measured it, for the lobby browser.</summary>
        private void UpdatePingLocation() {
            if (Mode != NetworkMode.Host || !m_lobbyExists || !m_isLobbyClaimed || m_isPingLocationPublished || m_time < m_nextPingLocationTime)
                return;
            m_nextPingLocationTime = m_time + c_ping_location_retry_delay;
            var location = SteamNetworkingUtils.LocalPingLocation;
            if (!location.HasValue) return;
            m_lobby.SetData(WKTLobbyConsts.KEY_PING_LOCATION, location.Value.ToString());
            m_isPingLocationPublished = true;
        }

        private void OnLobbyMemberLeft(Lobby lobby, Friend member) {
            if (Mode != NetworkMode.Host || m_server == null || !m_lobbyExists || lobby.Id != m_lobby.Id) return;
            m_server.Kick(member.Id);
        }

        /// <summary>
        /// Host only. Picks the seed and spawn slots, starts the run locally, and only once that succeeded tells every
        /// client to start the same run.
        /// </summary>
        private bool TryStartHostRun(M_Gamemode gamemode, GamemodeId gamemodeId) {
            var logger = WKT.Instance.Logger;
            var hostPlayer = m_server?.HostPlayer;
            if (m_server == null || hostPlayer == null) {
                logger.LogError("Cannot start a multiplayer run. The host session is not ready yet.");
                return false;
            }
            if (m_server.AwaitingReconnectCount > 0) {
                logger.LogInfo("Cannot start a multiplayer run while players are still reconnecting.");
                return false;
            }
            var shared = m_server.SharedProgression;
            if (!shared.IsGamemodeUnlocked(gamemode.unlockAchievement)) {
                logger.LogInfo($"Cannot start {gamemode.gamemodeName}. Not every player has unlocked it.");
                return false;
            }
            m_runBuffer.Clear();
            m_runBuffer.Seed = UnityEngine.Random.Range(1, WKTRunStart.MAX_SEED);
            m_runBuffer.ClockTicks = DateTime.Now.Ticks;
            m_runBuffer.Gamemode = gamemodeId;
            m_runBuffer.PlayMode = m_playMode;
            m_runBuffer.GooFollow = m_gooFollow;
            m_runBuffer.OrbRadius = m_orbRadius;
            m_runBuffer.PlayerCollision = m_playerCollision;
            m_runBuffer.ShortcutMask = LobbyShortcutsTicked;
            // A modifier hidden in this play mode never applies, whatever it was left at.
            foreach (var modifier in m_modifiers)
                m_runBuffer.SetModifier(modifier, WKTPlayModeDefaults.IsAvailable(m_playMode, modifier));
            // Slots in player id order, so they are stable no matter how the player map is ordered.
            for (var id = 0; id <= byte.MaxValue && m_runBuffer.SlotCount < m_server.PlayerCount; id++) {
                if (m_server.TryGetPlayer((byte)id, out _))
                    m_runBuffer.AddSlot((byte)id);
            }
            m_runBuffer.SharedProgression.CopyFrom(shared);
            if (!WKTRunStarter.TryStart(m_runBuffer, gamemode, hostPlayer.Id, Session)) return false;
            m_server.IsRunInProgress = true;
            PublishLobbyInRun();
            if (m_lobbyExists)
                SetLobbyPicking(false);
            // The session's copy has had modifiers the shared progression lacks removed, so clients apply the same set.
            var writer = BeginMessage(WKTMessageId.StartRun);
            Session.Run.Write(writer);
            m_server.Broadcast(writer, SendType.Reliable);
            logger.LogInfo($"Sent StartRun ({writer.Length} bytes) to {m_server.PlayerCount - 1} clients.");
            return true;
        }

        /// <summary>Client only. Starts the run the host started, or leaves the session if it cannot.</summary>
        private void OnStartRunMessage(WKTPlayer sender, NetDataReader reader) {
            var logger = WKT.Instance.Logger;
            var localPlayer = m_client?.LocalPlayer;
            if (Mode != NetworkMode.Client || !sender.IsHost || localPlayer == null) return;
            if (!WKTRunStart.TryRead(reader, m_runBuffer)) {
                logger.LogError("Received a malformed StartRun. Leaving the session.");
                RequestStop();
                return;
            }
            if (!WKTMultiplayerGamemodes.TryGet(m_runBuffer.Gamemode, out var gamemode)) {
                logger.LogError($"Cannot start the host's run. Gamemode {m_runBuffer.Gamemode} is unavailable. Leaving the session.");
                RequestStop();
                return;
            }
            // Still on the way to the menu after the host restarted: the run starts once it is there.
            if (m_restartPending && SceneManager.GetActiveScene().name != WKTSceneConsts.MAIN_MENU) {
                m_bufferedRunGamemode = gamemode;
                logger.LogInfo("Received the restarted run before reaching the main menu. Starting it once there.");
                return;
            }
            StartClientRun(gamemode);
        }

        /// <summary>Client only. Starts the run in <see cref="m_runBuffer"/>, or leaves the session if it cannot.</summary>
        private void StartClientRun(M_Gamemode gamemode) {
            var localPlayer = m_client?.LocalPlayer;
            if (localPlayer != null && WKTRunStarter.TryStart(m_runBuffer, gamemode, localPlayer.Id, Session)) return;
            WKT.Instance.Logger.LogError("Cannot start the host's run. Leaving the session.");
            RequestStop();
        }

        /// <summary>Client only. The host ended the run for everyone.</summary>
        private void OnReturnToLobbyMessage(WKTPlayer sender, NetDataReader reader) {
            if (Mode != NetworkMode.Client || !sender.IsHost) return;
            WKT.Instance.Logger.LogInfo("The host took every player back to the lobby.");
            WKTToast.Show("The host took everyone back to the lobby.");
            ReturnToMainMenu();
        }

        /// <summary>Client only. The host restarts the run: back to the menu, where its StartRun follows.</summary>
        private void OnRestartRunMessage(WKTPlayer sender, NetDataReader reader) {
            if (Mode != NetworkMode.Client || !sender.IsHost) return;
            WKT.Instance.Logger.LogInfo("The host is restarting the run.");
            m_restartPending = true;
            if (SceneManager.GetActiveScene().name == WKTSceneConsts.MAIN_MENU)
                m_restartTime = m_time;
            else
                ReturnToMainMenu();
        }

        private void OnClientRejected(WKTRejectReason reason) {
            switch (reason) {
                case WKTRejectReason.Kicked:
                    WKTToast.Show("The host kicked you from the lobby.");
                    break;
                case WKTRejectReason.Banned:
                    WKTToast.Show("The host banned you from the lobby.");
                    break;
                case WKTRejectReason.RunInProgress:
                    WKTToast.Show("The game has already started.");
                    break;
            }
            OnRejected?.Invoke(reason);
            EndSession(WKTSessionEndReason.Rejected);
        }

        private void OnLobbyCreated(Result result, Lobby lobby) {
            if (result != Result.OK) {
                WKT.Instance.Logger.LogError($"Failed to create Steam lobby with result {result}.");
                return;
            }
            m_lobby = lobby;
            m_lobbyExists = true;
            m_lobby.SetData(host_lobby_data, SteamClient.SteamId.ToString());
            m_isLobbyClaimed = true;
            m_lobby.SetData(WKTLobbyConsts.KEY_MOD, WKTLobbyConsts.MOD_ID);
            m_lobby.SetData(WKTLobbyConsts.KEY_MOD_VERSION, WKTPlugin.VERSION);
            m_lobby.SetData(WKTLobbyConsts.KEY_GAME_VERSION, m_gameVersion);
            m_lobby.SetData(WKTLobbyConsts.KEY_NAME, WKTLobbyData.GetLobbyName(WKT.Instance.Settings.LocalLobbyName, SteamClient.Name));
            SetLobbyVisibility(WKT.Instance.Settings.LocalLobbyVisibilityId);
            SetLobbyGamemode(WKT.Instance.Settings.LocalSelectedGamemodeId);
            SetLobbyPlayMode(WKT.Instance.Settings.LocalPlayModeId);
            SetLobbyGooFollow(WKT.Instance.Settings.LocalGooFollowId);
            SetLobbyOrbRadius(WKT.Instance.Settings.LocalOrbRadiusId);
            SetLobbyPlayerCollision(WKT.Instance.Settings.LocalPlayerCollisionId);
            PublishLobbyShortcuts();
            m_lobby.SetJoinable(true);
            WKT.Instance.Logger.LogInfo($"Created Steam lobby with id {lobby.Id}.");
            OnHostStarted?.Invoke();
        }

        private void OnGameLobbyJoinRequested(Lobby lobby, SteamId steamId) {
            WKT.Instance.Logger.LogInfo("Handling Steam lobby join request.");
            HandleJoinRequest(lobby.Id);
        }

        private void HandleJoinRequest(SteamId lobbyId) {
            var isOnMainMenu = SceneManager.GetActiveScene().name == WKTSceneConsts.MAIN_MENU;
            var currentLobbyId = m_lobbyExists ? m_lobby.Id.Value : 0UL;
            switch (WKTLobbyJoinRequest.Decide(Mode, isOnMainMenu, lobbyId.Value, currentLobbyId)) {
                case WKTLobbyJoinAction.Ignore:
                    WKT.Instance.Logger.LogInfo($"Already in Steam lobby with id {lobbyId}.");
                    break;
                case WKTLobbyJoinAction.Defer:
                    // Joining from inside a run would put it onto the network, so it waits for the menu.
                    m_pendingLobbyId = lobbyId;
                    WKT.Instance.Logger.LogInfo($"Joining Steam lobby with id {lobbyId} once back on the main menu.");
                    break;
                case WKTLobbyJoinAction.LeaveThenJoin:
                    WKT.Instance.Logger.LogInfo("Leaving the current session to join another lobby.");
                    Stop();
                    JoinLobby(lobbyId);
                    break;
                case WKTLobbyJoinAction.Join:
                    JoinLobby(lobbyId);
                    break;
            }
        }

        /// <summary>Joins a lobby picked in the lobby browser, like accepting an invite to it.</summary>
        internal void JoinListedLobby(SteamId lobbyId) {
            if (IsJoiningLobby) return;
            HandleJoinRequest(lobbyId);
        }

        private void JoinLobby(SteamId lobbyId) {
            WKT.Instance.Logger.LogInfo($"Joining Steam lobby with id {lobbyId}.");
            m_joinStartTime = m_time;
            SteamMatchmaking.JoinLobbyAsync(lobbyId);
        }

        /// <summary>Facepunch reports a lobby entered even when Steam refused entry (full, closed), so membership is checked.</summary>
        private static bool IsLocalMember(Lobby lobby) {
            var localId = SteamClient.SteamId;
            foreach (var member in lobby.Members)
                if (member.Id == localId) return true;
            return false;
        }

        private void OnLobbyEntered(Lobby lobby) {
            m_joinStartTime = float.NegativeInfinity;
            if (!IsLocalMember(lobby)) {
                WKT.Instance.Logger.LogError($"Could not enter Steam lobby with id {lobby.Id}.");
                if (Mode == NetworkMode.None)
                    WKTToast.Show("Could not join the lobby. It may be full or closed.");
                return;
            }
            WKT.Instance.Logger.LogInfo($"Entered Steam lobby with id {lobby.Id}.");
            PublishLocalVoices(lobby);
            if (m_nextMode == NetworkMode.Host)
                m_server?.CreateHostPlayer();
            if (Mode != NetworkMode.None)
                return;
            // The lobby UI opens for this gamemode as soon as the client starts, so a lobby without one is unusable.
            if (!WKTLobbyData.TryParseGamemode(lobby.GetData(WKTLobbyConsts.KEY_GAMEMODE), out var gamemodeId)) {
                WKT.Instance.Logger.LogError($"Leaving Steam lobby with id {lobby.Id}. It has no valid gamemode.");
                lobby.Leave();
                return;
            }
            if (!WKTLobbyData.TryParsePlayMode(lobby.GetData(WKTLobbyConsts.KEY_PLAY_MODE), out var playModeId)) {
                WKT.Instance.Logger.LogError($"Leaving Steam lobby with id {lobby.Id}. It has no valid play mode.");
                lobby.Leave();
                return;
            }
            WKT.Instance.Settings.LocalSelectedGamemodeId = gamemodeId;
            m_lobbyPlayMode = playModeId;
            // Not needed to join: the host's run start decides the run, and a missing value shows the default.
            m_lobbyGooFollow = WKTLobbyData.TryParseGooFollow(lobby.GetData(WKTLobbyConsts.KEY_GOO_FOLLOW), out var gooFollowId) ? gooFollowId : default;
            m_lobbyOrbRadius = WKTLobbyData.TryParseOrbRadius(lobby.GetData(WKTLobbyConsts.KEY_ORB_RADIUS), out var orbRadiusId) ? orbRadiusId : OrbRadiusId.Medium;
            m_lobbyPlayerCollision = WKTLobbyData.TryParsePlayerCollision(lobby.GetData(WKTLobbyConsts.KEY_PLAYER_COLLISION), out var playerCollisionId)
                ? playerCollisionId
                : PlayerCollisionId.Soft;
            m_lobbyModifierMask = WKTLobbyData.ParseModifierMask(lobby.GetData(WKTLobbyConsts.KEY_MODIFIER));
            m_lobbyShortcutsAvailable = WKTLobbyData.ParseShortcutMask(lobby.GetData(WKTLobbyConsts.KEY_SHORTCUTS_AVAILABLE));
            m_lobbyShortcutsTicked = WKTLobbyData.ParseShortcutMask(lobby.GetData(WKTLobbyConsts.KEY_SHORTCUTS));
            WKTTrinkets.ParseNames(lobby.GetData(WKTLobbyConsts.KEY_TRINKETS_AVAILABLE), m_lobbyTrinkets);
            // A late joiner goes straight to the trinket screen if everyone is there. A countdown already under way is
            // stopped by the host anyway, since the new player is not ready.
            m_lobbyPicking = lobby.GetData(WKTLobbyConsts.KEY_PICKING) == "1";
            m_countdownToken = lobby.GetData(WKTLobbyConsts.KEY_COUNTDOWN) ?? string.Empty;
            m_lobbyJoinCode = WKTLobbyData.TryParseJoinCode(lobby.GetData(WKTLobbyConsts.KEY_JOIN_CODE), out var joinCode) ? joinCode : string.Empty;
            m_lobby = lobby;
            m_lobbyExists = true;
            var hostId = m_lobby.GetData(host_lobby_data);
            StartClient(hostId);
        }

        private void PublishLocalVoices(Lobby lobby) {
            lobby.SetMemberData(WKTLobbyConsts.KEY_VOICES, WKTLobbyData.FormatVoiceIds(WKTVoiceCosmetics.GetLocalIds(), m_sb));
            var config = WKTPlugin.ModConfig;
            lobby.SetMemberData(WKTLobbyConsts.KEY_VOICE_CHAT_OFF, config != null && !config.VoiceEnabled.Value ? "1" : "0");
        }

        private void OnLobbyMemberDataChanged(Lobby lobby, Friend member) {
            if (!m_lobbyExists || lobby.Id != m_lobby.Id) return;
            OnReadyChanged?.Invoke();
            if (OnPlayerMemberDataChanged == null) return;
            foreach (var player in GetPlayersEnumerable()) {
                if (player.friend.Id != member.Id) continue;
                OnPlayerMemberDataChanged(player);
                return;
            }
        }

        /// <summary>Client only: whether the host has the lobby on the trinket screen.</summary>
        private void ReadPicking(Lobby lobby) {
            var picking = lobby.GetData(WKTLobbyConsts.KEY_PICKING) == "1";
            if (picking == m_lobbyPicking) return;
            m_lobbyPicking = picking;
            OnLobbyPickingChanged?.Invoke();
        }

        /// <summary>Client only: the host started the ready countdown (a new number), or stopped it (empty). Counted here from when it is seen.</summary>
        private void ReadCountdown(Lobby lobby) {
            var token = lobby.GetData(WKTLobbyConsts.KEY_COUNTDOWN) ?? string.Empty;
            if (token == m_countdownToken) return;
            m_countdownToken = token;
            m_countdownEnd = token.Length > 0 ? m_time + WKTLobbyConsts.READY_COUNTDOWN : float.MaxValue;
            OnReadyChanged?.Invoke();
        }

        private void OnLobbyDataChanged(Lobby lobby) {
            if (Mode != NetworkMode.Client || !m_lobbyExists || lobby.Id != m_lobby.Id) return;
            // A missing or unknown play mode or goo follow keeps the last one. The host only ever writes valid names.
            if (WKTLobbyData.TryParsePlayMode(lobby.GetData(WKTLobbyConsts.KEY_PLAY_MODE), out var playModeId) &&
                playModeId != m_lobbyPlayMode) {
                m_lobbyPlayMode = playModeId;
                OnLobbyPlayModeChanged?.Invoke();
            }
            if (WKTLobbyData.TryParseGooFollow(lobby.GetData(WKTLobbyConsts.KEY_GOO_FOLLOW), out var gooFollowId) &&
                gooFollowId != m_lobbyGooFollow) {
                m_lobbyGooFollow = gooFollowId;
                OnLobbyGooFollowChanged?.Invoke();
            }
            if (WKTLobbyData.TryParseOrbRadius(lobby.GetData(WKTLobbyConsts.KEY_ORB_RADIUS), out var orbRadiusId) &&
                orbRadiusId != m_lobbyOrbRadius) {
                m_lobbyOrbRadius = orbRadiusId;
                OnLobbyOrbRadiusChanged?.Invoke();
            }
            if (WKTLobbyData.TryParsePlayerCollision(lobby.GetData(WKTLobbyConsts.KEY_PLAYER_COLLISION), out var playerCollisionId) &&
                playerCollisionId != m_lobbyPlayerCollision) {
                m_lobbyPlayerCollision = playerCollisionId;
                OnLobbyPlayerCollisionChanged?.Invoke();
            }
            var shortcutsAvailable = WKTLobbyData.ParseShortcutMask(lobby.GetData(WKTLobbyConsts.KEY_SHORTCUTS_AVAILABLE));
            var shortcutsTicked = WKTLobbyData.ParseShortcutMask(lobby.GetData(WKTLobbyConsts.KEY_SHORTCUTS));
            if (shortcutsAvailable != m_lobbyShortcutsAvailable || shortcutsTicked != m_lobbyShortcutsTicked) {
                m_lobbyShortcutsAvailable = shortcutsAvailable;
                m_lobbyShortcutsTicked = shortcutsTicked;
                OnLobbyShortcutsChanged?.Invoke();
            }
            WKTTrinkets.ParseNames(lobby.GetData(WKTLobbyConsts.KEY_TRINKETS_AVAILABLE), m_trinketScratch);
            if (!m_trinketScratch.SetEquals(m_lobbyTrinkets)) {
                m_lobbyTrinkets.Clear();
                m_lobbyTrinkets.UnionWith(m_trinketScratch);
                OnLobbyTrinketsChanged?.Invoke();
            }
            m_trinketScratch.Clear();
            ReadPicking(lobby);
            ReadCountdown(lobby);
            var joinCode = WKTLobbyData.TryParseJoinCode(lobby.GetData(WKTLobbyConsts.KEY_JOIN_CODE), out var parsedCode) ? parsedCode : string.Empty;
            if (joinCode != m_lobbyJoinCode) {
                m_lobbyJoinCode = joinCode;
                OnLobbyJoinCodeChanged?.Invoke();
            }
            var modifierMask = WKTLobbyData.ParseModifierMask(lobby.GetData(WKTLobbyConsts.KEY_MODIFIER));
            if (modifierMask == m_lobbyModifierMask) return;
            m_lobbyModifierMask = modifierMask;
            OnLobbyModifiersChanged?.Invoke();
        }
    }
}