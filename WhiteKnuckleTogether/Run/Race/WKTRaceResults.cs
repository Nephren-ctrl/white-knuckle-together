using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// The end of a race on every machine, the losers' included: a card over the screen naming the winner, with every player's
    /// place. It shows over the game's fade and score screen for <see cref="SHOW_SECONDS"/>, then fades, leaving the score
    /// screen and its buttons. Built on the race HUD's canvas.
    /// </summary>
    internal sealed class WKTRaceResults {
        /// <summary>How long the card stays before it fades.</summary>
        public const float SHOW_SECONDS = 8f;
        private const float c_fade_seconds = 1f;
        private const float c_width = 900f;
        private const float c_title_size = 64f;
        private const float c_line_size = 38f;
        private const float c_padding = 40f;
        private static readonly Color s_backing = new(0f, 0f, 0f, 0.85f);
        private static readonly Color s_winColor = new(1f, 0.85f, 0.3f);
        private static readonly Color s_lossColor = new(0.85f, 0.85f, 0.85f);
        private readonly CanvasGroup m_group;
        private readonly float m_shownAt;

        private WKTRaceResults(CanvasGroup group) {
            m_group = group;
            m_shownAt = Time.unscaledTime;
        }

        /// <summary>
        /// Shows the card under <paramref name="canvas"/>. <paramref name="title"/> is rich text; <paramref name="standings"/>
        /// lists players by place, with <paramref name="alive"/> marking who was still climbing when it ended.
        /// </summary>
        public static WKTRaceResults Show(Transform canvas, string title, bool localWon, System.ReadOnlySpan<byte> standings, System.ReadOnlySpan<bool> alive,
                                          byte winner, WKTNetwork network) {
            var root = (RectTransform)new GameObject("WKT Race Results", typeof(RectTransform)).transform;
            root.SetParent(canvas, false);
            root.anchorMin = root.anchorMax = root.pivot = new Vector2(0.5f, 0.5f);
            var group = root.gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            group.interactable = false;
            var backing = root.gameObject.AddComponent<Image>();
            backing.color = s_backing;
            backing.raycastTarget = false;
            var layout = root.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset((int)c_padding, (int)c_padding, (int)c_padding, (int)c_padding);
            layout.spacing = 12f;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            var fitter = root.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            root.sizeDelta = new Vector2(c_width, 0f);
            AddText(root, "RACE OVER", c_line_size, localWon ? s_winColor : s_lossColor);
            AddText(root, title, c_title_size, Color.white);
            var builder = new StringBuilder();
            var place = 0;
            var shared = false;
            for (var i = 0; i < standings.Length; i++) {
                var id = standings[i];
                var stillClimbing = alive[i] && id != winner;
                // Everyone still climbing shares the place after the winner.
                if (!(stillClimbing && shared))
                    place = i + 1;
                shared |= stillClimbing;
                var name = network.TryGetPlayer(id, out var player) && player != null ? player.friend.Name ?? "?" : "?";
                var status = id == winner ? "won" : stillClimbing ? "still climbing" : "eliminated";
                builder.Append(Ordinal(place)).Append("   ").Append(WKTChatText.PlayerName(id, name)).Append("   <alpha=#AA>").Append(status).Append("<alpha=#FF>");
                if (i < standings.Length - 1)
                    builder.Append('\n');
            }
            if (builder.Length > 0)
                AddText(root, builder.ToString(), c_line_size, Color.white);
            return new WKTRaceResults(group);
        }

        /// <summary>Fades the card once its time is up. False once it is gone.</summary>
        public bool Update() {
            if (m_group == null) return false;
            var fade = (Time.unscaledTime - m_shownAt - SHOW_SECONDS) / c_fade_seconds;
            if (fade <= 0f) return true;
            if (fade >= 1f) {
                Object.Destroy(m_group.gameObject);
                return false;
            }
            m_group.alpha = 1f - fade;
            return true;
        }

        /// <summary>"1st", "2nd", "3rd", "4th"...</summary>
        public static string Ordinal(int place) {
            var lastTwo = place % 100;
            if (lastTwo is >= 11 and <= 13) return place + "th";
            return (place % 10) switch {
                1 => place + "st",
                2 => place + "nd",
                3 => place + "rd",
                _ => place + "th"
            };
        }

        private static void AddText(Transform parent, string richText, float size, Color color) {
            var rect = (RectTransform)new GameObject("Text", typeof(RectTransform)).transform;
            rect.SetParent(parent, false);
            var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            WKTGameFont.Adopt(text);
            text.fontSize = size;
            text.color = color;
            text.alignment = TextAlignmentOptions.Center;
            text.raycastTarget = false;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.text = richText;
        }
    }
}
