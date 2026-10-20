using System.Collections.Generic;
using System.Text;
using JetBrains.Annotations;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// Tells the local player, in the top right corner, whenever something waits for other players: a door that closes once
    /// everyone is in, a gate kept shut until the group catches up, a run that starts once everyone is ready. Whatever waits
    /// reports itself every frame it waits (<see cref="Report"/>); the panel shows every report of the frame, one per line, and
    /// fades out once nothing reports any more. Only multiplayer code reports, so singleplayer never creates it.
    /// </summary>
    internal sealed class WKTWaitHud : MonoBehaviour {
        private const float c_fade_rate = 5f;
        private const float c_margin = 24f;
        private const float c_font_size = 26f;
        /// <summary>Just below the toast's, which may sit on top of it.</summary>
        private const int c_sorting_order = short.MaxValue - 2;
        private static readonly List<Entry> s_entries = new();
        private static readonly List<Entry> s_shown = new();
        private static readonly StringBuilder s_text = new();
        private static WKTWaitHud? s_instance;
        private CanvasGroup m_group = null!;
        private TextMeshProUGUI m_text = null!;
        private float m_alpha;

        /// <summary>The panel's height while it shows, so the toast can sit below it.</summary>
        public static float VisibleHeight => s_instance != null && s_instance.m_alpha > 0f ? ((RectTransform)s_instance.m_text.transform.parent).rect.height : 0f;

        [UsedImplicitly]
        private void LateUpdate() {
            if (!Matches()) {
                s_shown.Clear();
                s_shown.AddRange(s_entries);
                if (s_entries.Count > 0)
                    Render();
            }
            m_alpha = Mathf.MoveTowards(m_alpha, s_entries.Count > 0 ? 1f : 0f, Time.unscaledDeltaTime * c_fade_rate);
            m_group.alpha = m_alpha;
            s_entries.Clear();
        }

        [UsedImplicitly]
        private void OnDestroy() {
            if (s_instance == this)
                s_instance = null;
        }

        /// <summary>
        /// Something waits this frame: shows "<paramref name="label"/> (<paramref name="ready"/>/<paramref name="total"/>)".
        /// Call from Update every frame it waits. <paramref name="label"/> should be a constant, so nothing is allocated.
        /// </summary>
        public static void Report(string label, int ready, int total) {
            if (s_instance == null)
                s_instance = Create();
            foreach (var entry in s_entries) {
                if (ReferenceEquals(entry.Label, label)) return;
            }
            s_entries.Add(new Entry(label, ready, total));
        }

        /// <summary>Removes the panel, such as when the mod shuts down.</summary>
        public static void DestroyInstance() {
            if (s_instance != null)
                Destroy(s_instance.gameObject);
            s_instance = null;
            s_entries.Clear();
            s_shown.Clear();
        }

        private static bool Matches() {
            if (s_entries.Count == 0) return true;
            if (s_entries.Count != s_shown.Count) return false;
            for (var i = 0; i < s_entries.Count; i++) {
                if (!s_entries[i].Equals(s_shown[i])) return false;
            }
            return true;
        }

        private void Render() {
            s_text.Clear();
            foreach (var entry in s_shown) {
                if (s_text.Length > 0)
                    s_text.Append('\n');
                s_text.Append(entry.Label).Append(" (").Append(entry.Ready).Append('/').Append(entry.Total).Append(')');
            }
            WKTGameFont.Adopt(m_text);
            m_text.SetText(s_text);
        }

        private static WKTWaitHud Create() {
            var root = new GameObject(nameof(WKTWaitHud));
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
            background.color = new Color(0f, 0f, 0f, 0.6f);
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
            text.color = new Color(1f, 0.85f, 0.4f);
            text.alignment = TextAlignmentOptions.Right;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.raycastTarget = false;
            var group = root.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.blocksRaycasts = false;
            group.interactable = false;
            var hud = root.AddComponent<WKTWaitHud>();
            hud.m_group = group;
            hud.m_text = text;
            return hud;
        }

        private readonly struct Entry {
            public readonly string Label;
            public readonly int Ready;
            public readonly int Total;

            public Entry(string label, int ready, int total) {
                Label = label;
                Ready = ready;
                Total = total;
            }

            public bool Equals(Entry other) => ReferenceEquals(Label, other.Label) && Ready == other.Ready && Total == other.Total;
        }
    }
}
