using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// A developer view of the local player's own body as others see it, standing <see cref="DISTANCE"/> in front of them and
    /// facing them, mirroring their look, crouch and held items. Made for checking how held items sit in the hand without a
    /// second player. Driven by the console command <c>wkt_avatar</c> (not a cheat):
    /// <list type="bullet">
    /// <item><c>wkt_avatar</c>: show or hide it.</item>
    /// <item><c>wkt_avatar hold &lt;prefab&gt;</c>: puts an item in its right hand; without a name, back to what the player holds.</item>
    /// <item><c>wkt_avatar next</c> / <c>prev</c>: steps through every item in the game.</item>
    /// <item><c>wkt_avatar dump</c>: logs every item model's shape, for tuning <see cref="WKTHeldItemPose"/>.</item>
    /// <item><c>wkt_avatar tree &lt;prefab&gt;</c>: logs an item model's renderers.</item>
    /// <item><c>wkt_avatar turn</c>: turns it a quarter, to see items from the side.</item>
    /// </list>
    /// Owned by <see cref="WKTPlayerSync"/>, so it only exists in a multiplayer run.
    /// </summary>
    internal sealed class WKTAvatarPreview {
        public const string COMMAND = "wkt_avatar";
        public const float DISTANCE = 2.5f;
        private static readonly AccessTools.FieldRef<List<CL_AssetManager.WKDatabaseHolder>> s_databases =
            AccessTools.StaticFieldRefAccess<List<CL_AssetManager.WKDatabaseHolder>>(AccessTools.Field(typeof(CL_AssetManager), "databases"));
        private readonly List<string> m_itemNames = new();
        private WKTRemotePlayer? m_body;
        private string? m_forcedItem;
        private int m_itemIndex = -1;
        /// <summary>Quarter turns away from facing the player.</summary>
        private int m_turns;
        private string? m_shownLeft;
        private string? m_shownRight;

        public void Register() => CommandConsole.AddCommand(COMMAND, OnCommand, false);

        public void Dispose() {
            CommandConsole.RemoveCommand(COMMAND);
            Hide();
        }

        /// <summary>Call every frame after the local pose is built, with the held items' prefab names and the body others are sent.</summary>
        public void Update(in WKTPlayerPose pose, float worldOffset, string? left, string? right, in WKTPlayerBodyState body) {
            if (m_body == null) return;
            m_body.SetBody(body);
            var yaw = pose.Yaw + 180f + m_turns * 90f;
            var forward = Quaternion.Euler(0f, pose.Yaw, 0f) * Vector3.forward;
            var heldRight = m_forcedItem ?? right;
            // Hands shown held out, as when holding an item; never hanging from a hold that is somewhere else.
            var leftHand = HeldOut(pose.Left, left);
            var rightHand = HeldOut(pose.Right, heldRight);
            var mirrored = new WKTPlayerPose(pose.Position + forward * DISTANCE, yaw, pose.Pitch, pose.Crouch, leftHand, rightHand);
            m_body.Apply(mirrored, worldOffset, false);
            if (left == m_shownLeft && heldRight == m_shownRight) return;
            m_shownLeft = left;
            m_shownRight = heldRight;
            m_body.SetHeldItems(new WKTHeldItemsState(0, left, heldRight));
        }

        private static WKTHandPose HeldOut(in WKTHandPose hand, string? item) =>
            string.IsNullOrEmpty(item) ? WKTHandPose.Rested : new WKTHandPose(false, Vector3.zero, hand.Stamina, false, true);

        private void OnCommand(string[] args) {
            var verb = args.Length > 0 ? args[0].ToLowerInvariant() : string.Empty;
            switch (verb) {
                case "":
                    if (m_body != null)
                        Hide();
                    else
                        Show();
                    break;
                case "hold":
                    m_forcedItem = args.Length > 1 ? args[1] : null;
                    Show();
                    CommandConsole.Log(m_forcedItem != null ? $"Preview holds {m_forcedItem}." : "Preview holds what you hold.");
                    break;
                case "next":
                case "prev":
                    Step(verb == "next" ? 1 : -1);
                    break;
                case "dump":
                    Dump();
                    break;
                case "turn":
                    m_turns = (m_turns + 1) % 4;
                    break;
                case "tree" when args.Length > 1:
                    Tree(args[1]);
                    break;
                default:
                    CommandConsole.Log($"{COMMAND} [hold <prefab> | next | prev | turn | dump | tree <prefab>]");
                    break;
            }
        }

        private void Show() {
            if (m_body != null) return;
            var network = WKT.Instance.Network;
            var local = network.LocalPlayer;
            var player = ENT_Player.playerObject;
            if (local == null || player == null) return;
            m_body = WKTRemotePlayer.Create(local, player, null);
            m_body.name = "WKT Avatar Preview";
            m_body.SetVisible(true);
            m_shownLeft = m_shownRight = null;
            m_body.SetHeldItems(new WKTHeldItemsState(0, null, null));
        }

        private void Hide() {
            if (m_body == null) return;
            Object.Destroy(m_body.gameObject);
            m_body = null;
        }

        private void Step(int direction) {
            LoadItemNames();
            if (m_itemNames.Count == 0) return;
            m_itemIndex = (m_itemIndex + direction + m_itemNames.Count) % m_itemNames.Count;
            m_forcedItem = m_itemNames[m_itemIndex];
            Show();
            CommandConsole.Log($"Preview holds {m_forcedItem} ({m_itemIndex + 1}/{m_itemNames.Count}).");
            WKT.Instance.Logger.LogInfo($"Avatar preview holds {m_forcedItem}.");
        }

        private void LoadItemNames() {
            if (m_itemNames.Count != 0) return;
            var databases = s_databases();
            if (databases == null) return;
            foreach (var holder in databases) {
                if (holder?.database?.itemPrefabs == null) continue;
                foreach (var prefab in holder.database.itemPrefabs) {
                    if (prefab == null || !prefab.TryGetComponent<Item_Object>(out var item) || item.itemData == null) continue;
                    var name = item.itemData.prefabName;
                    if (!string.IsNullOrEmpty(name) && !m_itemNames.Contains(name))
                        m_itemNames.Add(name);
                }
            }
        }

        /// <summary>Logs every renderer of an item's model: where it is, whether it shows, and its extent in the model's space.</summary>
        private static void Tree(string prefabName) {
            var prefab = CL_AssetManager.GetItemObjectPrefab(prefabName);
            if (prefab == null) {
                CommandConsole.Log($"No item {prefabName}.");
                return;
            }
            var root = prefab.transform;
            var logger = WKT.Instance.Logger;
            logger.LogInfo($"Item tree {prefabName}: root scale {root.localScale:F3}");
            foreach (var renderer in prefab.GetComponentsInChildren<Renderer>(true)) {
                var path = renderer.transform == root ? "." : PathOf(root, renderer.transform);
                var bounds = renderer.bounds;
                logger.LogInfo($"  {path} [{renderer.GetType().Name}] enabled {renderer.enabled} active {renderer.gameObject.activeSelf} " +
                    $"local {renderer.transform.localPosition:F3} scale {renderer.transform.localScale:F3} world bounds {bounds.center:F3} size {bounds.size:F3}");
            }
            CommandConsole.Log($"Logged {prefabName}'s renderers.");
        }

        private static string PathOf(Transform root, Transform child) {
            var path = child.name;
            for (var parent = child.parent; parent != null && parent != root; parent = parent.parent)
                path = parent.name + "/" + path;
            return path;
        }

        /// <summary>Logs each item model's shape in its own space, as <see cref="WKTHeldItemPose"/> reads it.</summary>
        private void Dump() {
            LoadItemNames();
            var sb = new StringBuilder();
            foreach (var name in m_itemNames) {
                var prefab = CL_AssetManager.GetItemObjectPrefab(name);
                if (prefab == null) continue;
                sb.Clear();
                var handItem = prefab.itemData != null && prefab.itemData.handItemAsset != null ? prefab.itemData.handItemAsset.GetType().Name : "none";
                sb.Append($"Item shape {name} ({handItem}): ");
                if (WKTHeldItemPose.TryMeasure(prefab.gameObject, out var shape))
                    sb.Append($"bounds {shape.min:F3}..{shape.max:F3}");
                else
                    sb.Append("no renderers");
                WKT.Instance.Logger.LogInfo(sb.ToString());
            }
            CommandConsole.Log($"Logged {m_itemNames.Count} item shapes.");
        }
    }
}
