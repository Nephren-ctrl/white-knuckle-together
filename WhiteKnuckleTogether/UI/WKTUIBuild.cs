using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace WhiteKnuckleTogether {
    /// <summary>Building blocks for interface the mod builds in code.</summary>
    internal static class WKTUIBuild {
        private static readonly Color s_fallbackBackground = new(0f, 0f, 0f, 0.35f);

        public static RectTransform CreateRect(string objectName, Transform parent) {
            var rect = new GameObject(objectName, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            return rect;
        }

        /// <summary>Makes <paramref name="target"/> look like <paramref name="style"/>, or a dark tint without one.</summary>
        public static void CopyBackground(Image target, Image? style) {
            if (style == null) {
                target.color = s_fallbackBackground;
                return;
            }
            target.sprite = style.sprite;
            target.type = style.type;
            target.color = style.color;
            target.pixelsPerUnitMultiplier = style.pixelsPerUnitMultiplier;
        }

        /// <summary>
        /// A one line text in the game's font, cut short with an ellipsis. Rich text is off: these texts show what players
        /// wrote, which must not be able to restyle them.
        /// </summary>
        public static TMP_Text CreateText(string objectName, Transform parent, float fontSize, Color color, HorizontalAlignmentOptions alignment) {
            var text = CreateRect(objectName, parent).gameObject.AddComponent<TextMeshProUGUI>();
            WKTGameFont.Adopt(text);
            text.fontSize = fontSize;
            text.color = color;
            text.horizontalAlignment = alignment;
            text.verticalAlignment = VerticalAlignmentOptions.Middle;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.richText = false;
            text.raycastTarget = false;
            return text;
        }
    }
}
