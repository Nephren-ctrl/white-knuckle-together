using System;
using System.Collections.Generic;
using HarmonyLib;
using JetBrains.Annotations;
using UnityEngine;
using WhiteKnuckleTogether.Networking.LiteNetLib;
using SendType = Steamworks.Data.SendType;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// Giving items to Mother in a shared world. She is the host's, so the host runs every trade:
    /// <list type="bullet">
    /// <item>She holds out her hand for anyone near her with an item, as she does for the host.</item>
    /// <item>A client handing her an item gives it up from their own hand; the host makes it in her hand and she takes it,
    /// looks it over and keeps it (grubs) or hands it back, as everyone sees.</item>
    /// <item>What a trade earns goes to the player who gave: a perk, the flags it sets (such as a sacrificed grub, which
    /// her judgement counts), and the few seconds she keeps grubs from being eaten. The host's own perks never decide
    /// another player's trade.</item>
    /// <item>Everyone hears what she says about it.</item>
    /// </list>
    /// Lives next to <see cref="WKTRunController"/>; does nothing outside a shared world.
    /// </summary>
    internal sealed class WKTMotherTrades : MonoBehaviour {
        public const float OFFER_DISTANCE = 4f;
        /// <summary>The host's allowance over <see cref="OFFER_DISTANCE"/> for a giver it sees a little behind.</summary>
        public const float ACCEPT_DISTANCE = 6f;
        private const byte c_nobody = byte.MaxValue;

        private const string c_no_shops_perk = "Perk_Binding_NoShops";
        private static readonly int s_extendHand = Animator.StringToHash("ExtendHand");
        private static readonly AccessTools.FieldRef<DEN_Mother, bool> s_trading = AccessTools.FieldRefAccess<DEN_Mother, bool>("trading");
        private static readonly AccessTools.FieldRef<DEN_Mother, Item?> s_heldItem = AccessTools.FieldRefAccess<DEN_Mother, Item?>("heldItem");
        private static WKTMotherTrades? s_instance;
        private readonly List<string> m_itemData = new();
        private WKTNetwork m_network = null!;
        /// <summary>Host only: the Mother with an item from a trade in her hand, whose lines everyone hears.</summary>
        private DEN_Mother? m_tradingMother;
        /// <summary>Host only: the player whose trade is being worked out right now, while <see cref="DEN_Mother.GrabItem"/> runs.</summary>
        private byte m_trader = c_nobody;

        /// <summary>A remote player's trade is being worked out: what it earns is theirs.</summary>
        public static bool IsRemoteTrade => s_instance != null && s_instance.m_trader != c_nobody;

        [UsedImplicitly]
        private void Awake() {
            s_instance = this;
            m_network = WKT.Instance.Network;
            m_network.Messages.Register(WKTMessageId.MotherTrade, OnTrade);
            m_network.Messages.Register(WKTMessageId.MotherReward, OnReward);
        }

        [UsedImplicitly]
        private void OnDestroy() {
            if (s_instance == this)
                s_instance = null;
            var wkt = WKT.InstanceOrNull;
            if (wkt == null) return;
            wkt.Network.Messages.Unregister(WKTMessageId.MotherTrade);
            wkt.Network.Messages.Unregister(WKTMessageId.MotherReward);
        }

        [UsedImplicitly]
        private void Update() {
            var mother = DEN_Mother.instance;
            if (mother == null || !m_network.Session.IsSyncedWorld) return;
            if (m_network.Mode == NetworkMode.Host)
                WatchHeldItem(mother);
            else
                UpdateClientOffer(mother);
        }

        #region Offering

        /// <summary>
        /// Host only, after Mother's own update: she holds out her hand for a remote player near her with an item too. The
        /// game only looks at the host's player.
        /// </summary>
        public static void AfterMotherUpdate(DEN_Mother mother) {
            var trades = s_instance;
            if (trades == null || trades.m_network.Mode != NetworkMode.Host || !trades.m_network.Session.IsSyncedWorld) return;
            if (mother.focused || !mother.canTrade || s_trading(mother)) return;
            if (trades.IsRemotePlayerOffering(mother))
                mother.animator.SetBool(s_extendHand, true);
        }

        private bool IsRemotePlayerOffering(DEN_Mother mother) {
            var players = WKTPlayerSync.Instance;
            if (players == null || mother.tradeSpot == null) return false;
            var spot = mother.tradeSpot.position;
            for (var i = 0; i < WKTPlayerPositions.RemoteCount; i++) {
                var id = WKTPlayerPositions.GetRemoteId(i);
                if (WKTPlayerPositions.IsRemoteDead(i) || Vector3.Distance(spot, WKTPlayerPositions.GetRemotePosition(i)) >= OFFER_DISTANCE) continue;
                if (players.TryGetHeldItems(id, out var held) && (!string.IsNullOrEmpty(held.Left) || !string.IsNullOrEmpty(held.Right)))
                    return true;
            }
            return false;
        }

        /// <summary>Client only: Mother's own update does not run on a client, so her hand opens to the local player here.</summary>
        private static void UpdateClientOffer(DEN_Mother mother) {
            var player = ENT_Player.playerObject;
            if (player == null || mother.itemInteractor == null || mother.tradeSpot == null) return;
            var offering = !mother.focused && mother.canTrade && !player.dead && player.HasItemInAnyHand() &&
                           Vector3.Distance(mother.tradeSpot.position, player.transform.position) < OFFER_DISTANCE;
            var interactor = mother.itemInteractor.gameObject;
            var parent = mother.itemInteractor.transform.parent.gameObject;
            if (interactor.activeSelf != offering)
                interactor.SetActive(offering);
            if (parent.activeSelf != offering)
                parent.SetActive(offering);
        }

        #endregion

        #region Handing over

        /// <summary>
        /// Client only, in place of the game's hand-in to Mother: the item leaves the local hand and goes to the host, who puts
        /// it in her hand. False when the hand-in was taken over.
        /// </summary>
        public static bool BeforeInteract(UT_ItemInteractor interactor, Clickable.InteractionInfo info) {
            var trades = s_instance;
            var mother = DEN_Mother.instance;
            if (trades == null || mother == null || mother.itemInteractor != interactor || trades.m_network.Mode != NetworkMode.Client ||
                !trades.m_network.Session.IsSyncedWorld || !mother.TryGetComponent<WKTNetIdentity>(out var identity) || identity.Handle == 0) return true;
            var hand = info.hand;
            var item = hand?.GetHandItem()?.item;
            var inventory = Inventory.instance;
            if (hand == null || item == null || inventory == null || hand.id >= inventory.itemHands.Length || !hand.GetHandItem().CanDrop()) return false;
            var writer = trades.m_network.BeginMessage(WKTMessageId.MotherTrade);
            writer.Put(identity.Handle);
            writer.Put(item.prefabName, WKTWire.MAX_ID_LENGTH);
            WriteData(writer, item.data);
            inventory.DestroyItemInHand(hand.id);
            hand.SetHandWait(0.25f);
            trades.m_network.SendToHost(writer, SendType.Reliable);
            return false;
        }

        /// <summary>Host only: a client gave Mother an item.</summary>
        private void OnTrade(WKTPlayer sender, NetDataReader reader) {
            if (m_network.Mode != NetworkMode.Host || sender.IsHost || !reader.TryGetUShort(out var handle) ||
                !WKTReaderUtility.TryGetBoundedString(reader, WKTWire.MAX_ID_LENGTH, out var prefabName) || !TryReadData(reader, m_itemData)) return;
            var sync = WKTWorldSync.Instance;
            if (sync == null || !sync.TryGetIdentity(handle, out var identity) || identity!.Entity is not DEN_Mother mother) return;
            if (!CanAccept(mother, sender.Id)) {
                // Mother is busy with someone else's item: it lands at her feet for anyone to pick up, never lost.
                Spawn(prefabName, mother.tradeSpot.position, null);
                return;
            }
            var item = Spawn(prefabName, Vector3.zero, mother.itemInteractor);
            if (item == null) return;
            m_trader = sender.Id;
            try {
                mother.GrabItem(item);
            }
            finally {
                m_trader = c_nobody;
            }
        }

        private bool CanAccept(DEN_Mother mother, byte giver) =>
            !mother.focused && mother.canTrade && !s_trading(mother) && s_heldItem(mother) == null &&
            WKTPlayerPositions.TryGetRemotePosition(giver, out var position) && Vector3.Distance(mother.tradeSpot.position, position) < ACCEPT_DISTANCE;

        /// <summary>Host only: makes the item, placed the way her hand would take it, or at <paramref name="position"/> without one.</summary>
        private Item? Spawn(string prefabName, Vector3 position, UT_ItemInteractor? placer) {
            Item_Object? prefab;
            try {
                prefab = CL_AssetManager.GetItemObjectPrefab(prefabName);
            }
            catch (Exception) {
                prefab = null;
            }
            if (prefab == null || prefab.itemData == null) {
                WKT.Instance.Logger.LogWarning($"Could not make a {prefabName} given to Mother.");
                return null;
            }
            var item = prefab.itemData.GetClone();
            item.data ??= new List<string>();
            item.data.Clear();
            item.data.AddRange(m_itemData);
            var drop = item.GetDropObject().transform;
            if (placer != null && placer.dropPlacer != null) {
                drop.SetPositionAndRotation(placer.dropPlacer.position, placer.dropPlacer.rotation);
                if (placer.parentToPlacer)
                    drop.parent = placer.dropPlacer;
            }
            else
                drop.position = position;
            drop.gameObject.SetActive(true);
            return item;
        }

        #endregion

        #region Rewards

        /// <summary>Host only, while a remote player's trade is worked out: their reward perk goes to them.</summary>
        public static bool BeforeAddPerk(Perk perk) {
            var trades = s_instance;
            if (trades == null || trades.m_trader == c_nobody || perk == null || string.IsNullOrEmpty(perk.id)) return true;
            trades.SendReward(WKTMotherRewardKind.Perk, perk.id);
            return false;
        }

        /// <summary>Host only, while a remote player's trade is worked out: the flags it sets are set for them too.</summary>
        public static void OnFlagSet(string flag, bool state) {
            var trades = s_instance;
            if (trades != null && trades.m_trader != c_nobody && state && !string.IsNullOrEmpty(flag))
                trades.SendReward(WKTMotherRewardKind.Flag, flag);
        }

        /// <summary>Host only, while a remote player's trade is worked out: whether the giver has a perk, not the host.</summary>
        public static bool TryGetTraderPerk(string perkId, out bool has) {
            var trades = s_instance;
            has = false;
            if (trades == null || trades.m_trader == c_nobody || perkId != c_no_shops_perk) return false;
            has = WKTPlayerPerks.Has(trades.m_trader, perkId);
            return true;
        }

        private void SendReward(WKTMotherRewardKind kind, string value) {
            if (!m_network.TryGetPlayer(m_trader, out var player) || player == null) return;
            var writer = m_network.BeginMessage(WKTMessageId.MotherReward);
            writer.Put((byte)kind);
            writer.Put(value, WKTWire.MAX_ID_LENGTH);
            m_network.SendToPlayer(player, writer, SendType.Reliable);
        }

        /// <summary>Client only: what the local player's trade earned.</summary>
        private void OnReward(WKTPlayer sender, NetDataReader reader) {
            if (!sender.IsHost || m_network.Mode == NetworkMode.Host || !reader.TryGetByte(out var kind) ||
                !WKTReaderUtility.TryGetBoundedString(reader, WKTWire.MAX_ID_LENGTH, out var value) || value.Length == 0) return;
            switch ((WKTMotherRewardKind)kind) {
                case WKTMotherRewardKind.Perk:
                    GivePerk(value);
                    return;
                case WKTMotherRewardKind.Flag:
                    CL_GameManager.SetGameFlag(value, true);
                    return;
                case WKTMotherRewardKind.Given:
                    DEN_Mother.motherGiveTime = Time.time;
                    return;
            }
        }

        private static void GivePerk(string perkId) {
            var player = ENT_Player.playerObject;
            Perk? perk;
            try {
                perk = CL_AssetManager.GetPerkAsset(perkId);
            }
            catch (Exception) {
                perk = null;
            }
            if (player == null || perk == null) {
                WKT.Instance.Logger.LogWarning($"Could not receive Mother's {perkId}.");
                return;
            }
            player.AddPerk(perk);
        }

        #endregion

        #region Lines and held items

        /// <summary>Host only: whether <paramref name="mother"/> is busy with a trade, so everyone hears what she says.</summary>
        public static bool IsTrading(DEN_Mother mother) => s_instance != null && s_instance.m_tradingMother == mother;

        /// <summary>Host only: Mother is taking an item from someone.</summary>
        public static void OnGrabbed(DEN_Mother mother) {
            if (s_instance != null)
                s_instance.m_tradingMother = mother;
            // Grubs cannot be eaten for a moment after giving one; the giver is where that matters.
            if (s_instance != null && s_instance.m_trader != c_nobody)
                s_instance.SendReward(WKTMotherRewardKind.Given, "1");
        }

        /// <summary>Host only: Mother's hand is empty again.</summary>
        public static void OnReleased(DEN_Mother mother) {
            if (s_instance != null && s_instance.m_tradingMother == mother && s_heldItem(mother) == null)
                s_instance.m_tradingMother = null;
        }

        /// <summary>
        /// Host only: a client taking the item out of her hand removes the host's copy without the game's pickup, which is
        /// what frees her for the next trade. Done here instead.
        /// </summary>
        private static void WatchHeldItem(DEN_Mother mother) {
            if (!s_trading(mother)) return;
            var held = s_heldItem(mother);
            if (held == null) return;
            var drop = held.GetDropObject();
            if (drop != null && drop.gameObject.activeInHierarchy) return;
            mother.RemoveItem(held);
        }

        #endregion

        public static void WriteData(NetDataWriter writer, List<string>? data) {
            var count = data == null ? 0 : Math.Min(data.Count, WKTWire.MAX_ITEM_DATA);
            writer.Put((byte)count);
            for (var i = 0; i < count; i++)
                writer.Put(data![i] ?? string.Empty, WKTWire.MAX_ID_LENGTH);
        }

        public static bool TryReadData(NetDataReader reader, List<string> data) {
            data.Clear();
            if (!reader.TryGetByte(out var count) || count > WKTWire.MAX_ITEM_DATA) return false;
            for (var i = 0; i < count; i++) {
                if (!WKTReaderUtility.TryGetBoundedString(reader, WKTWire.MAX_ID_LENGTH, out var value)) return false;
                data.Add(value);
            }
            return true;
        }
    }
}
