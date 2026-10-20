using JetBrains.Annotations;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// A short message in the top right corner that fades out on its own, above every menu and the HUD. Survives scene
    /// loads, so a message shown just before one stays up. Only multiplayer code shows it, so singleplayer never creates it.
    /// </summary>
    internal sealed class WKTToast : MonoBehaviour {
        private const float c_hold_seconds = 3f;
        private const float c_fade_seconds = 0.75f;
        private const float c_margin = 24f;
        private const float c_gap = 8f;
        private const float c_font_size = 26f;
        private const int c_sorting_order = short.MaxValue;
        private static WKTToast? s_instance;
        private CanvasGroup m_group = null!;
        private TextMeshProUGUI m_text = null!;
        private float m_shownTime;

        [UsedImplicitly]
        private void Update() {
            var elapsed = Time.unscaledTime - m_shownTime;
            if (elapsed < c_hold_seconds) return;
            var fade = (elapsed - c_hold_seconds) / c_fade_seconds;
            if (fade < 1f) {
                m_group.alpha = 1f - fade;
                return;
            }
            m_group.alpha = 0f;
            enabled = false;
        }

        [UsedImplicitly]
        private void OnDestroy() {
            if (s_instance == this)
                s_instance = null;
        }

        /// <summary>Shows <paramref name="message"/>, replacing any message still showing.</summary>
        public static void Show(string message) {
            if (s_instance == null)
                s_instance = Create();
            s_instance.ShowMessage(message);
        }

        /// <summary>Removes the toast, such as when the mod shuts down.</summary>
        public static void DestroyInstance() {
            if (s_instance != null)
                Destroy(s_instance.gameObject);
            s_instance = null;
        }

        private void ShowMessage(string message) {
            WKTGameFont.Adopt(m_text);
            m_text.text = message;
            // Below the wait panel while it shows, which shares the corner.
            var waitHeight = WKTWaitHud.VisibleHeight;
            ((RectTransform)m_text.transform.parent).anchoredPosition = new Vector2(-c_margin, -c_margin - (waitHeight > 0f ? waitHeight + c_gap : 0f));
            m_group.alpha = 1f;
            m_shownTime = Time.unscaledTime;
            enabled = true;
        }

        private static WKTToast Create() {
            var root = new GameObject(nameof(WKTToast));
            DontDestroyOnLoad(root);
            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = c_sorting_order;
            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            var panel = new GameObject("Panel", typeof(RectTransform));
            var panelRect = (RectTransform)panel.transform;
            panelRect.SetParent(root.transform, false);
            panelRect.anchorMin = Vector2.one;
            panelRect.anchorMax = Vector2.one;
            panelRect.pivot = Vector2.one;
            panelRect.anchoredPosition = new Vector2(-c_margin, -c_margin);
            var background = panel.AddComponent<Image>();
            background.color = new Color(0f, 0f, 0f, 0.75f);
            background.raycastTarget = false;
            var layout = panel.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(18, 18, 10, 10);
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            var fitter = panel.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var textObject = new GameObject("Text", typeof(RectTransform));
            textObject.transform.SetParent(panelRect, false);
            var text = textObject.AddComponent<TextMeshProUGUI>();
            text.fontSize = c_font_size;
            text.color = Color.white;
            text.alignment = TextAlignmentOptions.Right;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.raycastTarget = false;
            var group = root.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.blocksRaycasts = false;
            group.interactable = false;
            var toast = root.AddComponent<WKTToast>();
            toast.m_group = group;
            toast.m_text = text;
            toast.enabled = false;
            return toast;
        }
    }
}
