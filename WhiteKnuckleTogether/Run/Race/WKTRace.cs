using System;
using System.Collections.Generic;
using JetBrains.Annotations;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using WhiteKnuckleTogether.Networking.LiteNetLib;
using SendType = Steamworks.Data.SendType;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// A race. An orb follows whoever is highest, and after a short grace anyone outside it loses health faster the longer
    /// they stay out. Nobody is revived. The last player alive wins, or the first to finish the run. Every player then sees
    /// the end of the run with the winner named.
    /// <para>
    /// The same orb keeps a co-op group together with <see cref="ModifierId.KeepTogether"/>: it follows the middle of the
    /// living players instead, and nobody wins; the run ends as co-op's does.
    /// </para>
    /// <para>
    /// The host moves the orb and decides the winner; each machine hurts only its own player, as with all damage. The orb's
    /// center travels in wire space, so it sits in the same place for everyone whatever their world offset, instanced or
    /// not (instanced players climb identical worlds).
    /// </para>
    /// Lives next to <see cref="WKTRunController"/> in a race, or a co-op run keeping together.
    /// </summary>
    internal sealed class WKTRace : MonoBehaviour {
        private const float c_send_interval = 0.1f;
        private const float c_client_follow_rate = 8f;
        private const float c_warning_font_size = 40f;
        private const float c_warning_top = 180f;
        private const SendType c_orb_send_type = SendType.Unreliable | SendType.NoNagle;
        private const string c_death_type = "race_orb";
        private WKTNetwork m_network = null!;
        private WKTRaceOrb m_orb = null!;
        private GameObject m_canvas = null!;
        private TMP_Text m_warning = null!;
        private Vector3 m_center;
        private Vector3 m_target;
        private Vector3 m_velocity;
        private float m_radius;
        private float m_startTime;
        private float m_nextSend;
        private float m_secondsOutside;
        private float m_nextDamage;
        private int m_started;
        private bool m_hasCenter;
        private bool m_over;
        /// <summary>A race, rather than a co-op group kept together, which has no winner.</summary>
        private bool m_isRace;
        /// <summary>Whom this machine saw die, in order, for the places shown at the end.</summary>
        private readonly List<byte> m_eliminated = new();
        private readonly bool[] m_seenDead = new bool[byte.MaxValue + 1];
        private readonly byte[] m_players = new byte[WKTLobbyConsts.MAX_PLAYERS];
        private readonly bool[] m_dead = new bool[WKTLobbyConsts.MAX_PLAYERS];
        private readonly byte[] m_standings = new byte[WKTLobbyConsts.MAX_PLAYERS];
        private readonly bool[] m_standingAlive = new bool[WKTLobbyConsts.MAX_PLAYERS];
        private WKTRaceResults? m_results;

        public static WKTRace? Instance { get; private set; }

        [UsedImplicitly]
        private void Awake() {
            Instance = this;
            m_network = WKT.Instance.Network;
            m_isRace = m_network.Session.IsRace;
            var run = m_network.Session.Run;
            m_radius = WKTRaceRules.GetRadius(run.OrbRadius);
            m_started = run.SlotCount;
            m_startTime = Time.time;
            m_orb = new WKTRaceOrb(m_radius);
            BuildHud();
            m_network.Messages.Register(WKTMessageId.RaceOrb, OnOrbMessage);
            m_network.Messages.Register(WKTMessageId.RaceFinish, OnFinishMessage);
            m_network.Messages.Register(WKTMessageId.RaceOver, OnOverMessage);
            WKT.Instance.Logger.LogInfo($"{(m_isRace ? "Racing" : "Keeping together")} with an orb of {m_radius} m among {m_started} players.");
        }

        [UsedImplicitly]
        private void OnDestroy() {
            if (Instance == this)
                Instance = null;
            m_orb.Destroy();
            if (m_canvas != null)
                Destroy(m_canvas);
            var wkt = WKT.InstanceOrNull;
            if (wkt == null) return;
            wkt.Network.Messages.Unregister(WKTMessageId.RaceOrb);
            wkt.Network.Messages.Unregister(WKTMessageId.RaceFinish);
            wkt.Network.Messages.Unregister(WKTMessageId.RaceOver);
        }

        [UsedImplicitly]
        private void Update() {
            if (m_results != null && !m_results.Update())
                m_results = null;
            var player = ENT_Player.playerObject;
            if (player == null || m_over) return;
            TrackEliminations();
            var isHost = m_network.Mode == NetworkMode.Host;
            if (isHost)
                UpdateLeader(player);
            if (!m_hasCenter) return;
            // A client follows the host's orb, which never lags the leader by more than a quarter radius, and never lags it by more.
            m_center = isHost ? WKTRaceRules.MoveOrb(m_center, m_target, ref m_velocity, m_radius, Time.deltaTime)
                : WKTRaceRules.ClampLag(Vector3.Lerp(m_center, m_target, 1f - Mathf.Exp(-c_client_follow_rate * Time.deltaTime)), m_target, m_radius);
            m_orb.Position = m_center;
            if (isHost && Time.unscaledTime >= m_nextSend)
                SendOrb();
            UpdateLocalPlayer(player);
            if (isHost && m_isRace)
                CheckElimination();
        }

        /// <summary>
        /// In place of the game's win at the end of the run, during a race: the first to get there wins for everyone. True
        /// when the race took it over.
        /// </summary>
        public static bool TryHandleFinish() {
            var race = Instance;
            if (race == null || !race.m_isRace) return false;
            if (race.m_over) return true;
            var local = race.m_network.LocalPlayer;
            if (local == null) return true;
            if (race.m_network.Mode == NetworkMode.Host)
                race.Decide(local.Id, true);
            else
                race.m_network.SendToHost(race.m_network.BeginMessage(WKTMessageId.RaceFinish), SendType.Reliable);
            return true;
        }

        #region Orb

        /// <summary>Host only: the orb's target is the highest living player in a race, else the middle of the living players.</summary>
        private void UpdateLeader(ENT_Player player) {
            var local = m_network.LocalPlayer;
            var count = 0;
            var leader = Vector3.zero;
            var sum = Vector3.zero;
            if (local != null && !WKTPlayerDeaths.IsDead(local.Id)) {
                leader = player.transform.position;
                sum = leader;
                count = 1;
            }
            for (var i = 0; i < WKTPlayerPositions.RemoteCount; i++) {
                if (WKTPlayerPositions.IsRemoteDead(i) || WKTPlayerDeaths.IsDead(WKTPlayerPositions.GetRemoteId(i))) continue;
                var position = WKTPlayerPositions.GetRemotePosition(i);
                sum += position;
                if (count++ > 0 && position.y <= leader.y) continue;
                leader = position;
            }
            if (count == 0) return;
            m_target = (m_isRace ? leader : sum / count) + Vector3.up * WKTRaceRules.CENTER_HEIGHT;
            if (m_hasCenter) return;
            m_hasCenter = true;
            m_center = m_target;
        }

        private void SendOrb() {
            m_nextSend = Time.unscaledTime + c_send_interval;
            var writer = m_network.BeginMessage(WKTMessageId.RaceOrb);
            WKTWire.Put(writer, WKTWorldOffset.ToWire(m_center, WKTWorldOffset.Get()));
            m_network.Broadcast(writer, c_orb_send_type);
        }

        private void OnOrbMessage(WKTPlayer sender, NetDataReader reader) {
            if (!sender.IsHost || m_network.Mode == NetworkMode.Host || !WKTWire.TryGetPosition(reader, out var wire)) return;
            m_target = WKTWorldOffset.FromWire(wire, WKTWorldOffset.Get());
            if (m_hasCenter) return;
            m_hasCenter = true;
            m_center = m_target;
        }

        #endregion

        #region Local player

        /// <summary>Hurts the local player while they are outside the orb, after the grace, and warns them.</summary>
        private void UpdateLocalPlayer(ENT_Player player) {
            var elapsed = Time.time - m_startTime;
            var local = m_network.LocalPlayer;
            var alive = local != null && !WKTPlayerDeaths.IsDead(local.Id) && !player.dead;
            var outside = alive && WKTRaceRules.IsOutside(player.transform.position, m_center, m_radius);
            var hurting = WKTRaceRules.IsHurting(elapsed);
            UpdateWarning(alive, outside, hurting, elapsed);
            if (!outside || !hurting) {
                m_secondsOutside = 0f;
                return;
            }
            m_secondsOutside += Time.deltaTime;
            if (!WKTRaceRules.IsDamaging(m_secondsOutside) || Time.time < m_nextDamage) return;
            m_nextDamage = Time.time + WKTRaceRules.DAMAGE_TICK_SECONDS;
            var amount = player.maxHealth * WKTRaceRules.GetDamagePerSecond(m_secondsOutside) * WKTRaceRules.DAMAGE_TICK_SECONDS;
            player.Damage(Damageable.DamageInfo.CreateDamageInfo(amount, c_death_type));
        }

        private void UpdateWarning(bool alive, bool outside, bool hurting, float elapsed) {
            string text;
            if (!alive)
                text = string.Empty;
            else if (!hurting)
                text = outside ? $"Get to the orb! It hurts in {Mathf.CeilToInt(WKTRaceRules.GRACE_SECONDS - elapsed)}" : string.Empty;
            else
                text = outside ? "RETURN TO THE ORB!" : string.Empty;
            if (m_warning.text == text) return;
            WKTGameFont.Adopt(m_warning);
            m_warning.text = text;
        }

        #endregion

        #region End

        /// <summary>Host only: ends the race when one player is left alive out of several, or nobody is.</summary>
        private void CheckElimination() {
            if (m_network.AwaitingReconnectCount > 0) return;
            var alive = 0;
            var lastAlive = WKTRaceRules.NO_WINNER;
            foreach (var player in m_network.GetPlayersEnumerable()) {
                if (WKTPlayerDeaths.IsDead(player.Id)) continue;
                alive++;
                lastAlive = player.Id;
            }
            if (WKTRaceRules.IsDecided(m_started, alive, lastAlive, out var winner))
                Decide(winner, false);
        }

        private void OnFinishMessage(WKTPlayer sender, NetDataReader reader) {
            if (m_network.Mode != NetworkMode.Host || sender.IsHost || m_over || WKTPlayerDeaths.IsDead(sender.Id)) return;
            Decide(sender.Id, true);
        }

        /// <summary>Host only: tells everyone who won, then ends the race here too.</summary>
        private void Decide(byte winner, bool finished) {
            if (m_over) return;
            var writer = m_network.BeginMessage(WKTMessageId.RaceOver);
            writer.Put(winner);
            writer.Put(finished);
            m_network.Broadcast(writer, SendType.Reliable);
            End(winner, finished);
        }

        private void OnOverMessage(WKTPlayer sender, NetDataReader reader) {
            if (!sender.IsHost || m_network.Mode == NetworkMode.Host || !reader.TryGetByte(out var winner) || !reader.TryGetBool(out var finished)) return;
            End(winner, finished);
        }

        private void End(byte winner, bool finished) {
            if (m_over) return;
            m_over = true;
            m_warning.text = string.Empty;
            var local = m_network.LocalPlayer;
            var won = local != null && winner == local.Id;
            var winnerName = winner != WKTRaceRules.NO_WINNER && m_network.TryGetPlayer(winner, out var player) && player != null ? player.friend.Name : null;
            var hasWinner = winnerName != null;
            var how = finished ? "finished first and won the race" : "outlasted everyone and won the race";
            var plain = !hasWinner ? "Nobody won the race." : won ? $"You {how}!" : $"{winnerName} {how}.";
            var banner = !hasWinner || won ? plain : $"<color=#{WKTPlayerColors.GetHex(winner)}>{WKTChatText.Escape(winnerName!)}</color> {how}.";
            WKT.Instance.Logger.LogInfo($"The race is over. Winner: {(hasWinner ? winner.ToString() : "nobody")}.");
            WKTChat.PostSystem(plain);
            WKTToast.Show(banner);
            ShowResults(winner, won, banner);
            // Only reaching the end counts as the game's own win; outlasting the others ends the run like a loss.
            WKTPlayerDeaths.ShowRunEnd(won && finished, banner);
        }

        /// <summary>Notes each player's death as it happens here, so the end can tell who lasted longer.</summary>
        private void TrackEliminations() {
            foreach (var player in m_network.GetPlayersEnumerable()) {
                if (m_seenDead[player.Id] || !WKTPlayerDeaths.IsDead(player.Id)) continue;
                m_seenDead[player.Id] = true;
                m_eliminated.Add(player.Id);
            }
        }

        /// <summary>Every player's place, on a card over the end of the run.</summary>
        private void ShowResults(byte winner, bool won, string title) {
            TrackEliminations();
            var count = 0;
            foreach (var player in m_network.GetPlayersEnumerable()) {
                if (count == m_players.Length) break;
                m_players[count] = player.Id;
                m_dead[count++] = WKTPlayerDeaths.IsDead(player.Id);
            }
            Array.Sort(m_players, m_dead, 0, count);
            var placed = WKTRaceRules.GetStandings(winner, m_players.AsSpan(0, count), m_dead.AsSpan(0, count), m_eliminated.ToArray(), m_standings);
            for (var i = 0; i < placed; i++)
                m_standingAlive[i] = !WKTPlayerDeaths.IsDead(m_standings[i]);
            m_results = WKTRaceResults.Show(m_canvas.transform, title, won, m_standings.AsSpan(0, placed), m_standingAlive.AsSpan(0, placed), winner, m_network);
        }

        #endregion

        private void BuildHud() {
            m_canvas = new GameObject("WKT Race HUD");
            var canvas = m_canvas.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = short.MaxValue - 5;
            var scaler = m_canvas.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            var rect = (RectTransform)new GameObject("Warning", typeof(RectTransform)).transform;
            rect.SetParent(m_canvas.transform, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -c_warning_top);
            rect.sizeDelta = new Vector2(1200f, 80f);
            var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            text.fontSize = c_warning_font_size;
            text.color = new Color(1f, 0.3f, 0.3f);
            text.alignment = TextAlignmentOptions.Center;
            text.raycastTarget = false;
            m_warning = text;
        }
    }
}
