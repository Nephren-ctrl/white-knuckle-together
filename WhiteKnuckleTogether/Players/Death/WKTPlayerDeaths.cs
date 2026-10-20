using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using JetBrains.Annotations;
using UnityEngine;
using WhiteKnuckleTogether.Networking.LiteNetLib;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// Deaths in a multiplayer run. The game's own death would stop time, fade out and end the run, so in a multiplayer run
    /// a death only marks the player dead (see <see cref="WKTDeathSync"/>), and <see cref="WKTSpectator"/> takes over. Once
    /// every player is dead, each machine shows its own player's stats the game's way. Lives next to
    /// <see cref="WKTRunController"/>, so it only exists during a multiplayer run.
    /// </summary>
    internal sealed class WKTPlayerDeaths : MonoBehaviour {
        private const string c_basic_death = "basic";
        private const string c_death_messages = "deathmessages";
        private static readonly MethodInfo? s_endGameSequence = AccessTools.Method(typeof(CL_GameManager), "EndGameSequence");
        private static WKTPlayerDeaths? s_active;
        private WKTNetwork m_network = null!;
        private WKTDeathSync? m_sync;
        private Action<WKTPlayer> m_onPlayerDisconnected = null!;
        private Action m_onHostChanged = null!;
        private bool m_hasRunEnded;

        /// <summary>The run's deaths. Null outside a multiplayer run.</summary>
        public static WKTDeathSync? Sync => s_active == null ? null : s_active.m_sync;
        /// <summary>
        /// Whether this machine shows the end of the run (<see cref="ShowRunEnd"/>): everyone dead in co-op, or a race won or
        /// lost, even with players still alive. Death no longer keeps anyone apart then.
        /// </summary>
        public static bool IsRunEnded => s_active != null && s_active.m_hasRunEnded;

        [UsedImplicitly]
        private void Awake() {
            m_network = WKT.Instance.Network;
            var localPlayer = m_network.LocalPlayer;
            if (localPlayer == null) {
                WKT.Instance.Logger.LogError("Cannot sync deaths. There is no local player.");
                enabled = false;
                return;
            }
            s_active = this;
            m_sync = new WKTDeathSync(m_network.Mode == NetworkMode.Host, localPlayer.Id, IsRemotePlayer, SendToHost, Broadcast);
            m_sync.DeathChanged += OnDeathChanged;
            m_sync.RunOver += OnRunOver;
            m_network.Messages.Register(WKTMessageId.PlayerDeath, OnPlayerDeath);
            m_network.Messages.Register(WKTMessageId.PlayerDeathRelay, OnPlayerDeathRelay);
            m_network.Messages.Register(WKTMessageId.RunOver, OnRunOverMessage);
            m_onPlayerDisconnected = OnPlayerDisconnected;
            m_network.OnPlayerDisconnected += m_onPlayerDisconnected;
            m_onHostChanged = OnHostChanged;
            m_network.OnHostChanged += m_onHostChanged;
        }

        [UsedImplicitly]
        private void OnDestroy() {
            if (s_active == this)
                s_active = null;
            if (m_sync == null) return;
            // The network outlives the scene, and may already be gone when the game quits.
            if (WKT.InstanceOrNull == null) return;
            m_network.Messages.Unregister(WKTMessageId.PlayerDeath);
            m_network.Messages.Unregister(WKTMessageId.PlayerDeathRelay);
            m_network.Messages.Unregister(WKTMessageId.RunOver);
            m_network.OnPlayerDisconnected -= m_onPlayerDisconnected;
            m_network.OnHostChanged -= m_onHostChanged;
        }

        [UsedImplicitly]
        private void Update() {
            // A race ends by its own rules (see WKTRace).
            if (m_sync == null || !m_sync.IsHost || m_sync.IsRunOver || !m_network.IsMultiplayerRun || m_network.Session.IsRace) return;
            // Re-checked every frame, so deaths, players leaving and migrations all end the run the same way.
            Span<byte> playerIds = stackalloc byte[byte.MaxValue + 1];
            var count = 0;
            for (var id = 0; id <= byte.MaxValue; id++) {
                if (m_network.TryGetPlayer((byte)id, out _))
                    playerIds[count++] = (byte)id;
            }
            m_sync.TryEndRun(playerIds.Slice(0, count), m_network.AwaitingReconnectCount > 0);
        }

        public static bool IsDead(byte playerId) {
            var sync = Sync;
            return sync != null && sync.Roster.IsDead(playerId);
        }

        /// <summary>
        /// In place of the game's <c>CL_GameManager.Die</c> in a multiplayer run. Keeps what the death looks and sounds like,
        /// but not what ends the run: no slowed time, no fade, no revive from a save, no score screen.
        /// </summary>
        public static void HandleLocalDeath(CL_GameManager gameManager, string type) {
            if (s_active == null || s_active.m_sync == null) {
                WKT.Instance.Logger.LogError("The local player died, but deaths are not synced in this run.");
                return;
            }
            if (s_active.m_sync.IsLocalDead) return;
            // What the game's own Die does first: a chamber the player died in lets them out, and the death counts.
            CL_GameManager._onPlayerDeath?.Invoke();
            StatManager.sessionStats?.UpdateStatistic("deaths", 1, StatManager.Statistic.DataType.Int, StatManager.Statistic.ModType.Add);
            SetDeathText(gameManager, type);
            var player = gameManager.localPlayer != null ? gameManager.localPlayer : ENT_Player.playerObject;
            if (player != null)
                player.KillEffects(type);
            // Some deaths turn pausing off for their own cutscene, but a dead player must still be able to leave.
            gameManager.canPause = true;
            WKT.Instance.Logger.LogInfo($"The local player died ({(type.Length == 0 ? c_basic_death : type)}).");
            s_active.m_sync.SetLocalDead(true);
        }

        /// <summary>The same death text and tip the game would show, for when the score screen shows at the end.</summary>
        private static void SetDeathText(CL_GameManager gameManager, string type) {
            var deathTypes = gameManager.deathTypes;
            if (deathTypes != null && deathTypes.Count > 0) {
                CL_GameManager.curDeathType = deathTypes[0];
                foreach (var deathType in deathTypes) {
                    if (deathType.name == type)
                        CL_GameManager.curDeathType = deathType;
                }
            }
            var screen = gameManager.statManager != null ? gameManager.statManager.GetScoreScreen() : null;
            if (screen == null) return;
            var message = CL_LocalizationManager.GetLocalization(c_death_messages, type);
            if (string.IsNullOrEmpty(message))
                message = CL_LocalizationManager.GetLocalization(c_death_messages, c_basic_death);
            screen.SetDeathText(message);
            screen.SetTip(gameManager.GetReleventTip());
        }

        private bool IsRemotePlayer(byte id) {
            var localPlayer = m_network.LocalPlayer;
            return localPlayer != null && id != localPlayer.Id && m_network.TryGetPlayer(id, out _);
        }

        private void SendToHost(NetDataWriter writer) => m_network.SendToHost(writer, WKTDeathSync.SEND_TYPE);

        private void Broadcast(NetDataWriter writer, WKTPlayer? except) => m_network.Broadcast(writer, WKTDeathSync.SEND_TYPE, except);

        private void OnPlayerDeath(WKTPlayer sender, NetDataReader reader) => m_sync?.HandlePlayerDeath(sender, reader);

        private void OnPlayerDeathRelay(WKTPlayer sender, NetDataReader reader) => m_sync?.HandlePlayerDeathRelay(sender, reader);

        private void OnRunOverMessage(WKTPlayer sender, NetDataReader reader) => m_sync?.HandleRunOver(sender);

        private void OnPlayerDisconnected(WKTPlayer player) => m_sync?.Remove(player.Id);

        /// <summary>Everyone sends their own state to the new host, so every machine agrees even if the old host never relayed it.</summary>
        private void OnHostChanged() {
            if (m_sync == null) return;
            m_sync.IsHost = m_network.Mode == NetworkMode.Host;
            m_sync.SendLocal();
        }

        private void OnDeathChanged(byte playerId, bool dead) {
            if (!m_network.TryGetPlayer(playerId, out var player) || player == null) return;
            var isLocal = player == m_network.LocalPlayer;
            WKT.Instance.Logger.LogInfo($"Player {playerId} ({player.friend.Name}) {(dead ? "died" : "came back")}.");
            if (dead && !isLocal)
                WKTToast.Show($"{player.friend.Name} died");
            WKT.Instance.API.RaisePlayerDeathChanged(player, dead);
        }

        /// <summary>Every player is dead: the game's own end of the run, with the score screen's buttons made multiplayer.</summary>
        private void OnRunOver() {
            WKT.Instance.Logger.LogInfo("Every player is dead. The run is over.");
            ShowRunEnd(false, null);
        }

        /// <summary>
        /// Ends the run on this machine the game's way, with the score screen's buttons made multiplayer.
        /// <paramref name="win"/> shows it as the game's own win. <paramref name="banner"/>, if any, replaces the death text,
        /// such as to name a race's winner.
        /// </summary>
        public static void ShowRunEnd(bool win, string? banner) {
            if (s_active != null)
                s_active.m_hasRunEnded = true;
            WKT.Instance.API.RaiseRunOver();
            var gameManager = CL_GameManager.gMan;
            if (gameManager == null) return;
            // The game shows a separate screen for a win: the buttons and banner go on the one this machine shows.
            var screen = gameManager.statManager != null ? gameManager.statManager.GetScoreScreen(win) : null;
            if (screen != null) {
                WKTScoreScreenButtons.Attach(screen);
                if (banner != null)
                    screen.SetDeathText(banner);
            }
            if (s_endGameSequence != null && s_endGameSequence.Invoke(gameManager, new object[] { win }) is IEnumerator sequence) {
                gameManager.StartCoroutine(sequence);
                return;
            }
            WKT.Instance.Logger.LogError("Could not find the game's end of run. Showing the score screen directly.");
            if (gameManager.statManager != null)
                gameManager.statManager.ShowScoreScreen();
        }
    }
}
