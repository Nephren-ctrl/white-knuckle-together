using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// Who a dead player is spectating, how to switch, and with Dead Can Talk on whether the living hear them, at the bottom
    /// of the screen. Lives only in the run's scene.
    /// </summary>
    internal sealed class WKTSpectateHud : MonoBehaviour {
        private const float c_margin = 64f;
        private const float c_font_size = 30f;
        private const float c_hint_font_size = 20f;
        /// <summary>Below the toast and the pause menu's own canvases, which sort above the game's HUD.</summary>
        private const int c_sorting_order = 100;
        private const string c_hint = "<size={0}><color=#BBBBBB>Left / right click to switch player</color></size>";
        private const string c_talk_hint = "<size={0}><color=#BBBBBB>Hold Focus to talk to the living</color></size>";
        private const string c_talking = "<size={0}><color=#9FE0FF>The living can hear you</color></size>";
        private Canvas m_canvas = null!;
        private TextMeshProUGUI m_text = null!;
        private string m_hint = null!;
        private string m_talkHint = null!;
        private string m_talking = null!;
        private string? m_shownName;
        private WKTDeadTalk m_shownTalk;

        public static WKTSpectateHud Create() {
            var root = new GameObject(nameof(WKTSpectateHud));
            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = c_sorting_order;
            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            var textObject = new GameObject("Text", typeof(RectTransform));
            var rect = (RectTransform)textObject.transform;
            rect.SetParent(root.transform, false);
            rect.anchorMin = new Vector2(0.5f, 0f);
            rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.anchoredPosition = new Vector2(0f, c_margin);
            rect.sizeDelta = new Vector2(1200f, 120f);
            var text = textObject.AddComponent<TextMeshProUGUI>();
            text.fontSize = c_font_size;
            text.color = Color.white;
            text.alignment = TextAlignmentOptions.Bottom;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.raycastTarget = false;
            text.richText = true;
            var hud = root.AddComponent<WKTSpectateHud>();
            hud.m_canvas = canvas;
            hud.m_text = text;
            hud.m_hint = string.Format(c_hint, c_hint_font_size);
            hud.m_talkHint = string.Format(c_talk_hint, c_hint_font_size);
            hud.m_talking = string.Format(c_talking, c_hint_font_size);
            canvas.enabled = false;
            return hud;
        }

        /// <summary>Shows the spectated player's name and whether the living hear the local player. Only rebuilds the text when either changes.</summary>
        public void Show(string playerName, WKTDeadTalk talk) {
            if (m_canvas.enabled && playerName == m_shownName && talk == m_shownTalk) return;
            WKTGameFont.Adopt(m_text);
            m_shownName = playerName;
            m_shownTalk = talk;
            var talkLine = talk switch {
                WKTDeadTalk.Talking => "\n" + m_talking,
                WKTDeadTalk.CanTalk => "\n" + m_talkHint,
                _ => string.Empty
            };
            // Steam names may contain anything, rich text tags included.
            m_text.text = $"Spectating <b><noparse>{playerName}</noparse></b>\n{m_hint}{talkLine}";
            m_canvas.enabled = true;
        }

        public void Hide() {
            m_canvas.enabled = false;
            m_shownName = null;
        }
    }
}
