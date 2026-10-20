using BepInEx.Configuration;
using JetBrains.Annotations;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// One player's voice controls: a dot that lights up while they talk, a slider for how loud this player hears them, and a
    /// mute button. Saved per Steam user, so the same person keeps their volume across lobbies, runs and sessions. For the
    /// local player only the dot shows. Used by the lobby's player rows and the pause screen's voice panel.
    /// </summary>
    internal sealed class WKTVoicePlayerControl : MonoBehaviour {
        public const float WIDTH = 230f;
        public const float HEIGHT = 30f;
        private const float c_dot_size = 14f;
        private const float c_slider_width = 120f;
        private const float c_slider_height = 10f;
        private const float c_button_width = 70f;
        private const float c_gap = 8f;
        private const float c_font_size = 16f;
        private const string c_mute_text = "MUTE";
        private const string c_muted_text = "MUTED";
        private static readonly Color s_silent = new(1f, 1f, 1f, 0.15f);
        private static readonly Color s_speaking = new(0.4f, 1f, 0.45f, 1f);
        private static readonly Color s_track = new(0f, 0f, 0f, 0.6f);
        private static readonly Color s_fill = new(1f, 1f, 1f, 0.7f);
        private static readonly Color s_mutedFill = new(1f, 0.35f, 0.35f, 0.7f);
        private static readonly Color s_button = new(0f, 0f, 0f, 0.6f);
        private Image m_dot = null!;
        private Slider m_slider = null!;
        private Image m_fill = null!;
        private GameObject m_muteButton = null!;
        private TMP_Text m_muteText = null!;
        private ConfigEntry<float>? m_volume;
        private ConfigEntry<bool>? m_muted;
        private byte m_playerId;
        private bool m_bound;

        /// <summary>Builds the controls under <paramref name="parent"/>, right-aligned, unbound until <see cref="Bind"/>.</summary>
        public static WKTVoicePlayerControl Create(Transform parent) {
            var root = CreateRect("WKT Voice Controls", parent);
            root.anchorMin = root.anchorMax = new Vector2(1f, 0.5f);
            root.pivot = new Vector2(1f, 0.5f);
            root.sizeDelta = new Vector2(WIDTH, HEIGHT);
            var control = root.gameObject.AddComponent<WKTVoicePlayerControl>();
            control.Build(root);
            return control;
        }

        /// <summary>Shows <paramref name="player"/>'s voice. Only the speaking dot for the local player.</summary>
        public void Bind(WKTPlayer player, bool isLocal) {
            m_playerId = player.Id;
            m_bound = true;
            var config = WKTPlugin.ModConfig;
            var showSettings = !isLocal && config != null;
            m_slider.gameObject.SetActive(showSettings);
            m_muteButton.SetActive(showSettings);
            if (!showSettings) return;
            var steamId = player.friend.Id.Value;
            m_volume = config!.GetPlayerVolume(steamId);
            m_muted = config.GetPlayerMuted(steamId);
            Refresh();
        }

        [UsedImplicitly]
        private void Update() {
            if (!m_bound) return;
            var voice = WKTVoiceManager.Instance;
            m_dot.color = voice != null && voice.IsSpeaking(m_playerId) ? s_speaking : s_silent;
            // Another panel may have changed the same player's settings.
            if (m_volume != null && !Mathf.Approximately(m_slider.value, m_volume.Value))
                Refresh();
        }

        private void Refresh() {
            if (m_volume == null || m_muted == null) return;
            m_slider.SetValueWithoutNotify(m_volume.Value);
            m_fill.color = m_muted.Value ? s_mutedFill : s_fill;
            m_muteText.text = m_muted.Value ? c_muted_text : c_mute_text;
        }

        private void OnSlider(float value) {
            if (m_volume == null) return;
            m_volume.Value = value;
            // Moving the slider is a clear sign of wanting to hear them.
            if (m_muted != null && m_muted.Value && value > 0f)
                m_muted.Value = false;
            Refresh();
        }

        private void OnMute() {
            if (m_muted == null) return;
            m_muted.Value = !m_muted.Value;
            Refresh();
        }

        #region Building

        private void Build(RectTransform root) {
            var x = 0f;
            var dot = Place(CreateRect("Speaking", root), x, c_dot_size, c_dot_size);
            m_dot = dot.gameObject.AddComponent<Image>();
            m_dot.color = s_silent;
            m_dot.raycastTarget = false;
            x += c_dot_size + c_gap;
            m_slider = BuildSlider(Place(CreateRect("Volume", root), x, c_slider_width, HEIGHT));
            x += c_slider_width + c_gap;
            var button = Place(CreateRect("Mute", root), x, c_button_width, HEIGHT - 6f);
            m_muteButton = button.gameObject;
            var background = m_muteButton.AddComponent<Image>();
            background.color = s_button;
            var buttonComponent = m_muteButton.AddComponent<Button>();
            buttonComponent.targetGraphic = background;
            buttonComponent.onClick.AddListener(OnMute);
            m_muteText = CreateText(button, c_mute_text);
        }

        private Slider BuildSlider(RectTransform rect) {
            var track = CreateRect("Track", rect);
            track.anchorMin = new Vector2(0f, 0.5f);
            track.anchorMax = new Vector2(1f, 0.5f);
            track.sizeDelta = new Vector2(0f, c_slider_height);
            var trackImage = track.gameObject.AddComponent<Image>();
            trackImage.color = s_track;
            var fillArea = CreateRect("Fill Area", track);
            Stretch(fillArea);
            var fill = CreateRect("Fill", fillArea);
            Stretch(fill);
            m_fill = fill.gameObject.AddComponent<Image>();
            m_fill.color = s_fill;
            m_fill.raycastTarget = false;
            var handleArea = CreateRect("Handle Area", rect);
            Stretch(handleArea);
            var handle = CreateRect("Handle", handleArea);
            handle.sizeDelta = new Vector2(8f, 0f);
            var handleImage = handle.gameObject.AddComponent<Image>();
            handleImage.color = Color.white;
            var slider = rect.gameObject.AddComponent<Slider>();
            slider.fillRect = fill;
            slider.handleRect = handle;
            slider.targetGraphic = handleImage;
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = 0f;
            slider.maxValue = WKTConfig.MAX_VOLUME;
            slider.value = 1f;
            slider.onValueChanged.AddListener(OnSlider);
            return slider;
        }

        private static TMP_Text CreateText(RectTransform parent, string value) {
            var rect = CreateRect("Text", parent);
            Stretch(rect);
            var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            WKTGameFont.Adopt(text);
            text.text = value;
            text.fontSize = c_font_size;
            text.alignment = TextAlignmentOptions.Center;
            text.raycastTarget = false;
            return text;
        }

        private static RectTransform Place(RectTransform rect, float x, float width, float height) {
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 0.5f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.anchoredPosition = new Vector2(x, 0f);
            rect.sizeDelta = new Vector2(width, height);
            return rect;
        }

        private static void Stretch(RectTransform rect) {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        private static RectTransform CreateRect(string name, Transform parent) {
            var rect = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
            rect.SetParent(parent, false);
            return rect;
        }

        #endregion
    }
}
