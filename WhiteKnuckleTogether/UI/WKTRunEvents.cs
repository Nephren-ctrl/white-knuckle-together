using System.Collections.Generic;
using UnityEngine;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// What the local player does that everyone hears of, in everyone's chat: choosing a perk at a perk machine
    /// (<see cref="WKTChat.PostEvent"/>), and finding an item no player has held before (<see cref="WKTChat.AnnounceFind"/>), its
    /// name in the color of its outline. Common items (a white outline) are not told, and neither are perks from anything but
    /// a perk machine, such as a starting trinket's.
    /// <para>
    /// An item is found once: the first pickup marks the item's own data (<see cref="FOUND_DATA"/>), which travels with it
    /// whenever it changes hands or is dropped, so whoever picks it up after that announces nothing.
    /// </para>
    /// </summary>
    internal static class WKTRunEvents {
        /// <summary>
        /// The <c>Item.data</c> entry ("type:value") on an item someone has held. The game reads an entry by searching for
        /// "type:" in it; no type the game reads is a suffix of this one.
        /// </summary>
        public const string FOUND_DATA = "wktfound:1";
        private static readonly List<Renderer> s_renderers = new();
        private static readonly WKTPerkChoice s_perkChoice = new();

        /// <summary>
        /// While the run hands out what players start with (trinkets, loadouts): those items are theirs from the start, so
        /// they are marked held but never told.
        /// </summary>
        public static bool IsGrantingStartItems { get; set; }

        /// <summary>The local player took an item into a hand: if nobody held it before, everyone hears of it.</summary>
        public static void OnPickup(Item item) {
            if (!MarkFound(item) || IsGrantingStartItems || WKTMultiplayerHud.Instance == null || !WKTRunController.HasLocalSpawned) return;
            var color = OutlineColor(item);
            if (IsCommon(color)) return;
            var name = string.IsNullOrEmpty(item.itemName) ? item.prefabName : item.itemName;
            if (!string.IsNullOrEmpty(name))
                WKTChat.AnnounceFind(name, color);
        }

        /// <summary>Marks everything the local player carries as held: what they start with is never a find.</summary>
        public static void MarkStartingItems() {
            var inventory = Inventory.instance;
            if (inventory == null) return;
            foreach (var item in inventory.GetItems()) {
                if (item != null)
                    MarkFound(item);
            }
        }

        /// <summary>Marks an item as held. False if it already was, by anyone.</summary>
        public static bool MarkFound(Item item) {
            item.data ??= new List<string>();
            if (item.data.Contains(FOUND_DATA)) return false;
            item.data.Add(FOUND_DATA);
            return true;
        }

        /// <summary>Whether an outline color is the plain white of common items.</summary>
        public static bool IsCommon(Color color) => color.r > 0.85f && color.g > 0.85f && color.b > 0.85f;

        /// <summary>"a" or "an" for a name.</summary>
        public static string Article(string name) => name.Length > 0 && "AEIOUaeiou".IndexOf(name[0]) >= 0 ? "an" : "a";

        /// <summary>The local player is buying <paramref name="perk"/> at a perk machine.</summary>
        public static void OnPerkBought(Perk? perk) => s_perkChoice.Record(perk != null ? perk.id : null, Time.unscaledTime);

        /// <summary>The local player got a perk: told if it is the one just bought at a perk machine.</summary>
        public static void OnPerk(Perk perk) {
            if (WKTMultiplayerHud.Instance == null || !s_perkChoice.TryTake(perk.id, Time.unscaledTime)) return;
            var title = perk.GetTitle(true);
            if (string.IsNullOrEmpty(title))
                title = WKTChatText.Escape(perk.title ?? perk.id ?? "unknown");
            WKTChat.PostEvent($"{WKTChat.LocalName()} chose the {title} perk!");
        }

        /// <summary>The color of an item's outline, from its world model's outline renderer; white when it has none.</summary>
        private static Color OutlineColor(Item item) {
            var dropObject = item.GetDropObject(false);
            if (dropObject == null) return Color.white;
            dropObject.GetComponentsInChildren(true, s_renderers);
            try {
                foreach (var renderer in s_renderers) {
                    if (WKTItemOutline.TryGetColor(renderer, out var color))
                        return color;
                }
            }
            finally {
                s_renderers.Clear();
            }
            WKT.Instance.Logger.LogInfo($"Item {item.prefabName} has no outline renderer; told as common.");
            return Color.white;
        }
    }
}
