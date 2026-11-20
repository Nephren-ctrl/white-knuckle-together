using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Events;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// Rho altars in a synced co-op run (<c>ENV_RhoAltar</c>). Every machine would run its own against its own copy of each
    /// sacrificed item and pay its own player, so one sacrifice paid everyone. Only the host's altars run:
    /// <list type="bullet">
    /// <item>A sacrifice is taken for whoever last dropped the item into the world, else the host (as for
    /// <see cref="WKTRecyclers"/>): their bindings decide whether it is taken, and the flags it sets are theirs too.</item>
    /// <item>A trade pays the player whose sacrifice last counted toward it, else the host. A remote payee's perks reach them
    /// as Mother's rewards do (see <see cref="WKTMotherTrades.BeginReward"/>) and their bindings decide what the altar gives.
    /// Items it gives are the host's world objects, which everyone sees.</item>
    /// <item>Every client plays what the host's altar shows and sounds (see <see cref="WKTHazardSync"/>).</item>
    /// </list>
    /// Owned by <see cref="WKTWorldSync"/>.
    /// </summary>
    internal sealed class WKTRhoAltars {
        /// <summary>A trade picked none of the altar's own rewards: the Pitons-and-Beans binding's beans.</summary>
        public const byte NO_REWARD = byte.MaxValue;
        private static readonly AccessTools.FieldRef<ENV_RhoAltar, int> s_currentTrades = AccessTools.FieldRefAccess<ENV_RhoAltar, int>("currentTrades");
        private static readonly AccessTools.FieldRef<ENV_RhoAltar, int> s_currentValue = AccessTools.FieldRefAccess<ENV_RhoAltar, int>("currentValue");
        private static readonly AccessTools.FieldRef<ENV_RhoAltar, List<GameObject>> s_ignoreList = AccessTools.FieldRefAccess<ENV_RhoAltar, List<GameObject>>("ignoreList");
        private readonly WKTWorldSync m_sync;
        /// <summary>Host only: for each altar, by instance id, the player whose sacrifice last counted toward its next trade.</summary>
        private readonly Dictionary<int, byte> m_payees = new();
        /// <summary>Host only: altars whose rewards note which of them a trade picked.</summary>
        private readonly HashSet<int> m_wired = new();
        /// <summary>The altar whose take or trade is running, while it does.</summary>
        private ENV_RhoAltar? m_running;
        /// <summary>The running altar's value (for a take) or trade count (for a trade) before it ran.</summary>
        private int m_before;
        private byte m_taker;
        private byte m_reward = NO_REWARD;

        public WKTRhoAltars(WKTWorldSync sync) => m_sync = sync;

        /// <summary>Client only: plays what the host's altar just did here, without what it gives or sets.</summary>
        public static void Replay(ENV_RhoAltar altar, WKTHazardCall call, byte reward) {
            var position = altar.transform.position;
            switch (call) {
                case WKTHazardCall.AltarPlace:
                    RunPresentation(altar.placeEvent);
                    break;
                case WKTHazardCall.AltarDeny:
                    AudioManager.PlaySound(altar.denySound, position);
                    break;
                case WKTHazardCall.AltarReceive:
                    AudioManager.PlaySound(altar.recieveSound, position);
                    RunPresentation(altar.recieveEvent);
                    break;
                case WKTHazardCall.AltarTrade:
                    if (altar.rewards != null && reward < altar.rewards.Count)
                        RunPresentation(altar.rewards[reward]?.tradeEvent);
                    if (altar.acceptSounds is { Count: > 0 } sounds)
                        AudioManager.PlaySound(sounds[Random.Range(0, sounds.Count)], position);
                    RunPresentation(altar.tradeEvent);
                    break;
            }
        }

        public void Clear() {
            m_payees.Clear();
            m_wired.Clear();
            m_running = null;
        }

        /// <summary>
        /// An item touched an altar. False skips the altar's check: only the host's altars take items. <paramref name="ignored"/>
        /// is how many objects the altar ignored before, or -1 when the game's own early out skips the item (the altar is spent,
        /// or took or gave it already).
        /// </summary>
        public bool BeforeCheck(ENV_RhoAltar altar, Item_Object item, out int ignored) {
            ignored = -1;
            if (!m_sync.IsHost) return false;
            var list = s_ignoreList(altar);
            if (s_currentTrades(altar) < altar.maxTrades && !list.Contains(item.gameObject))
                ignored = list.Count;
            return true;
        }

        /// <summary>Host only: the altar checked an item. It took it if it now ignores it, else it refused it.</summary>
        public void AfterCheck(ENV_RhoAltar altar, int ignored) {
            if (ignored >= 0)
                m_sync.Hazards.OnAltar(altar, s_ignoreList(altar).Count > ignored ? WKTHazardCall.AltarPlace : WKTHazardCall.AltarDeny);
        }

        /// <summary>
        /// Host only: an altar's delayed take of <paramref name="item"/> (its <c>WaitToTake</c> coroutine) is about to run, for
        /// whoever dropped it. False skips it: the item was handed to a player meanwhile.
        /// </summary>
        public bool BeforeTake(ENV_RhoAltar altar, Item_Object? item) {
            if (item == null || item.itemData == null || item.itemData.hasBeenDestroyed) return false;
            m_running = altar;
            m_before = s_currentValue(altar);
            m_taker = m_sync.Recyclers.PayeeOf(item);
            WKTMotherTrades.BeginReward(m_taker);
            return true;
        }

        /// <summary>Host only: the take ran. One that counted makes its taker the payee of the altar's next trade.</summary>
        public void AfterTake() {
            WKTMotherTrades.EndReward();
            var altar = m_running;
            m_running = null;
            if (altar == null || s_currentValue(altar) <= m_before) return;
            m_payees[altar.GetInstanceID()] = m_taker;
            m_sync.Hazards.OnAltar(altar, WKTHazardCall.AltarReceive);
        }

        /// <summary>An altar is about to trade. False skips it: only the host's altars trade, for their payee.</summary>
        public bool BeforeTrade(ENV_RhoAltar altar) {
            if (!m_sync.IsHost) return false;
            Wire(altar);
            m_running = altar;
            m_before = s_currentTrades(altar);
            m_reward = NO_REWARD;
            if (m_payees.TryGetValue(altar.GetInstanceID(), out var payee))
                WKTMotherTrades.BeginReward(payee);
            return true;
        }

        /// <summary>Host only: the trade ran. One that went through is played on every client's copy.</summary>
        public void AfterTrade() {
            WKTMotherTrades.EndReward();
            var altar = m_running;
            m_running = null;
            if (altar == null || s_currentTrades(altar) == m_before) return;
            m_payees.Remove(altar.GetInstanceID());
            m_sync.Hazards.OnAltar(altar, WKTHazardCall.AltarTrade, m_reward);
        }

        /// <summary>
        /// Runs only the listeners of an altar event that show something here: its particles, its scrawl and the altar switching
        /// itself off. Its perk giver, achievement and flags are the host's.
        /// </summary>
        private static void RunPresentation(UnityEvent? unityEvent) {
            if (unityEvent == null) return;
            var masks = default(WKTListenerMasks);
            var count = unityEvent.GetPersistentEventCount();
            for (var i = 0; i < count; i++) {
                var target = unityEvent.GetPersistentTarget(i);
                masks = masks.With(i, target != null && (target is UT_ScrawlEffect || WKTWorldWiring.IsEntityLocalTarget(target.GetType(), false)));
            }
            WKTUnityEventFields.InvokeFiltered(unityEvent, null, masks, false);
        }

        /// <summary>Has each of an altar's rewards note that a trade picked it, once per altar.</summary>
        private void Wire(ENV_RhoAltar altar) {
            if (altar.rewards == null || !m_wired.Add(altar.GetInstanceID())) return;
            for (var i = 0; i < altar.rewards.Count && i < NO_REWARD; i++) {
                var index = (byte)i;
                altar.rewards[i]?.tradeEvent?.AddListener(() => m_reward = index);
            }
        }
    }
}
