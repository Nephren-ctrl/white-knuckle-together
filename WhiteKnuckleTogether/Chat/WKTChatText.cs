using System.Text;
using UnityEngine;

namespace WhiteKnuckleTogether {
    /// <summary>Cleans chat text before it is sent, and escapes it before it is shown.</summary>
    internal static class WKTChatText {
        /// <summary>The longest message, in characters. The input field stops there too.</summary>
        public const int MAX_LENGTH = 200;
        private static readonly StringBuilder s_sb = new(MAX_LENGTH);

        /// <summary>
        /// <paramref name="text"/> on one line: control characters (line breaks, tabs) become spaces, runs of spaces become one,
        /// the ends are trimmed and the rest is cut to <see cref="MAX_LENGTH"/>. Empty if nothing is left to send.
        /// </summary>
        public static string Sanitize(string? text) {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            s_sb.Clear();
            var pendingSpace = false;
            foreach (var c in text) {
                if (char.IsControl(c) || char.IsWhiteSpace(c)) {
                    pendingSpace = s_sb.Length > 0;
                    continue;
                }
                if (pendingSpace) {
                    if (s_sb.Length + 1 >= MAX_LENGTH) break;
                    s_sb.Append(' ');
                    pendingSpace = false;
                }
                if (s_sb.Length >= MAX_LENGTH) break;
                // Never leave half of a surrogate pair at the cut.
                if (char.IsHighSurrogate(c) && s_sb.Length + 2 > MAX_LENGTH) break;
                s_sb.Append(c);
            }
            var result = s_sb.ToString();
            s_sb.Clear();
            return result;
        }

        /// <summary>
        /// Keeps player text from opening rich text tags, or TextAnimator's appearance tags on event lines: a zero width space
        /// follows every '&lt;' and '{'.
        /// </summary>
        public static string Escape(string text) {
            if (text.IndexOf('<') >= 0)
                text = text.Replace("<", "<​");
            if (text.IndexOf('{') >= 0)
                text = text.Replace("{", "{​");
            return text;
        }

        /// <summary>A player's name in their color, for event lines.</summary>
        public static string PlayerName(byte playerId, string name) => $"<color=#{WKTPlayerColors.GetHex(playerId)}><b>{Escape(name)}</b></color>";

        /// <summary>Text in bold, such as an item name.</summary>
        public static string Highlight(string text) => $"<b>{Escape(text)}</b>";

        /// <summary>Text shown in a color, such as an item name in its rarity's outline color.</summary>
        public static string Colored(string text, Color color) => $"<color=#{ColorUtility.ToHtmlStringRGB(color)}>{Escape(text)}</color>";
    }
}
