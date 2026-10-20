using System;
using System.Collections.Generic;
using JetBrains.Annotations;
using UnityEngine;
using WhiteKnuckleTogether.Networking.LiteNetLib;
using SendType = Steamworks.Data.SendType;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// The lobby's Item Stealing setting: holding grab with an empty hand on another player's hand that holds an item, while
    /// looking at it, takes the item. The request goes through the host to the victim, whose machine takes the item out of
    /// their hand and sends it back with its data, through the host, to the thief. The thief cannot use it until they let go
    /// of that hand's button, so the grab that took it does not also fire it.
    /// </summary>
    internal sealed class WKTItemStealing : MonoBehaviour {
        /// <summary>How long grab must be held on the hand.</summary>
        public const float HOLD_TIME = 0.2f;
        /// <summary>How far away a hand can be taken from.</summary>
        public const float REACH = 2.5f;
        /// <summary>How close to the hand the view must pass.</summary>
        public const float HAND_RADIUS = 0.3f;
        private static readonly bool[] s_locked = new bool[2];
        private readonly WKTItemSteal m_steal = new();
        private readonly float[] m_held = new float[2];
        private readonly byte[] m_targetPlayer = new byte[2];
        private readonly byte[] m_targetHand = new byte[2];
        private readonly bool[] m_sent = new bool[2];
        private WKTNetwork m_network = null!;
        private WKTPlayerSync m_players = null!;
        /// <summary>The player and hand whose held item shows its outline, or <see cref="byte.MaxValue"/> for none.</summary>
        private byte m_outlinedPlayer = byte.MaxValue;
        private byte m_outlinedHand;

        /// <summary>Set while a stolen item goes into the thief's hand, which is not a find.</summary>
        public static bool IsGiving { get; private set; }

        private bool IsHost => m_network.Mode == NetworkMode.Host;

        [UsedImplicitly]
        private void Awake() {
            m_network = WKT.Instance.Network;
            m_players = GetComponent<WKTPlayerSync>();
            Array.Clear(s_locked, 0, s_locked.Length);
            m_network.Messages.Register(WKTMessageId.ItemSteal, OnSteal);
        }

        [UsedImplicitly]
        private void OnDestroy() {
            Array.Clear(s_locked, 0, s_locked.Length);
            if (WKT.InstanceOrNull != null)
                m_network.Messages.Unregister(WKTMessageId.ItemSteal);
        }

        [UsedImplicitly]
        private void Update() {
            var local = ENT_Player.playerObject;
            UpdateOutline(local);
            if (local == null || local.hands == null) return;
            for (var hand = 0; hand < 2 && hand < local.hands.Length; hand++) {
                var pressed = IsPressed(local.hands[hand]);
                if (!pressed)
                    s_locked[hand] = false;
                if (!m_network.Session.ItemStealing || local.dead || !pressed) {
                    m_held[hand] = 0f;
                    m_sent[hand] = false;
                    continue;
                }
                if (!m_sent[hand])
                    UpdateHand(local, hand);
            }
        }

        /// <summary>
        /// The item in another player's hand shows its outline only while the local player could steal it: stealing is on, they
        /// are alive with a hand free, and they look at it.
        /// </summary>
        private void UpdateOutline(ENT_Player? local) {
            var victim = byte.MaxValue;
            var victimHand = (byte)0;
            var camera = Camera.main;
            if (m_network.Session.ItemStealing && local != null && !local.dead && HasFreeHand(local) && camera != null &&
                TryFindTarget(camera.transform, out var target, out var targetHand, out _)) {
                victim = target;
                victimHand = targetHand;
            }
            if (victim == m_outlinedPlayer && victimHand == m_outlinedHand) return;
            if (m_outlinedPlayer != byte.MaxValue && m_players.TryGetRemote(m_outlinedPlayer, out var old))
                old.SetHeldOutline(m_outlinedHand, false);
            m_outlinedPlayer = victim;
            m_outlinedHand = victimHand;
            if (victim != byte.MaxValue && m_players.TryGetRemote(victim, out var remote))
                remote.SetHeldOutline(victimHand, true);
        }

        private static bool HasFreeHand(ENT_Player local) {
            if (local.hands == null) return false;
            foreach (var hand in local.hands) {
                if (hand != null && hand.inventoryHand?.currentItem == null && !hand.IsHolding()) return true;
            }
            return false;
        }

        /// <summary>Whether the local player cannot use what is in <paramref name="hand"/> yet, having just stolen it.</summary>
        public static bool IsLocked(int hand) => hand is >= 0 and < 2 && s_locked[hand];

        /// <summary>
        /// The nearest hand holding an item that the view ray from <paramref name="origin"/> along <paramref name="direction"/>
        /// passes within <paramref name="radius"/> of, no farther than <paramref name="reach"/>; its distance along the ray. Pure.
        /// </summary>
        public static bool IsAimedAt(Vector3 origin, Vector3 direction, Vector3 hand, float reach, float radius, out float along) {
            var offset = hand - origin;
            along = Vector3.Dot(offset, direction);
            if (along < 0f || along > reach) return false;
            return (offset - direction * along).sqrMagnitude <= radius * radius;
        }

        private static bool IsPressed(ENT_Player.Hand? hand) =>
            hand != null && !string.IsNullOrEmpty(hand.fireButton) && InputManager.GetButton(hand.fireButton).Pressed;

        private void UpdateHand(ENT_Player local, int hand) {
            var playerHand = local.hands[hand];
            var camera = Camera.main;
            if (camera == null || playerHand.inventoryHand?.currentItem != null || playerHand.IsHolding() || !TryFindTarget(camera.transform, out var victim, out var victimHand, out var prefab)) {
                m_held[hand] = 0f;
                return;
            }
            if (victim != m_targetPlayer[hand] || victimHand != m_targetHand[hand]) {
                m_targetPlayer[hand] = victim;
                m_targetHand[hand] = victimHand;
                m_held[hand] = 0f;
            }
            m_held[hand] += Time.deltaTime;
            if (m_held[hand] < HOLD_TIME || m_network.LocalPlayer is not { } me) return;
            m_sent[hand] = true;
            m_steal.Kind = WKTItemSteal.KIND_REQUEST;
            m_steal.ThiefId = me.Id;
            m_steal.VictimId = victim;
            m_steal.VictimHand = victimHand;
            m_steal.ThiefHand = (byte)hand;
            m_steal.PrefabId = prefab;
            m_steal.ItemData.Clear();
            Send(m_steal);
        }

        private bool TryFindTarget(Transform view, out byte victim, out byte victimHand, out string prefab) {
            victim = 0;
            victimHand = 0;
            prefab = string.Empty;
            var bestAlong = float.PositiveInfinity;
            var origin = view.position;
            var direction = view.forward;
            for (var i = 0; i < WKTPlayerPositions.RemoteCount; i++) {
                var id = WKTPlayerPositions.GetRemoteId(i);
                if (WKTPlayerPositions.IsRemoteDead(i) || !m_players.TryGetRemote(id, out var remote) || !remote.IsShown || !m_players.TryGetHeldItems(id, out var held)) continue;
                for (var hand = 0; hand < 2; hand++) {
                    var heldName = held.Get(hand);
                    if (string.IsNullOrEmpty(heldName) || !IsAimedAt(origin, direction, remote.GetHandPosition(hand), REACH, HAND_RADIUS, out var along) || along >= bestAlong) continue;
                    bestAlong = along;
                    victim = id;
                    victimHand = (byte)hand;
                    prefab = heldName;
                }
            }
            return bestAlong < float.PositiveInfinity;
        }

        /// <summary>Sends a step on its way: the host routes it, a client sends it to the host.</summary>
        private void Send(WKTItemSteal steal) {
            if (IsHost) {
                Route(steal);
                return;
            }
            var writer = m_network.BeginMessage(WKTMessageId.ItemSteal);
            steal.Write(writer);
            m_network.SendToHost(writer, SendType.Reliable);
        }

        /// <summary>Host only: a request goes to the victim, a hand-over to the thief, handled here when that is the host.</summary>
        private void Route(WKTItemSteal steal) {
            var target = steal.Kind == WKTItemSteal.KIND_REQUEST ? steal.VictimId : steal.ThiefId;
            if (m_network.LocalPlayer is { } me && me.Id == target) {
                Handle(steal);
                return;
            }
            if (!m_network.TryGetPlayer(target, out var player) || player == null) return;
            var writer = m_network.BeginMessage(WKTMessageId.ItemSteal);
            steal.Write(writer);
            m_network.SendToPlayer(player, writer, SendType.Reliable);
        }

        private void OnSteal(WKTPlayer sender, NetDataReader reader) {
            if (!WKTItemSteal.TryRead(reader, m_steal)) return;
            if (IsHost) {
                // Each step speaks only for whoever sent it: the thief asks, the victim hands over.
                var from = m_steal.Kind == WKTItemSteal.KIND_REQUEST ? m_steal.ThiefId : m_steal.VictimId;
                if (sender.IsHost || sender.Id != from) return;
                Route(m_steal);
                return;
            }
            if (sender.IsHost)
                Handle(m_steal);
        }

        private void Handle(WKTItemSteal steal) {
            if (!m_network.Session.ItemStealing || m_network.LocalPlayer is not { } me) return;
            if (steal.Kind == WKTItemSteal.KIND_REQUEST && me.Id == steal.VictimId)
                HandOver(steal);
            else if (steal.Kind == WKTItemSteal.KIND_GIVE && me.Id == steal.ThiefId)
                Receive(steal);
        }

        /// <summary>The local player is the victim: the item leaves their hand, unless it changed or cannot be let go.</summary>
        private void HandOver(WKTItemSteal steal) {
            var inventory = Inventory.instance;
            var local = ENT_Player.playerObject;
            if (inventory == null || local == null || local.dead || steal.VictimHand >= inventory.itemHands.Length) return;
            var item = inventory.itemHands[steal.VictimHand].currentItem;
            if (item == null || item.prefabName != steal.PrefabId) return;
            var handItem = item.GetHandItem();
            if (handItem != null && !handItem.CanDrop()) return;
            steal.Kind = WKTItemSteal.KIND_GIVE;
            steal.ItemData.Clear();
            WKTItemModules.Pack(item);
            if (item.data != null) {
                for (var i = 0; i < item.data.Count && i < WKTWire.MAX_ITEM_DATA; i++)
                    steal.ItemData.Add(item.data[i] ?? string.Empty);
            }
            inventory.DestroyItemInHand(steal.VictimHand);
            WKTToast.Show($"{NameOf(steal.ThiefId)} took your {item.itemName}!");
            Send(steal);
        }

        /// <summary>The local player is the thief: the item goes into the hand that took it, or the bag if that hand is full now.</summary>
        private void Receive(WKTItemSteal steal) {
            var inventory = Inventory.instance;
            Item_Object? prefab;
            try {
                prefab = CL_AssetManager.GetItemObjectPrefab(steal.PrefabId);
            }
            catch (Exception) {
                prefab = null;
            }
            if (inventory == null || prefab == null || prefab.itemData == null) {
                WKT.Instance.Logger.LogWarning($"Could not receive a stolen {steal.PrefabId}.");
                return;
            }
            var clone = prefab.itemData.GetClone();
            clone.data ??= new List<string>();
            clone.data.Clear();
            clone.data.AddRange(steal.ItemData);
            // The clone shares the prefab's modules until loaded into its own.
            clone.Load();
            WKTItemModules.Unpack(clone);
            IsGiving = true;
            try {
                if (steal.ThiefHand < inventory.itemHands.Length && inventory.itemHands[steal.ThiefHand].currentItem == null) {
                    inventory.AddItemToHand(clone, steal.ThiefHand);
                    s_locked[steal.ThiefHand] = true;
                }
                else
                    inventory.AddItemToInventoryCenter(clone);
            }
            finally {
                IsGiving = false;
            }
            WKTChat.PostEvent($"{WKTChat.LocalName()} stole {WKTChatText.PlayerName(steal.VictimId, NameOf(steal.VictimId))}'s {WKTChatText.Highlight(clone.itemName)}!");
        }

        private string NameOf(byte id) => m_players.TryGetRemote(id, out var remote) ? remote.DisplayName : "Someone";
    }
}
