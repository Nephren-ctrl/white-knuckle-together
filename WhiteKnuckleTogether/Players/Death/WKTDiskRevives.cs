using System;
using System.Linq;
using JetBrains.Annotations;
using UnityEngine;
using WhiteKnuckleTogether.Networking.LiteNetLib;
using SendType = Steamworks.Data.SendType;

namespace WhiteKnuckleTogether {
    /// <summary>Save disks by tier, from their prefab names (<c>Item_Floppy_T1</c>, <c>Item_Floppy_T2_01</c>, ...). Pure, so it can be tested without Unity.</summary>
    internal static class WKTDiskTiers {
        private const string c_prefix = "Item_Floppy_T";

        /// <summary>The disk's tier, 1 to 9, or 0 for anything that is not a tiered save disk.</summary>
        public static int TierOf(string? prefabName) {
            if (prefabName == null || prefabName.Length <= c_prefix.Length || !prefabName.StartsWith(c_prefix, StringComparison.Ordinal)) return 0;
            var digit = prefabName[c_prefix.Length];
            if (digit is < '1' or > '9') return 0;
            if (prefabName.Length > c_prefix.Length + 1 && prefabName[c_prefix.Length + 1] != '_') return 0;
            return digit - '0';
        }

        /// <summary>The plain disk one tier below, or null for a tier 1 disk, which is used up.</summary>
        public static string? LowerOf(int tier) => tier > 1 ? c_prefix + (tier - 1) : null;
    }

    /// <summary>
    /// Save disks revive the dead in a multiplayer run. Slotting a tiered disk into a computer's floppy reader brings every dead
    /// player back next to whoever slotted it, instead of the game's disk program. The disk goes down a tier: a tier 1 disk is
    /// used up, a higher one comes back to the slotter's bag one tier lower. Revived players come back with full health and
    /// grip, without their buffs, keeping their perks and the items the game's own disk revive keeps (see <see cref="KeepOnlyKeptItems"/>). The host
    /// picks free spots for them.
    /// </summary>
    internal sealed class WKTDiskRevives : MonoBehaviour {
        /// <summary>Items with this tag survive a disk revive (the game's artifacts among them).</summary>
        private const string c_save_with_disk_tag = "savewithdisk";
        private const string c_hammer_tag = "hammer";
        /// <summary>The perk that keeps a player's whole bag through a disk revive.</summary>
        private const string c_keep_inventory_perk = "Perk_AnomalousBonds";
        private const float c_spot_radius = 1.6f;
        private const int c_spot_candidates = 12;
        private const float c_capsule_radius = 0.35f;
        private const float c_capsule_height = 1.6f;
        private WKTNetwork m_network = null!;
        private int m_worldMask;

        public static WKTDiskRevives? Instance { get; private set; }

        [UsedImplicitly]
        private void Awake() {
            m_network = WKT.Instance.Network;
            Instance = this;
            m_worldMask = LayerMask.GetMask("Default", "World", "Platform");
            m_network.Messages.Register(WKTMessageId.ReviveRequest, OnReviveRequest);
            m_network.Messages.Register(WKTMessageId.Revive, OnRevive);
        }

        [UsedImplicitly]
        private void OnDestroy() {
            if (Instance == this)
                Instance = null;
            if (WKT.InstanceOrNull == null) return;
            m_network.Messages.Unregister(WKTMessageId.ReviveRequest);
            m_network.Messages.Unregister(WKTMessageId.Revive);
        }

        /// <summary>
        /// A disk is being slotted into a floppy reader here. False skips the game's own disk handling: a tiered disk revives
        /// the dead instead. With nobody to revive, the disk stays where the game put it, by the reader.
        /// </summary>
        public bool BeforeInsert(OS_FloppyReader reader, ENT_Player player, Item floppy) {
            var tier = WKTDiskTiers.TierOf(floppy.prefabName);
            if (tier == 0) return true;
            if (!AnyoneDead()) {
                WKTToast.Show("Nobody to revive.");
                return false;
            }
            WKTWorldSync.Instance?.CancelLocalDrop(floppy);
            floppy.Destroy();
            if (WKTDiskTiers.LowerOf(tier) is { } lower)
                GiveDisk(lower);
            if (reader.insertFinish != null)
                AudioManager.PlaySound(reader.insertFinish, reader.transform.position, reader.audioVolume);
            WKTChat.PostEvent($"{WKTChat.LocalName()} revived the dead with a {WKTChatText.Colored($"tier {tier} disk", Color.cyan)}!");
            var position = WKTWorldOffset.ToWire(player.transform.position, WKTWorldOffset.Get());
            if (m_network.Mode == NetworkMode.Host) {
                ReviveAround(position);
                return false;
            }
            var writer = m_network.BeginMessage(WKTMessageId.ReviveRequest);
            WKTWire.Put(writer, position);
            m_network.SendToHost(writer, SendType.Reliable);
            return false;
        }

        private static bool AnyoneDead() {
            var sync = WKTPlayerDeaths.Sync;
            if (sync == null) return false;
            for (var id = 0; id <= byte.MaxValue; id++) {
                if (sync.Roster.IsDead((byte)id) && WKT.Instance.Network.TryGetPlayer((byte)id, out _)) return true;
            }
            return false;
        }

        private static void GiveDisk(string prefabName) {
            Item_Object? prefab;
            try {
                prefab = CL_AssetManager.GetItemObjectPrefab(prefabName);
            }
            catch (Exception) {
                prefab = null;
            }
            if (prefab == null || prefab.itemData == null || Inventory.instance == null) {
                WKT.Instance.Logger.LogWarning($"Could not give back a {prefabName}.");
                return;
            }
            Inventory.instance.AddItemToInventoryCenter(prefab.itemData.GetClone());
        }

        private void OnReviveRequest(WKTPlayer sender, NetDataReader reader) {
            if (m_network.Mode != NetworkMode.Host || sender.IsHost || !WKTWire.TryGetPosition(reader, out var position)) return;
            ReviveAround(position);
        }

        /// <summary>Host only: brings every dead player back at a free spot around <paramref name="wirePosition"/>.</summary>
        private void ReviveAround(Vector3 wirePosition) {
            var sync = WKTPlayerDeaths.Sync;
            if (sync == null) return;
            var center = WKTWorldOffset.FromWire(wirePosition, WKTWorldOffset.Get());
            var spot = 0;
            for (var id = 0; id <= byte.MaxValue; id++) {
                if (!sync.Roster.IsDead((byte)id) || !m_network.TryGetPlayer((byte)id, out var player) || player == null) continue;
                var position = FindSpot(center, ref spot);
                var yaw = Quaternion.LookRotation(Vector3.ProjectOnPlane(center - position, Vector3.up).normalized + Vector3.forward * 0.001f).eulerAngles.y;
                var writer = m_network.BeginMessage(WKTMessageId.Revive);
                writer.Put((byte)id);
                WKTWire.Put(writer, WKTWorldOffset.ToWire(position, WKTWorldOffset.Get()));
                writer.Put(yaw);
                m_network.Broadcast(writer, SendType.Reliable);
                if (player == m_network.LocalPlayer)
                    ReviveLocal(position, yaw);
            }
        }

        /// <summary>The next free standing spot around <paramref name="center"/>, on a ring, falling back to the center itself.</summary>
        private Vector3 FindSpot(Vector3 center, ref int next) {
            for (; next < c_spot_candidates; next++) {
                var angle = next * (360f / c_spot_candidates) * Mathf.Deg2Rad;
                var candidate = center + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * c_spot_radius;
                var bottom = candidate + Vector3.up * (c_capsule_radius + 0.05f);
                var top = candidate + Vector3.up * (c_capsule_height - c_capsule_radius);
                if (Physics.CheckCapsule(bottom, top, c_capsule_radius, m_worldMask, QueryTriggerInteraction.Ignore)) continue;
                if (Physics.Linecast(center + Vector3.up, candidate + Vector3.up, m_worldMask, QueryTriggerInteraction.Ignore)) continue;
                next++;
                return candidate;
            }
            return center + Vector3.up * 0.1f;
        }

        private void OnRevive(WKTPlayer sender, NetDataReader reader) {
            if (!sender.IsHost || m_network.Mode == NetworkMode.Host || !reader.TryGetByte(out var id) || !WKTWire.TryGetPosition(reader, out var position) ||
                !reader.TryGetFloat(out var yaw) || float.IsNaN(yaw)) return;
            if (m_network.LocalPlayer is { } local && local.Id == id)
                ReviveLocal(WKTWorldOffset.FromWire(position, WKTWorldOffset.Get()), yaw);
        }

        /// <summary>Brings the local player back where the host put them.</summary>
        private static void ReviveLocal(Vector3 position, float yaw) {
            var player = ENT_Player.playerObject;
            var sync = WKTPlayerDeaths.Sync;
            if (player == null || sync == null || !sync.IsLocalDead) return;
            KeepOnlyKeptItems(player);
            player.Revive();
            player.RemoveAllBuffs();
            player.SetGripStrength(player.gripStrengthTimer);
            player.Teleport(position, Quaternion.Euler(0f, yaw, 0f));
            sync.SetLocalDead(false);
            WKT.Instance.Logger.LogInfo("Revived by a disk.");
        }

        /// <summary>
        /// What the player keeps, exactly as the game's own disk revive decides (<c>CL_SaveManager.SaveState.LoadSave</c> for a
        /// disk save): bag and pouch items tagged "savewithdisk" (everything with the Anomalous Bonds perk, lost disks turned
        /// into lost floppies), a hammer if they have none, and nothing in their hands.
        /// </summary>
        private static void KeepOnlyKeptItems(ENT_Player player) {
            const CL_SaveManager.SaveState.SaveType disk = CL_SaveManager.SaveState.SaveType.disk;
            var data = new ENT_Player.SaveData();
            data.SaveDataIntoClass(player);
            data.pouchInventory ??= Array.Empty<Inventory.Pouch.PouchSaveData>();
            data.pocketInventory ??= Array.Empty<Inventory.Pouch.PouchSaveData>();
            foreach (var pouch in data.pouchInventory)
                pouch.items = data.ProcessWipeInventory(player, pouch.items.ToArray(), disk, false).ToList();
            foreach (var pocket in data.pocketInventory)
                pocket.items = data.ProcessWipeInventory(player, pocket.items.ToArray(), disk, false).ToList();
            var hasHammer = false;
            foreach (var pouch in data.pouchInventory.Concat(data.pocketInventory)) {
                foreach (var item in pouch.items) {
                    if (item.HasTag(c_hammer_tag))
                        hasHammer = true;
                }
            }
            data.inventory = data.ProcessWipeInventory(player, data.inventory, disk, !hasHammer);
            data.handInventory = new Item[2];
            if (!data.HasPerk(c_keep_inventory_perk)) {
                foreach (var pouch in data.pouchInventory.Concat(data.pocketInventory))
                    pouch.items.RemoveAll(item => !item.HasTag(c_save_with_disk_tag));
            }
            data.LoadDataIntoPlayer(player);
        }
    }
}
