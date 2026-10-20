using System;
using System.Text;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// Rich text for event lines in the chat (see <see cref="WKTChat.PostEvent"/>). Events are written by the machine where they
    /// happen and passed on as rich text, so every received line is cut down to the tags an event may use: colors, bold,
    /// italics and the game's text effects (<c>shimmer</c>, <c>shake</c>...), played by the line's TextAnimator. Anything else,
    /// such as a size, a sprite or a link, is dropped, so no player can blow a line up across the screen.
    /// </summary>
    internal static class WKTChatRichText {
        /// <summary>The longest event line, in characters, tags included.</summary>
        public const int MAX_LENGTH = 256;
        /// <summary>The zero width space that follows an escaped tag opener.</summary>
        public const char ESCAPE_MARK = '​';
        /// <summary>Longer tags are not tags an event uses.</summary>
        private const int c_max_tag_length = 48;
        /// <summary>TextMeshPro styling an event may use.</summary>
        private static readonly string[] s_textTags = { "color", "b", "i", "u", "s" };
        /// <summary>
        /// TextAnimator behaviors: Text Animator's own and the game's (see <c>Perk.GetTitle</c>). Unknown ones would show as
        /// plain text, so only these pass.
        /// </summary>
        private static readonly string[] s_effectTags = {
            "shimmer", "shake", "wave", "wiggle", "rainb", "bounce", "swing", "incr", "dangle", "pend", "rot", "slide", "fade"
        };
        private static readonly StringBuilder s_sb = new(MAX_LENGTH);

        /// <summary>
        /// <paramref name="text"/> with only the allowed tags left, cut to <see cref="MAX_LENGTH"/>. A '&lt;' that opens no tag,
        /// or one already escaped, stays as text.
        /// </summary>
        public static string Sanitize(string? text) => Filter(text, true);

        /// <summary>
        /// <see cref="Sanitize"/> without the text effect tags too, for a chat that cannot play them: they would show as text.
        /// </summary>
        public static string StripEffects(string? text) => Filter(text, false);

        private static string Filter(string? text, bool keepEffects) {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            s_sb.Clear();
            var length = Math.Min(text!.Length, MAX_LENGTH);
            var i = 0;
            while (i < length) {
                var c = text[i];
                if (c != '<' || i + 1 >= length || text[i + 1] == ESCAPE_MARK) {
                    s_sb.Append(c);
                    i++;
                    continue;
                }
                var close = text.IndexOf('>', i + 1, Math.Min(length - i - 1, c_max_tag_length + 1));
                if (close < 0 || text.IndexOf('<', i + 1, close - i - 1) >= 0) {
                    // Not a tag: shown as text, and never able to pair with a later '>'.
                    s_sb.Append('<').Append(ESCAPE_MARK);
                    i++;
                    continue;
                }
                if (IsAllowed(text, i + 1, close, keepEffects))
                    s_sb.Append(text, i, close - i + 1);
                i = close + 1;
            }
            var result = s_sb.ToString();
            s_sb.Clear();
            return result;
        }

        /// <summary>Whether the tag between <paramref name="start"/> (after '&lt;') and <paramref name="end"/> ('&gt;') may stay.</summary>
        private static bool IsAllowed(string text, int start, int end, bool keepEffects) {
            if (start < end && text[start] == '/')
                start++;
            var nameEnd = start;
            while (nameEnd < end && char.IsLetter(text[nameEnd]))
                nameEnd++;
            if (nameEnd == start) return false;
            // The name ends the tag, or is followed by its value or parameters.
            if (nameEnd < end && text[nameEnd] != '=' && text[nameEnd] != ' ') return false;
            return IsNamed(text, start, nameEnd - start, s_textTags) || keepEffects && IsNamed(text, start, nameEnd - start, s_effectTags);
        }

        private static bool IsNamed(string text, int start, int length, string[] names) {
            foreach (var name in names) {
                if (name.Length == length && string.Compare(text, start, name, 0, length, StringComparison.OrdinalIgnoreCase) == 0)
                    return true;
            }
            return false;
        }
    }
}
