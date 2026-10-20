using System.Collections.Generic;
using JetBrains.Annotations;
using UnityEngine;
using WhiteKnuckleTogether.Networking.LiteNetLib;
using SendType = Steamworks.Data.SendType;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// One purse of roaches for the whole run, when the lobby shares roaches. The host keeps the purse:
    /// <list type="bullet">
    /// <item>Any change a client makes (banking roaches, a recycler's payout, spending) goes to the host, which applies it and
    /// tells everyone the new total. The client's own count changes at once and is corrected by that total.</item>
    /// <item>A client buying a priced perk first asks the host, which holds the roaches back for it if the purse, less what is
    /// already held back, can pay. Two players buying at once can never both spend the same roaches.</item>
    /// <item>Each player's starting roaches go into the purse when the run starts.</item>
    /// </list>
    /// </summary>
    internal sealed class WKTSharedRoaches : MonoBehaviour {
        private const float c_hold_timeout = 15f;
        private const int c_max_change = 9999;
        private readonly Dictionary<(byte Player, byte Request), (int Cost, float Since)> m_holds = new();
        private readonly List<(byte, byte)> m_scratch = new();
        private WKTNetwork m_network = null!;
        private App_PerkPage? m_pendingPage;
        private byte m_pendingRequest;
        private byte m_nextRequest;
        private int m_prepaid;
        private bool m_contributed;

        public static WKTSharedRoaches? Instance { get; private set; }
        /// <summary>Set while a total from the host is applied, so it is not sent back.</summary>
        public static bool IsApplying { get; private set; }

        [UsedImplicitly]
        private void Awake() {
            m_network = WKT.Instance.Network;
            if (!m_network.Session.ShareRoaches) {
                enabled = false;
                return;
            }
            Instance = this;
            m_network.Messages.Register(WKTMessageId.RoachChange, OnChange);
            m_network.Messages.Register(WKTMessageId.RoachTotal, OnTotal);
            m_network.Messages.Register(WKTMessageId.RoachSpendRequest, OnSpendRequest);
            m_network.Messages.Register(WKTMessageId.RoachSpendAnswer, OnSpendAnswer);
        }

        [UsedImplicitly]
        private void OnDestroy() {
            if (Instance != this) return;
            Instance = null;
            if (WKT.InstanceOrNull == null) return;
            m_network.Messages.Unregister(WKTMessageId.RoachChange);
            m_network.Messages.Unregister(WKTMessageId.RoachTotal);
            m_network.Messages.Unregister(WKTMessageId.RoachSpendRequest);
            m_network.Messages.Unregister(WKTMessageId.RoachSpendAnswer);
        }

        [UsedImplicitly]
        private void Update() {
            if (!m_contributed && ENT_Player.playerObject != null && CL_GameManager.gMan != null) {
                m_contributed = true;
                // The starting roaches each player brought go into the purse; the host's are in it already.
                if (m_network.Mode != NetworkMode.Host)
                    SendChange(CL_GameManager.GetRoaches());
                else
                    BroadcastTotal();
            }
            if (m_network.Mode != NetworkMode.Host || m_holds.Count == 0) return;
            m_scratch.Clear();
            foreach (var pair in m_holds) {
                if (Time.unscaledTime - pair.Value.Since > c_hold_timeout)
                    m_scratch.Add(pair.Key);
            }
            foreach (var key in m_scratch)
                m_holds.Remove(key);
        }

        #region Changes

        /// <summary>
        /// The game changes this machine's run roaches (<c>CL_GameManager.AddRoaches</c>). False skips nothing: the change always
        /// applies here at once. A client sends it to the host; the host tells everyone the new total.
        /// </summary>
        public void OnLocalChange(int amount) {
            if (IsApplying || amount == 0) return;
            if (m_network.Mode == NetworkMode.Host) {
                BroadcastTotal();
                return;
            }
            // The roaches a perk the host approved costs were already held back there: the purchase spends them.
            if (amount < 0 && m_prepaid > 0) {
                var paid = Mathf.Min(m_prepaid, -amount);
                m_prepaid -= paid;
                SendChange(amount, paid);
                return;
            }
            SendChange(amount);
        }

        private void SendChange(int amount, int fromHold = 0) {
            var writer = m_network.BeginMessage(WKTMessageId.RoachChange);
            writer.Put(Mathf.Clamp(amount, -c_max_change, c_max_change));
            writer.Put(fromHold);
            m_network.SendToHost(writer, SendType.Reliable);
        }

        private void OnChange(WKTPlayer sender, NetDataReader reader) {
            if (m_network.Mode != NetworkMode.Host || sender.IsHost || !reader.TryGetInt(out var amount) || !reader.TryGetInt(out var fromHold) ||
                amount < -c_max_change || amount > c_max_change || fromHold < 0) return;
            if (fromHold > 0)
                ReleaseHold(sender.Id, fromHold);
            IsApplying = true;
            try {
                CL_GameManager.AddRoaches(amount);
            }
            finally {
                IsApplying = false;
            }
            BroadcastTotal();
        }

        private void ReleaseHold(byte player, int amount) {
            m_scratch.Clear();
            foreach (var pair in m_holds) {
                if (pair.Key.Player == player && amount > 0) {
                    amount -= pair.Value.Cost;
                    m_scratch.Add(pair.Key);
                }
            }
            foreach (var key in m_scratch)
                m_holds.Remove(key);
        }

        private void BroadcastTotal() {
            var writer = m_network.BeginMessage(WKTMessageId.RoachTotal);
            writer.Put(CL_GameManager.GetRoaches());
            m_network.Broadcast(writer, SendType.Reliable);
        }

        private void OnTotal(WKTPlayer sender, NetDataReader reader) {
            if (m_network.Mode == NetworkMode.Host || !sender.IsHost || !reader.TryGetInt(out var total) || total < 0) return;
            IsApplying = true;
            try {
                CL_GameManager.AddRoaches(total - CL_GameManager.GetRoaches());
            }
            finally {
                IsApplying = false;
            }
        }

        #endregion

        #region Perk purchases

        /// <summary>
        /// A perk page is about to buy its selected perk. False holds the purchase back: a client asks the host first, and
        /// buys once it answers; the host checks the purse less what is held back for others.
        /// </summary>
        public bool BeforePurchase(App_PerkPage page, int cost) {
            if (cost <= 0) return true;
            if (m_network.Mode == NetworkMode.Host) {
                if (CL_GameManager.GetRoaches() - GetHeld() >= cost) return true;
                WKTToast.Show("Not enough roaches: another player is buying with them.");
                return false;
            }
            if (m_prepaid >= cost) return true;
            if (m_pendingPage != null) return false;
            m_pendingPage = page;
            m_pendingRequest = ++m_nextRequest;
            var writer = m_network.BeginMessage(WKTMessageId.RoachSpendRequest);
            writer.Put(m_pendingRequest);
            writer.Put(cost);
            m_network.SendToHost(writer, SendType.Reliable);
            return false;
        }

        private void OnSpendRequest(WKTPlayer sender, NetDataReader reader) {
            if (m_network.Mode != NetworkMode.Host || sender.IsHost || !reader.TryGetByte(out var request) || !reader.TryGetInt(out var cost) ||
                cost <= 0 || cost > c_max_change) return;
            var approved = CL_GameManager.GetRoaches() - GetHeld() >= cost;
            if (approved)
                m_holds[(sender.Id, request)] = (cost, Time.unscaledTime);
            var writer = m_network.BeginMessage(WKTMessageId.RoachSpendAnswer);
            writer.Put(request);
            writer.Put(approved);
            writer.Put(cost);
            m_network.SendToPlayer(sender, writer, SendType.Reliable);
        }

        private void OnSpendAnswer(WKTPlayer sender, NetDataReader reader) {
            if (!sender.IsHost || !reader.TryGetByte(out var request) || !reader.TryGetBool(out var approved) || !reader.TryGetInt(out var cost) ||
                request != m_pendingRequest || m_pendingPage == null) return;
            var page = m_pendingPage;
            m_pendingPage = null;
            if (!approved) {
                WKTToast.Show("Not enough roaches: another player spent them first.");
                return;
            }
            m_prepaid = cost;
            page.PurchaseSelected();
            // A purchase that did not go through after all gives the held-back roaches up.
            if (m_prepaid <= 0) return;
            m_prepaid = 0;
            SendChange(0, cost);
        }

        #endregion

        /// <summary>Host only: the roaches held back for purchases clients are making.</summary>
        private int GetHeld() {
            var held = 0;
            foreach (var hold in m_holds.Values)
                held += hold.Cost;
            return held;
        }
    }
}
