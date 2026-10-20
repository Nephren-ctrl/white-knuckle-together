using System;
using System.Collections.Generic;
using System.Globalization;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// An item's state mostly lives in <c>Item.data</c> ("type:value" entries such as ammo, charge, uses or a stopwatch's
    /// timer), which travels whole whenever an item is dropped, stolen or traded. The rest lives in its data modules, which
    /// the game rolls on first use: the text a note shows (<c>Item_Data_Text_Random</c>) and its sprite in hand
    /// (<c>Item_Data_HandSprite</c>). Those are packed into the data as extra entries before it is sent and unpacked where it
    /// arrives, so the same note reads the same for everyone.
    /// </summary>
    internal static class WKTItemModules {
        /// <summary>An entry is "wktmod&lt;module index&gt;:&lt;kind&gt;&lt;value&gt;". No type the game reads is a suffix of it.</summary>
        public const string PREFIX = "wktmod";
        public const char KIND_TEXT = 't';
        public const char KIND_SPRITE = 's';

        /// <summary>Writes the item's module state into its data, replacing what was packed before.</summary>
        public static void Pack(Item? item) {
            if (item?.dataModules == null || item.dataModules.Count == 0) return;
            item.data ??= new List<string>();
            RemovePacked(item.data);
            for (var i = 0; i < item.dataModules.Count; i++) {
                switch (item.dataModules[i]) {
                    case Item_Data_Text_Random text when !string.IsNullOrEmpty(text.selectedString) && text.strings != null:
                        var index = text.strings.IndexOf(text.selectedString);
                        if (index >= 0)
                            item.data.Add(Format(i, KIND_TEXT, index.ToString(CultureInfo.InvariantCulture)));
                        break;
                    case Item_Data_HandSprite sprite when sprite.handSprite != null:
                        item.data.Add(Format(i, KIND_SPRITE, sprite.handSprite.name));
                        break;
                }
            }
        }

        /// <summary>Applies module state packed into the item's data. Its modules must be its own, not a prefab's.</summary>
        public static void Unpack(Item? item) {
            if (item?.data == null || item.dataModules == null) return;
            foreach (var entry in item.data) {
                if (!TryParse(entry, out var module, out var kind, out var value) || module >= item.dataModules.Count) continue;
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

        public static string Format(int module, char kind, string value) =>
            PREFIX + module.ToString(CultureInfo.InvariantCulture) + ":" + kind + value;

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
