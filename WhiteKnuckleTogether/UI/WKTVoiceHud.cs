using System.Text;
using JetBrains.Annotations;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// During a multiplayer run, the names of whoever is talking right now, at the left edge of the screen, so a voice can be
    /// put to a name even when its speaker is out of sight. Rebuilds its text only when someone starts or stops talking.
    /// </summary>
    internal sealed class WKTVoiceHud : MonoBehaviour {
        private const float c_font_size = 22f;
        private const float c_margin = 24f;
        private const float c_width = 400f;
        private const int c_sorting_order = short.MaxValue - 4;
        private const string c_speaker_icon = "♪ ";
        private readonly bool[] m_speaking = new bool[byte.MaxValue + 1];
        private readonly StringBuilder m_sb = new();
        private GameObject m_canvas = null!;
        private TMP_Text m_text = null!;
        private WKTNetwork m_network = null!;

        [UsedImplicitly]
        private void Awake() {
            m_network = WKT.Instance.Network;
            Build();
        }

        [UsedImplicitly]
        private void OnDestroy() {
            if (m_canvas != null)
                Destroy(m_canvas);
        }

        [UsedImplicitly]
        private void LateUpdate() {
            var voice = WKTVoiceManager.Instance;
            var changed = false;
            foreach (var player in m_network.GetPlayersEnumerable()) {
                var speaking = voice != null && voice.IsSpeaking(player.Id);
                if (m_speaking[player.Id] == speaking) continue;
                m_speaking[player.Id] = speaking;
                changed = true;
            }
            if (changed)
                Refresh();
        }

        private void Refresh() {
            m_sb.Clear();
            foreach (var player in m_network.GetPlayersEnumerable()) {
                if (!m_speaking[player.Id]) continue;
                m_sb.Append("<color=#").Append(player.ColorHex).Append('>').Append(c_speaker_icon)
                    .Append(WKTChatText.Escape(player.friend.Name)).Append("</color>\n");
            }
            WKTGameFont.Adopt(m_text);
            m_text.text = m_sb.ToString();
        }

        private void Build() {
            m_canvas = new GameObject("WKT Voice HUD");
            var canvas = m_canvas.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = c_sorting_order;
            var scaler = m_canvas.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            var rect = (RectTransform)new GameObject("Speakers", typeof(RectTransform)).transform;
            rect.SetParent(m_canvas.transform, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 0.5f);
            rect.pivot = new Vector2(0f, 0f);
            rect.anchoredPosition = new Vector2(c_margin, 0f);
            rect.sizeDelta = new Vector2(c_width, 0f);
            var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            text.fontSize = c_font_size;
            text.alignment = TextAlignmentOptions.BottomLeft;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Overflow;
            text.raycastTarget = false;
            m_text = text;
        }
    }
}
