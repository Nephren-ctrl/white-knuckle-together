using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// The game's own interface font, for the mod's texts. Found once and kept: the font most of the game's texts use that
    /// can write plain letters and digits. Taking whichever text Unity happened to list first gave the mod's texts a
    /// different font from scene to scene, sometimes a display font of other proportions or one with no letters at all.
    /// </summary>
    internal static class WKTGameFont {
        /// <summary>Letters and digits a font must have to write player names and chat.</summary>
        private const string c_required_characters = "AZaz09";
        private static readonly Dictionary<TMP_FontAsset, int> s_uses = new();
        private static TMP_FontAsset? s_font;

        /// <summary>
        /// Gives <paramref name="text"/> the game's own font, unless it already has one. The game's font is only found once
        /// a scene has loaded one, so call it again whenever the text is shown until then.
        /// </summary>
        public static void Adopt(TMP_Text text) {
            if (text.font != null && text.font != TMP_Settings.defaultFontAsset) return;
            var font = Find();
            if (font != null)
                text.font = font;
        }

        private static TMP_FontAsset? Find() {
            if (s_font != null) return s_font;
            var defaultFont = TMP_Settings.defaultFontAsset;
            foreach (var text in Object.FindObjectsByType<TMP_Text>(FindObjectsInactive.Include, FindObjectsSortMode.None)) {
                var font = text.font;
                if (font == null || font == defaultFont) continue;
                s_uses.TryGetValue(font, out var uses);
                s_uses[font] = uses + 1;
            }
            var best = 0;
            foreach (var pair in s_uses) {
                if (pair.Value <= best || !CanWrite(pair.Key)) continue;
                best = pair.Value;
                s_font = pair.Key;
            }
            s_uses.Clear();
            if (s_font != null)
                WKT.Instance.Logger.LogInfo($"The mod's texts use the game's font {s_font.name}.");
            return s_font;
        }

        private static bool CanWrite(TMP_FontAsset font) {
            foreach (var character in c_required_characters) {
                if (!font.HasCharacter(character, false, true)) return false;
            }
            return true;
        }
    }
}
