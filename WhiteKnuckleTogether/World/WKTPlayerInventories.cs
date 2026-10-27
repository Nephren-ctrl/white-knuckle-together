using System.Collections.Generic;
using HarmonyLib;
using Steamworks.Data;
using UnityEngine;
using WhiteKnuckleTogether.Networking.LiteNetLib;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// Everything every player carries, on every machine, so a player who leaves the run (quitting, crashing, timing out or
    /// being kicked) drops it all where they were last seen. Each player sends their own inventory when it changes; the host
    /// keeps it and relays it to everyone else. Whoever hosts when the player leaves spawns their items, which covers the host
    /// leaving too: the player who takes over drops the old host's. A dead player's items are not dropped. Owned by
    /// <see cref="WKTWorldSync"/>.
    /// </summary>
    internal sealed class WKTPlayerInventories {
        /// <summary>How often the items are packed anyway, so changes to their own state (ammo, charge) are sent too.</summary>
        private const double c_refresh_interval = 5;
        private static readonly AccessTools.FieldRef<Inventory, List<Inventory.Pouch>> s_pouches =
            AccessTools.FieldRefAccess<Inventory, List<Inventory.Pouch>>("pouches");
        private readonly WKTWorldSync m_sync;
        private readonly WKTNetwork m_network;
        private readonly Dictionary<byte, WKTInventorySnapshot> m_byPlayer = new();
        private readonly Stack<WKTInventorySnapshot> m_pool = new();
        private readonly List<PendingDrop> m_pendingDrops = new();
        /// <summary>The local items last sent, in order, with whether each was used.</summary>
        private readonly List<Item> m_sentItems = new();
        private readonly List<bool> m_sentUsed = new();
        private readonly List<Item> m_walk = new();
        private readonly WKTInventorySnapshot m_sent = new();
        private readonly WKTInventorySnapshot m_scratch = new();
        private double m_nextRefresh;
        private bool m_sentOnce;
        /// <summary>
        /// Host only: this machine took over, and once everyone is back it tells each of them what everyone carries. Until
        /// then, the players still reconnecting would miss what it sends, and the items it drops.
        /// </summary>
        private bool m_isResyncPending;

        public WKTPlayerInventories(WKTWorldSync sync, WKTNetwork network) {
            m_sync = sync;
            m_network = network;
        }

        public void Clear() {
            foreach (var snapshot in m_byPlayer.Values)
                Release(snapshot);
            m_byPlayer.Clear();
            foreach (var drop in m_pendingDrops)
                Release(drop.Items);
            m_pendingDrops.Clear();
            m_sentItems.Clear();
            m_sentUsed.Clear();
            m_walk.Clear();
            m_sent.Clear();
            m_nextRefresh = 0;
            m_sentOnce = false;
            m_isResyncPending = false;
        }

        /// <summary>
        /// The host changed. A client sends its own inventory to the new host again, which may have missed it. A new host
        /// waits for everyone to be back and then sends them all.
        /// </summary>
        public void OnHostChanged() {
            m_sentOnce = false;
            m_isResyncPending = m_sync.IsHost;
        }

        public void Update(double now) {
            if (m_sync.IsHost && m_network.AwaitingReconnectCount > 0) return;
            if (m_isResyncPending) {
                m_isResyncPending = false;
                foreach (var player in m_network.GetPlayersEnumerable())
                    SendAllTo(player);
            }
            DropPending();
            // Checked every frame, so an item dropped right before leaving is gone from the mirror before the host drops it again.
            var inventory = Inventory.instance;
            var local = m_network.LocalPlayer;
            if (inventory == null || local == null) return;
            var refresh = now >= m_nextRefresh;
            if (!refresh && m_sentOnce && !HasChanged(inventory)) return;
            m_nextRefresh = now + c_refresh_interval;
            Collect(inventory);
            if (m_sentOnce && m_scratch.Matches(m_sent)) return;
            m_sent.CopyFrom(m_scratch);
            m_sentOnce = true;
            if (m_sync.IsHost) {
                Broadcast(local.Id, m_sent, null);
                return;
            }
            var writer = m_network.BeginMessage(WKTMessageId.PlayerInventory);
            m_sent.Write(writer);
            m_network.SendToHost(writer, SendType.Reliable);
        }

        /// <summary>Host only: tells a player who just arrived what everyone carries.</summary>
        public void SendAllTo(WKTPlayer player) {
            if (!m_sync.IsHost || player.IsHost) return;
            var local = m_network.LocalPlayer;
            if (local != null && m_sentOnce)
                SendTo(player, local.Id, m_sent);
            foreach (var pair in m_byPlayer) {
                if (pair.Key != player.Id)
                    SendTo(player, pair.Key, pair.Value);
            }
        }

        /// <summary>A client's own inventory reaching the host, or anyone's relayed by the host.</summary>
        public void OnInventory(WKTPlayer sender, NetDataReader reader) {
            if (m_sync.IsHost) {
                if (sender.IsHost || !WKTInventorySnapshot.TryRead(reader, m_scratch)) return;
                Store(sender.Id, m_scratch);
                Broadcast(sender.Id, m_scratch, sender);
                return;
            }
            if (!sender.IsHost || !reader.TryGetByte(out var playerId) || !WKTInventorySnapshot.TryRead(reader, m_scratch)) return;
            var local = m_network.LocalPlayer;
            if (local == null || playerId != local.Id)
                Store(playerId, m_scratch);
        }

        /// <summary>
        /// A player left: their items are dropped where they were last seen, by this machine once it is the host. Their
        /// position and death are last frame's, so it does not matter which other handlers forgot them first.
        /// </summary>
        public void OnPlayerLeft(byte playerId) {
            if (!m_byPlayer.Remove(playerId, out var items)) return;
            var isKnown = WKTPlayerPositions.TryGetRemote(playerId, out var position, out var dead);
            if (items.Count != 0 && isKnown && !dead) {
                m_pendingDrops.Add(new PendingDrop(playerId, position, items));
                return;
            }
            if (items.Count != 0)
                WKT.Instance.Logger.LogInfo($"Player {playerId} left {(dead ? "dead" : "from nowhere known")}: their {items.Count} items are not dropped.");
            Release(items);
        }

        /// <summary>
        /// Drops the items of players who left, on the host. A player who takes over learns it hosts only after the old host
        /// left, so this waits for a later frame, once everyone is back; on a client, the host drops them and they arrive as
        /// any other item.
        /// </summary>
        private void DropPending() {
            if (m_pendingDrops.Count == 0) return;
            if (m_sync.IsHost && WKTPlayerDeaths.Sync is not { IsRunOver: true }) {
                foreach (var drop in m_pendingDrops) {
                    for (var i = 0; i < drop.Items.Count; i++)
                        m_sync.SpawnLeftItem(drop.Items[i], drop.Position + WKTDropSpread.Offset(i, drop.Items.Count));
                    WKT.Instance.Logger.LogInfo($"Dropped the {drop.Items.Count} items of player {drop.PlayerId}, who left.");
                }
            }
            foreach (var drop in m_pendingDrops)
                Release(drop.Items);
            m_pendingDrops.Clear();
        }

        /// <summary>Whether the local items, or whether any was used, differ from those last sent. Leaves them walked.</summary>
        private bool HasChanged(Inventory inventory) {
            Walk(inventory);
            if (m_walk.Count != m_sentItems.Count) return true;
            for (var i = 0; i < m_walk.Count; i++) {
                if (!ReferenceEquals(m_walk[i], m_sentItems[i]) || m_walk[i].used != m_sentUsed[i]) return true;
            }
            return false;
        }

        /// <summary>Fills the scratch snapshot with the local items, packed, and remembers them as the ones sent.</summary>
        private void Collect(Inventory inventory) {
            Walk(inventory);
            m_scratch.Clear();
            m_sentItems.Clear();
            m_sentUsed.Clear();
            foreach (var item in m_walk) {
                m_sentItems.Add(item);
                m_sentUsed.Add(item.used);
                var prefabName = item.prefabName;
                if (string.IsNullOrEmpty(prefabName) || prefabName.Length > WKTWire.MAX_ID_LENGTH || m_scratch.Add() is not { } entry) continue;
                WKTItemModules.Pack(item);
                entry.PrefabId = prefabName;
                entry.Used = item.used;
                if (item.data != null)
                    entry.Data.AddRange(item.data);
            }
        }

        /// <summary>Lists what the local player carries the way the game's <c>Inventory.GetItems</c> does, without allocating.</summary>
        private void Walk(Inventory inventory) {
            m_walk.Clear();
            if (inventory.itemHands != null) {
                foreach (var hand in inventory.itemHands)
                    Add(hand?.currentItem);
            }
            if (inventory.bagItems != null) {
                foreach (var item in inventory.bagItems)
                    Add(item);
            }
            var pouches = s_pouches(inventory);
            if (pouches == null) return;
            foreach (var pouch in pouches) {
                if (pouch?.pouchItems == null) continue;
                foreach (var item in pouch.pouchItems) {
                    // As in the game, a pouch item already listed is not listed again.
                    if (!m_walk.Contains(item))
                        Add(item);
                }
            }
        }

        /// <summary>An item the host has yet to give the local player is left out: if the player left now, the host still has it.</summary>
        private void Add(Item? item) {
            if (item != null && !item.hasBeenDestroyed && m_walk.Count < WKTInventorySnapshot.MAX_ITEMS && !m_sync.IsClaimPending(item))
                m_walk.Add(item);
        }

        private void Store(byte playerId, WKTInventorySnapshot snapshot) {
            if (!m_byPlayer.TryGetValue(playerId, out var stored)) {
                stored = m_pool.Count != 0 ? m_pool.Pop() : new WKTInventorySnapshot();
                m_byPlayer[playerId] = stored;
            }
            stored.CopyFrom(snapshot);
        }

        private void Release(WKTInventorySnapshot snapshot) {
            snapshot.Clear();
            m_pool.Push(snapshot);
        }

        private void Broadcast(byte playerId, WKTInventorySnapshot snapshot, WKTPlayer? except) {
            var writer = m_network.BeginMessage(WKTMessageId.PlayerInventory);
            writer.Put(playerId);
            snapshot.Write(writer);
            m_network.Broadcast(writer, SendType.Reliable, except);
        }

        private void SendTo(WKTPlayer player, byte playerId, WKTInventorySnapshot snapshot) {
            var writer = m_network.BeginMessage(WKTMessageId.PlayerInventory);
            writer.Put(playerId);
            snapshot.Write(writer);
            m_network.SendToPlayer(player, writer, SendType.Reliable);
        }

        private readonly struct PendingDrop {
            public readonly byte PlayerId;
            public readonly Vector3 Position;
            public readonly WKTInventorySnapshot Items;

            public PendingDrop(byte playerId, Vector3 position, WKTInventorySnapshot items) {
                PlayerId = playerId;
                Position = position;
                Items = items;
            }
        }
    }
}
