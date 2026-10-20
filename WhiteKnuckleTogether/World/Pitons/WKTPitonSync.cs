using System;
using System.Collections.Generic;
using HarmonyLib;
using Steamworks.Data;
using UnityEngine;
using WhiteKnuckleTogether.Networking.LiteNetLib;
using Object = UnityEngine.Object;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// Placed pitons in a synced co-op run. Every player sees and can climb every piton. A hammer hit on one plays out for
    /// everyone, sound, effects and depth alike. A piton loosens only on the machine of a player holding it, as in the game;
    /// when it falls out there, the item it leaves goes to the host like a drop (see the local spawn scope) and the piton
    /// is removed everywhere. Taking a piton back out of the wall removes it everywhere too.
    /// <para>
    /// Every machine keeps the same registry, so the host can replay it to late joiners and a client that takes over as host
    /// already has it. Owned by <see cref="WKTWorldSync"/>.
    /// </para>
    /// </summary>
    internal sealed class WKTPitonSync {
        /// <summary>How far behind a piton the wall it sits in is looked for, so it follows moving walls like the original.</summary>
        private const float c_wall_search = 1f;
        private static readonly Action<M_Level, GameObject> s_addPlacedObject = AccessTools.MethodDelegate<Action<M_Level, GameObject>>(
            AccessTools.Method(typeof(M_Level), "AddPlacedObject"));
        private readonly WKTWorldSync m_sync;
        private readonly WKTNetwork m_network;
        private readonly WKTPitonRegistry m_registry = new();
        private readonly Dictionary<uint, WKTPlacedPiton> m_pitons = new();
        private readonly Dictionary<string, GameObject?> m_prefabs = new();
        /// <summary>Pitons in levels this machine has not built yet, by level key.</summary>
        private readonly Dictionary<ulong, List<WKTPitonPlaced>> m_pending = new();
        private uint m_counter;

        /// <summary>Set while a piton change from another machine is applied here, so it is not sent back.</summary>
        public static bool IsApplying { get; private set; }

        public WKTPitonSync(WKTWorldSync sync, WKTNetwork network) {
            m_sync = sync;
            m_network = network;
        }

        #region Local

        /// <summary>The local player placed <paramref name="piton"/> with <paramref name="hand"/>: everyone gets it.</summary>
        public void OnLocalPlaced(HandItem_Piton hand, GameObject piton) {
            var local = m_network.LocalPlayer;
            var level = WorldLoader.initialized && WorldLoader.instance != null ? WorldLoader.instance.GetCurrentLevel()?.GetLevel() : null;
            if (local == null || level == null || hand.item == null || string.IsNullOrEmpty(hand.item.prefabName) ||
                !WKTContentSeeding.TryGetLevelKey(level, out var levelKey)) return;
            var pitonTransform = piton.transform;
            var placed = new WKTPitonPlaced(WKTPitonRegistry.MakeId(local.Id, ++m_counter), hand.item.prefabName, levelKey,
                WKTWorldOffset.ToWire(pitonTransform.position, WKTWorldOffset.Get()), pitonTransform.rotation, 0f);
            if (!m_registry.Add(placed)) return;
            Track(placed.Id, piton);
            var writer = m_network.BeginMessage(WKTMessageId.PitonPlaced);
            placed.Write(writer);
            Send(writer);
        }

        /// <summary>A piton was hammered here: everyone sees and hears the same hit.</summary>
        public void OnLocalHammer(CL_Handhold handhold, float amount) {
            if (IsApplying || !handhold.TryGetComponent<WKTPlacedPiton>(out var marker) || amount <= 0f) return;
            amount = Mathf.Min(amount, WKTPitonChange.MAX_AMOUNT);
            m_registry.Hammer(marker.Id, amount);
            var writer = m_network.BeginMessage(WKTMessageId.PitonChange);
            new WKTPitonChange(marker.Id, WKTPitonChange.HAMMERED, amount).Write(writer);
            Send(writer);
        }

        /// <summary>
        /// A shared piton is being destroyed. It is gone for everyone if it fell out of the wall while held here or was taken out
        /// here; a level unloading on this machine alone only forgets it.
        /// </summary>
        public void OnPitonDestroyed(WKTPlacedPiton marker) {
            if (!m_pitons.TryGetValue(marker.Id, out var tracked) || tracked != marker) return;
            m_pitons.Remove(marker.Id);
            if (marker.RemovedRemotely) return;
            var handhold = marker.Handhold;
            var fellOut = handhold != null && !handhold.secure && handhold.secureAmount < 0f;
            if (!fellOut && !marker.Taken) return;
            m_registry.Remove(marker.Id);
            var writer = m_network.BeginMessage(WKTMessageId.PitonChange);
            new WKTPitonChange(marker.Id, WKTPitonChange.REMOVED).Write(writer);
            Send(writer);
        }

        private void Send(NetDataWriter writer) {
            if (m_sync.IsHost)
                m_network.Broadcast(writer, SendType.Reliable);
            else
                m_network.SendToHost(writer, SendType.Reliable);
        }

        #endregion

        #region Remote

        public void OnPlaced(WKTPlayer sender, NetDataReader reader) {
            if (!WKTPitonPlaced.TryRead(reader, out var placed)) return;
            if (m_sync.IsHost) {
                // A client only names its own pitons.
                if (sender.IsHost || WKTPitonRegistry.GetPlayer(placed.Id) != sender.Id || !m_registry.Add(placed)) return;
                Build(placed);
                var writer = m_network.BeginMessage(WKTMessageId.PitonPlaced);
                placed.Write(writer);
                m_network.Broadcast(writer, SendType.Reliable, sender);
                return;
            }
            if (!sender.IsHost) return;
            // A replay after a reconnect can carry the pitons this machine already built.
            if (m_registry.Add(placed) || !m_pitons.ContainsKey(placed.Id))
                Build(placed);
        }

        public void OnChange(WKTPlayer sender, NetDataReader reader) {
            if (!WKTPitonChange.TryRead(reader, out var change)) return;
            if (m_sync.IsHost) {
                if (sender.IsHost || !Apply(change)) return;
                var writer = m_network.BeginMessage(WKTMessageId.PitonChange);
                change.Write(writer);
                m_network.Broadcast(writer, SendType.Reliable, sender);
                return;
            }
            if (sender.IsHost)
                Apply(change);
        }

        /// <summary>Applies a change from another machine. False when it no longer applies.</summary>
        private bool Apply(in WKTPitonChange change) {
            switch (change.Change) {
                case WKTPitonChange.HAMMERED:
                    if (!m_registry.Hammer(change.Id, change.Amount)) return false;
                    if (m_pitons.TryGetValue(change.Id, out var hammered) && hammered != null && hammered.Handhold != null)
                        Hammer(hammered.Handhold, change.Amount);
                    return true;
                case WKTPitonChange.REMOVED:
                    if (!m_registry.Remove(change.Id)) return false;
                    if (m_pitons.TryGetValue(change.Id, out var removed) && removed != null) {
                        removed.RemovedRemotely = true;
                        removed.Handhold?.ForceDropHands();
                        Object.Destroy(removed.gameObject);
                    }
                    return true;
                default:
                    return false;
            }
        }

        private static void Hammer(CL_Handhold handhold, float amount) {
            IsApplying = true;
            try {
                handhold.HammerIn(amount);
            }
            finally {
                IsApplying = false;
            }
        }

        /// <summary>Builds another player's piton from the item's own piton prefab, as the game places one.</summary>
        private void Build(in WKTPitonPlaced placed) {
            if (m_pitons.ContainsKey(placed.Id) || m_registry.IsRemoved(placed.Id)) return;
            if (!WKTContentSeeding.TryGetLevel(placed.LevelKey, out var level)) {
                if (!m_pending.TryGetValue(placed.LevelKey, out var waiting)) {
                    waiting = new List<WKTPitonPlaced>();
                    m_pending.Add(placed.LevelKey, waiting);
                }
                waiting.Add(placed);
                return;
            }
            var prefab = FindPrefab(placed.ItemPrefab);
            if (prefab == null) return;
            var position = WKTWorldOffset.FromWire(placed.Position, WKTWorldOffset.Get());
            var piton = Object.Instantiate(prefab, position, placed.Rotation, level!.transform);
            // The game soft-parents a piton to what it was driven into; the wall is behind it.
            if (Physics.Raycast(position, placed.Rotation * Vector3.back, out var wall, c_wall_search, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                piton.AddComponent<UT_SoftParent>().Initialize(wall.collider.transform);
            s_addPlacedObject(level, piton);
            var marker = Track(placed.Id, piton);
            if (placed.Hammered > 0f && marker.Handhold != null)
                Hammer(marker.Handhold, placed.Hammered);
        }

        private WKTPlacedPiton Track(uint id, GameObject piton) {
            var marker = piton.AddComponent<WKTPlacedPiton>();
            marker.Id = id;
            marker.Handhold = piton.GetComponent<CL_Handhold>();
            m_pitons[id] = marker;
            m_sync.Handholds.RegisterRoot(piton, WKTHandholdKeys.GetRootKey(WKTHandholdKeys.PitonSalt, id));
            return marker;
        }

        private GameObject? FindPrefab(string itemPrefab) {
            if (m_prefabs.TryGetValue(itemPrefab, out var cached)) return cached;
            GameObject? found = null;
            try {
                var item = CL_AssetManager.GetItemObjectPrefab(itemPrefab);
                if (item != null && item.itemData != null && item.itemData.handItemAsset is HandItem_Piton hand)
                    found = hand.pitonWorldObject;
            }
            catch (Exception) {
                // Unknown ids throw inside the asset manager.
            }
            m_prefabs[itemPrefab] = found;
            if (found == null)
                WKT.Instance.Logger.LogWarning($"Cannot show other players' pitons placed with {itemPrefab}: no piton prefab.");
            return found;
        }

        #endregion

        /// <summary>A level was built here: the pitons placed in it before are built now.</summary>
        public void OnLevelKeyed(ulong levelKey) {
            if (!m_pending.Remove(levelKey, out var waiting)) return;
            foreach (var placed in waiting)
                Build(placed);
        }

        /// <summary>
        /// A level unloaded here. The host forgets its pitons, since the host keeps every level any player is in. A client keeps
        /// them in its registry: the host still has them.
        /// </summary>
        public void OnLevelForgetting(ulong levelKey) {
            m_pending.Remove(levelKey);
            if (m_sync.IsHost)
                m_registry.ForgetLevel(levelKey);
        }

        /// <summary>Host only: tells a player who (re)connected about every piton.</summary>
        public void SendAll(WKTPlayer player) {
            foreach (var placed in m_registry.Pitons) {
                var writer = m_network.BeginMessage(WKTMessageId.PitonPlaced);
                placed.Write(writer);
                m_network.SendToPlayer(player, writer, SendType.Reliable);
            }
        }
    }
}
