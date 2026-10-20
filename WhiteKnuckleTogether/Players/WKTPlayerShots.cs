using System;
using System.Collections.Generic;
using HarmonyLib;
using Steamworks.Data;
using UnityEngine;
using WhiteKnuckleTogether.Networking.LiteNetLib;
using Object = UnityEngine.Object;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// Shots and throws players fire from hand items in a synced co-op run. The shooter's own shot is the game's: it recoils,
    /// pushes and hits at once, and its hits on host objects go to the host like any other. Everyone else is sent the shot and
    /// shows a cosmetic copy that never hits anything. A rebar left in a wall becomes a shared world item through the local
    /// spawn scope (see <see cref="WKTWorldSync.BeginLocalSpawn"/>), so anyone can climb or take it. Return rebars are shared
    /// by <see cref="WKTReturnRebarSync"/> instead. Owned by <see cref="WKTWorldSync"/>.
    /// </summary>
    internal sealed class WKTPlayerShots {
        private const int c_max_cosmetic = 256;
        private static readonly AccessTools.FieldRef<Projectile, Vector3> s_velocity = AccessTools.FieldRefAccess<Projectile, Vector3>("velocity");
        private static readonly HashSet<Projectile> s_cosmetic = new();
        private static readonly HashSet<string> s_unshown = new();
        private readonly WKTWorldSync m_sync;
        private readonly WKTNetwork m_network;
        /// <summary>Projectile prefabs by item and projectile name, found once.</summary>
        private readonly Dictionary<(string Item, string Projectile), Projectile?> m_prefabs = new();

        public WKTPlayerShots(WKTWorldSync sync, WKTNetwork network) {
            m_sync = sync;
            m_network = network;
        }

        /// <summary>Whether a projectile is another player's shot shown here, which must never hit anything.</summary>
        public static bool IsCosmetic(Projectile projectile) => s_cosmetic.Count != 0 && s_cosmetic.Contains(projectile);

        public static void ClearCosmetic() => s_cosmetic.Clear();

        /// <summary>Makes a projectile only show, never hitting anything, or makes it hit again.</summary>
        public static void SetCosmetic(Projectile projectile, bool cosmetic) {
            if (!cosmetic) {
                s_cosmetic.Remove(projectile);
                return;
            }
            if (s_cosmetic.Count >= c_max_cosmetic)
                s_cosmetic.RemoveWhere(p => p == null);
            s_cosmetic.Add(projectile);
        }

        /// <summary>The local player fired <paramref name="projectile"/> from <paramref name="handItem"/>: everyone else is told.</summary>
        public void OnLocalShot(HandItem handItem, Projectile? projectile) {
            if (projectile == null || handItem.item == null || string.IsNullOrEmpty(handItem.item.prefabName)) return;
            var local = m_network.LocalPlayer;
            if (local == null) return;
            m_sync.PlacedRopes.OnLocalShot(handItem, projectile);
            if (projectile is Projectile_ReturnRebar rebar) {
                m_sync.ReturnRebars.OnLocalThrow(handItem, rebar);
                return;
            }
            if (!WKTPlayerShotTypes.IsShown(projectile.GetType())) {
                if (s_unshown.Add(projectile.GetType().Name))
                    WKT.Instance.Logger.LogInfo($"Shots of type {projectile.GetType().Name} are not shown to other players.");
                return;
            }
            var shotTransform = projectile.transform;
            var velocity = Vector3.ClampMagnitude(s_velocity(projectile), WKTPlayerShot.MAX_SPEED);
            var shot = new WKTPlayerShot(local.Id, handItem.item.prefabName, WKTPrefabSearch.GetPrefabName(projectile.name),
                WKTWorldOffset.ToWire(shotTransform.position, WKTWorldOffset.Get()), shotTransform.rotation, velocity);
            var writer = m_network.BeginMessage(WKTMessageId.PlayerShot);
            shot.Write(writer);
            if (m_sync.IsHost)
                m_network.Broadcast(writer, SendType.Reliable);
            else
                m_network.SendToHost(writer, SendType.Reliable);
        }

        public void OnShot(WKTPlayer sender, NetDataReader reader) {
            if (!WKTPlayerShot.TryRead(reader, out var shot)) return;
            if (m_sync.IsHost) {
                if (sender.IsHost) return;
                // Whoever sent it fired it.
                shot = new WKTPlayerShot(sender.Id, shot.ItemPrefab, shot.Projectile, shot.Position, shot.Rotation, shot.Velocity);
                Show(shot);
                var writer = m_network.BeginMessage(WKTMessageId.PlayerShot);
                shot.Write(writer);
                m_network.Broadcast(writer, SendType.Reliable, sender);
                return;
            }
            if (!sender.IsHost || shot.Shooter == m_network.LocalPlayer?.Id) return;
            Show(shot);
        }

        private void Show(in WKTPlayerShot shot) {
            var prefab = FindPrefab(shot.ItemPrefab, shot.Projectile);
            if (prefab == null || !WKTPlayerShotTypes.IsShown(prefab.GetType())) return;
            var instance = Object.Instantiate(prefab, WKTWorldOffset.FromWire(shot.Position, WKTWorldOffset.Get()), shot.Rotation);
            SetCosmetic(instance, true);
            // Initialize scales its direction by the prefab's speed; the shot carries the velocity itself.
            instance.Initialize(instance.speed > 0f ? shot.Velocity / instance.speed : shot.Velocity);
        }

        /// <summary>The projectile prefab named <paramref name="projectile"/> among <paramref name="itemPrefab"/>'s, searched once.</summary>
        public Projectile? FindPrefab(string itemPrefab, string projectile) {
            var key = (itemPrefab, projectile);
            if (m_prefabs.TryGetValue(key, out var cached)) return cached;
            Projectile? found = null;
            try {
                var item = CL_AssetManager.GetItemObjectPrefab(itemPrefab);
                var handItem = item != null && item.itemData != null ? item.itemData.handItemAsset : null;
                if (handItem != null)
                    found = WKTPrefabSearch.FindProjectile(handItem.gameObject, projectile);
            }
            catch (Exception) {
                // Unknown ids throw inside the asset manager.
            }
            m_prefabs[key] = found;
            if (found == null && s_unshown.Add($"{itemPrefab}/{projectile}"))
                WKT.Instance.Logger.LogWarning($"Cannot show other players' {projectile} shots from {itemPrefab}: no such projectile.");
            return found;
        }
    }
}
