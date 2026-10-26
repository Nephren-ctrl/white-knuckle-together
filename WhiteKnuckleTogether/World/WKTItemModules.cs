using System;
using System.Collections.Generic;
using System.Globalization;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// An item's state mostly lives in <c>Item.data</c> ("type:value" entries such as ammo, charge, uses or a stopwatch's
    /// timer), which travels whole whenever an item is dropped, stolen or traded. The rest lives in its data modules, which
    /// the game rolls on first use: the text a note shows (<c>Item_Data_Text_Random</c>) and its sprite in hand
    /// (<c>Item_Data_HandSprite</c>). Those are packed into the data as extra entries before it is sent and unpacked where it
    /// arrives, so the same note reads the same for everyone. So is a Bazaar item not yet paid for (<c>ENV_RoachTrader</c>):
    /// its "unpurchased" tag, its price, and the tags that keep it on its stand.
    /// </summary>
    internal static class WKTItemModules {
        /// <summary>An entry is "wktmod&lt;module index&gt;:&lt;kind&gt;&lt;value&gt;". No type the game reads is a suffix of it.</summary>
        public const string PREFIX = "wktmod";
        public const char KIND_TEXT = 't';
        public const char KIND_SPRITE = 's';
        /// <summary>A shop item's entry, "wktmodshop:&lt;worth&gt;". It parses as no module.</summary>
        public const string SHOP = PREFIX + "shop:";
        public const string UNPURCHASED = "unpurchased";
        private const string c_locked_tag = "ItemLocked";
        private const string c_vended_tag = "Vended";

        /// <summary>Writes the item's module and shop state into its data, replacing what was packed before.</summary>
        public static void Pack(Item? item) {
            if (item == null) return;
            if (item.data != null)
                RemovePacked(item.data);
            if (item.HasTag(UNPURCHASED)) {
                item.data ??= new List<string>();
                item.data.Add(SHOP + item.worth.ToString(CultureInfo.InvariantCulture));
            }
            if (item.dataModules == null) return;
            for (var i = 0; i < item.dataModules.Count; i++) {
                switch (item.dataModules[i]) {
                    case Item_Data_Text_Random text when !string.IsNullOrEmpty(text.selectedString) && text.strings != null:
                        var index = text.strings.IndexOf(text.selectedString);
                        if (index >= 0)
                            (item.data ??= new List<string>()).Add(Format(i, KIND_TEXT, index.ToString(CultureInfo.InvariantCulture)));
                        break;
                    case Item_Data_HandSprite sprite when sprite.handSprite != null:
                        (item.data ??= new List<string>()).Add(Format(i, KIND_SPRITE, sprite.handSprite.name));
                        break;
                }
            }
        }

        /// <summary>
        /// Applies module and shop state packed into the item's data. Its modules must be its own, not a prefab's. The shop's
        /// stand tags go on <paramref name="worldObject"/>, or the item's own world object.
        /// </summary>
        public static void Unpack(Item? item, Item_Object? worldObject = null) {
            if (item == null) return;
            var forSale = false;
            if (item.data != null) {
                foreach (var entry in item.data) {
                    if (TryParseShop(entry, out var worth)) {
                        forSale = true;
                        item.worth = worth;
                        continue;
                    }
                    if (item.dataModules == null || !TryParse(entry, out var module, out var kind, out var value) || module >= item.dataModules.Count) continue;
                    switch (item.dataModules[module]) {
                        case Item_Data_Text_Random text when kind == KIND_TEXT && text.strings != null:
                            if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var index) && index < text.strings.Count)
                                text.selectedString = text.strings[index];
                            break;
                        case Item_Data_HandSprite sprite when kind == KIND_SPRITE:
                            var asset = CL_AssetManager.GetSpriteAsset(value);
                            if (asset != null)
                                sprite.handSprite = asset;
                            break;
                    }
                }
            }
            SetForSale(item, forSale, worldObject != null ? worldObject : item.GetDropObject(false));
        }

        /// <summary>A shop item stays for sale until someone pays for it; any other item is left as it is.</summary>
        private static void SetForSale(Item item, bool forSale, Item_Object? worldObject) {
            if (forSale == item.HasTag(UNPURCHASED)) return;
            item.itemTags ??= new List<string>();
            var tagger = worldObject != null ? worldObject.GetComponent<ObjectTagger>() : null;
            if (forSale) {
                item.itemTags.Add(UNPURCHASED);
                if (tagger == null) return;
                tagger.AddTag(c_locked_tag);
                tagger.AddTag(c_vended_tag);
                return;
            }
            item.itemTags.Remove(UNPURCHASED);
            if (tagger != null)
                tagger.RemoveTag(c_locked_tag);
        }

        public static string Format(int module, char kind, string value) =>
            PREFIX + module.ToString(CultureInfo.InvariantCulture) + ":" + kind + value;

        public static bool TryParseShop(string? entry, out int worth) {
            worth = 0;
            return entry != null && entry.StartsWith(SHOP, StringComparison.Ordinal) &&
                int.TryParse(entry.Substring(SHOP.Length), NumberStyles.None, CultureInfo.InvariantCulture, out worth);
        }

        public static bool TryParse(string? entry, out int module, out char kind, out string value) {
            module = 0;
            kind = '\0';
            value = string.Empty;
            if (entry == null || !entry.StartsWith(PREFIX, StringComparison.Ordinal)) return false;
            var colon = entry.IndexOf(':', PREFIX.Length);
            if (colon < 0 || colon + 1 >= entry.Length ||
                !int.TryParse(entry.Substring(PREFIX.Length, colon - PREFIX.Length), NumberStyles.None, CultureInfo.InvariantCulture, out module)) return false;
            kind = entry[colon + 1];
            value = entry.Substring(colon + 2);
            return true;
        }

        public static void RemovePacked(List<string> data) => data.RemoveAll(entry => entry != null && entry.StartsWith(PREFIX, StringComparison.Ordinal));
    }
}
