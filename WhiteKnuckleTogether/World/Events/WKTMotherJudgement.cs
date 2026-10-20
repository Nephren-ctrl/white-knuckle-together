using System.Collections.Generic;
using JetBrains.Annotations;
using UnityEngine;
using WhiteKnuckleTogether.Networking.LiteNetLib;
using SendType = Steamworks.Data.SendType;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// Mother's judgement at the end of the Symbiosis prison, in a synced co-op run. In singleplayer she lifts the player to
    /// her platform and, on arrival, checks whether they killed or sacrificed grubs: guilty players are thrown into the Hot
    /// Zone, the rest walk on to the Infested Labs. The route is shared, so she judges the group once: each player's record is
    /// sent to the host as they arrive, and once every living player is there (or after a timeout) the host's verdict, guilty
    /// if anyone is, runs the same branch on every machine.
    /// </summary>
    internal sealed class WKTMotherJudgement : MonoBehaviour {
        private const string c_stat = "grubs-used";
        private const string c_flag = "session_sacrificed_grub";
        private const float c_timeout = 20f;
        private const float c_toast_interval = 4f;
        private const byte c_kind_arrived = 0;
        private const byte c_kind_verdict = 1;
        private readonly Dictionary<byte, bool> m_records = new();
        private WKTNetwork m_network = null!;
        private UT_StatChecker? m_checker;
        private UT_CheckFlag? m_flag;
        private float m_firstArrival = -1f;
        private float m_nextToast;
        private int m_verdict = -1;

        public static WKTMotherJudgement? Instance { get; private set; }

        private bool IsHost => m_network.Mode == NetworkMode.Host;

        [UsedImplicitly]
        private void Awake() {
            m_network = WKT.Instance.Network;
            if (!m_network.Session.IsSyncedWorld) {
                enabled = false;
                return;
            }
            Instance = this;
            m_network.Messages.Register(WKTMessageId.MotherJudgement, OnJudgement);
        }

        [UsedImplicitly]
        private void OnDestroy() {
            if (Instance != this) return;
            Instance = null;
            if (WKT.InstanceOrNull != null)
                m_network.Messages.Unregister(WKTMessageId.MotherJudgement);
        }

        [UsedImplicitly]
        private void Update() {
            if (!IsHost || m_firstArrival < 0f || m_verdict >= 0) return;
            var living = 0;
            var arrived = 0;
            var deaths = WKTPlayerDeaths.Sync;
            for (var id = 0; id <= byte.MaxValue; id++) {
                if (!m_network.TryGetPlayer((byte)id, out var player) || player == null || (deaths != null && deaths.Roster.IsDead((byte)id))) continue;
                living++;
                if (m_records.ContainsKey((byte)id))
                    arrived++;
            }
            if (arrived < living && Time.unscaledTime - m_firstArrival < c_timeout) {
                if (Time.unscaledTime >= m_nextToast) {
                    m_nextToast = Time.unscaledTime + c_toast_interval;
                    WKTToast.Show($"Mother waits for everyone ({arrived}/{living}).");
                }
                return;
            }
            var guilty = false;
            foreach (var record in m_records.Values)
                guilty |= record;
            var writer = m_network.BeginMessage(WKTMessageId.MotherJudgement);
            writer.Put(c_kind_verdict);
            writer.Put(guilty);
            m_network.Broadcast(writer, SendType.Reliable);
            ApplyVerdict(guilty);
        }

        /// <summary>Whether a stat check is Mother's judgement: the grubs-used check paired with the sacrificed-grub flag.</summary>
        public static bool IsJudgement(UT_StatChecker checker, out UT_CheckFlag? flag) {
            flag = null;
            return checker.statName == c_stat && checker.TryGetComponent(out flag) && flag!.flagName == c_flag;
        }

        /// <summary>Whether the game would find a player guilty: too many grubs used, or one sacrificed. Pure.</summary>
        public static bool IsGuilty(int grubsUsed, UT_StatChecker.Evaluation evaluation, int limit, bool sacrificed) {
            var overLimit = evaluation switch {
                UT_StatChecker.Evaluation.equals => grubsUsed == limit,
                UT_StatChecker.Evaluation.greaterThan => grubsUsed > limit,
                UT_StatChecker.Evaluation.lesserThan => grubsUsed < limit,
                _ => false
            };
            return overLimit || sacrificed;
        }

        /// <summary>The local player arrived before Mother: their record goes to the host instead of being judged here.</summary>
        public void OnLocalArrived(UT_StatChecker checker, UT_CheckFlag flag) {
            m_checker = checker;
            m_flag = flag;
            var grubsUsed = StatManager.GetStatisticInt(StatManager.sessionStats, checker.statName);
            var sacrificed = CL_GameManager.GetGameFlag(flag.flagName) is { state: true };
            var guilty = IsGuilty(grubsUsed, checker.evaluation, checker.evaluationInteger, sacrificed);
            WKT.Instance.Logger.LogInfo($"Arrived before Mother: {grubsUsed} grubs used, sacrificed {sacrificed}, guilty {guilty}.");
            if (m_verdict >= 0) {
                ApplyVerdict(m_verdict == 1);
                return;
            }
            if (IsHost) {
                Record(m_network.LocalPlayer?.Id ?? 0, guilty);
                return;
            }
            var writer = m_network.BeginMessage(WKTMessageId.MotherJudgement);
            writer.Put(c_kind_arrived);
            writer.Put(guilty);
            m_network.SendToHost(writer, SendType.Reliable);
        }

        private void Record(byte playerId, bool guilty) {
            m_records[playerId] = guilty;
            if (m_firstArrival < 0f)
                m_firstArrival = Time.unscaledTime;
        }

        private void OnJudgement(WKTPlayer sender, NetDataReader reader) {
            if (!reader.TryGetByte(out var kind) || !reader.TryGetBool(out var guilty)) return;
            switch (kind) {
                case c_kind_arrived when IsHost && !sender.IsHost:
                    Record(sender.Id, guilty);
                    break;
                case c_kind_verdict when !IsHost && sender.IsHost:
                    ApplyVerdict(guilty);
                    break;
            }
        }

        /// <summary>Runs the verdict's branch here, once, if the local player is already before Mother; otherwise on arrival.</summary>
        private void ApplyVerdict(bool guilty) {
            m_verdict = guilty ? 1 : 0;
            if (m_checker == null || m_flag == null) return;
            var checker = m_checker;
            var flag = m_flag;
            m_checker = null;
            m_flag = null;
            WKT.Instance.Logger.LogInfo($"Mother's verdict for the group: {(guilty ? "guilty" : "not guilty")}.");
            if (guilty)
                checker.trueEvent.Invoke();
            else
                flag.flagFalseEvent.Invoke();
        }
    }
}
