using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// Hides bad words in chat messages, for players who turned on "Censor Chat". Only changes what this player sees: other
    /// players and the network still get the message as written. Words are compared case-insensitively with common
    /// letter-for-symbol swaps (sh1t, @ss) undone, so a matched word keeps its first character and the rest becomes '*'.
    /// </summary>
    internal sealed class WKTChatFilter {
        public const char MASK = '*';
        private const string c_resource_suffix = "badwords.txt";
        private const char c_root_prefix = '*';
        private const char c_comment_prefix = '#';
        /// <summary>Endings stripped to find a listed word in its inflections (dicks, raping, wankers).</summary>
        private static readonly string[] s_suffixes = { "s", "es", "ing", "ed", "er", "ers", "y", "ies" };
        private static WKTChatFilter? s_default;
        private readonly HashSet<string> m_words = new(StringComparer.Ordinal);
        /// <summary>Words with doubled letters collapsed ("fuuuck" to "fuck"), for stretched spellings.</summary>
        private readonly HashSet<string> m_collapsedWords = new(StringComparer.Ordinal);
        private readonly List<string> m_roots = new();
        private readonly StringBuilder m_result = new();
        private readonly StringBuilder m_token = new();
        private readonly StringBuilder m_scratch = new();

        /// <summary>The filter with the mod's built-in word list.</summary>
        public static WKTChatFilter Default => s_default ??= LoadDefault();

        public WKTChatFilter(IEnumerable<string> lines) {
            foreach (var raw in lines) {
                var line = raw.Trim();
                if (line.Length == 0 || line[0] == c_comment_prefix) continue;
                if (line[0] == c_root_prefix) {
                    var root = Normalize(line.Substring(1));
                    if (root.Length > 0)
                        m_roots.Add(root);
                    continue;
                }
                var word = Normalize(line);
                if (word.Length == 0) continue;
                m_words.Add(word);
                m_collapsedWords.Add(Collapse(word));
            }
        }

        /// <summary><paramref name="text"/> with every bad word masked. The same string when there are none.</summary>
        public string Censor(string text) {
            m_result.Clear();
            var changed = false;
            var i = 0;
            while (i < text.Length) {
                if (!IsWordChar(text, i)) {
                    m_result.Append(text[i++]);
                    continue;
                }
                var start = i;
                while (i < text.Length && IsWordChar(text, i))
                    i++;
                if (IsBad(text, start, i - start)) {
                    changed = true;
                    m_result.Append(text[start]);
                    m_result.Append(MASK, i - start - 1);
                }
                else
                    m_result.Append(text, start, i - start);
            }
            var result = changed ? m_result.ToString() : text;
            m_result.Clear();
            return result;
        }

        private bool IsBad(string text, int start, int length) {
            m_token.Clear();
            for (var i = start; i < start + length; i++)
                m_token.Append(Fold(text[i]));
            var token = m_token.ToString();
            if (IsListed(token) || ContainsRoot(token)) return true;
            // Only stretched spellings are compared collapsed, or "as" would match "ass".
            var collapsed = Collapse(token);
            return collapsed.Length != token.Length && (m_collapsedWords.Contains(collapsed) || ContainsRoot(collapsed));
        }

        private bool ContainsRoot(string token) {
            foreach (var root in m_roots)
                if (token.IndexOf(root, StringComparison.Ordinal) >= 0) return true;
            return false;
        }

        private bool IsListed(string token) {
            if (m_words.Contains(token)) return true;
            foreach (var suffix in s_suffixes) {
                if (token.Length <= suffix.Length + 1 || !token.EndsWith(suffix, StringComparison.Ordinal)) continue;
                if (m_words.Contains(token.Substring(0, token.Length - suffix.Length))) return true;
            }
            return false;
        }

        private string Collapse(string word) {
            m_scratch.Clear();
            foreach (var c in word)
                if (m_scratch.Length == 0 || m_scratch[m_scratch.Length - 1] != c)
                    m_scratch.Append(c);
            var result = m_scratch.ToString();
            m_scratch.Clear();
            return result;
        }

        private static string Normalize(string word) {
            var chars = new char[word.Length];
            for (var i = 0; i < word.Length; i++)
                chars[i] = Fold(word[i]);
            return new string(chars);
        }

        /// <summary>
        /// Letters, digits and the symbols people swap in for letters. '!' only counts before a letter or digit, since it ends
        /// most sentences it appears in.
        /// </summary>
        private static bool IsWordChar(string text, int i) {
            var c = text[i];
            if (char.IsLetterOrDigit(c) || c == '@' || c == '$') return true;
            return c == '!' && i + 1 < text.Length && char.IsLetterOrDigit(text[i + 1]);
        }

        private static char Fold(char c) {
            switch (c) {
                case '0': return 'o';
                case '1':
                case '!': return 'i';
                case '3': return 'e';
                case '4':
                case '@': return 'a';
                case '5':
                case '$': return 's';
                case '7': return 't';
                default: return char.ToLowerInvariant(c);
            }
        }

        private static WKTChatFilter LoadDefault() {
            var assembly = typeof(WKTChatFilter).Assembly;
            foreach (var name in assembly.GetManifestResourceNames()) {
                if (!name.EndsWith(c_resource_suffix, StringComparison.Ordinal)) continue;
                using var stream = assembly.GetManifestResourceStream(name);
                if (stream == null) break;
                using var reader = new StreamReader(stream, Encoding.UTF8);
                return new WKTChatFilter(reader.ReadToEnd().Split('\n'));
            }
            return new WKTChatFilter(Array.Empty<string>());
        }
    }
}
