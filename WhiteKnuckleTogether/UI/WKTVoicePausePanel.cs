using System.Collections.Generic;
using JetBrains.Annotations;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// While the pause menu is open in a multiplayer run, a panel at the right edge of the screen lists the other players with
    /// their voice controls (<see cref="WKTVoicePlayerControl"/>), out of the way of the pause menu itself. Lives next to
    /// <see cref="WKTPlayerSync"/>, so only during a multiplayer run.
    /// </summary>
    internal sealed class WKTVoicePausePanel : MonoBehaviour {
        private const float c_width = 470f;
        private const float c_margin = 24f;
        private const float c_padding = 12f;
        private const float c_row_height = 40f;
        private const float c_name_width = 200f;
        private const float c_title_size = 26f;
        private const float c_name_size = 20f;
        private const float c_background_alpha = 0.7f;
        /// <summary>Above the pause menu, below the chat.</summary>
        private const int c_sorting_order = short.MaxValue - 3;
        private readonly List<GameObject> m_rows = new();
        private GameObject m_canvas = null!;
        private RectTransform m_panel = null!;
        private TMP_Text m_title = null!;
        private WKTNetwork m_network = null!;
        private bool m_shown;

        [UsedImplicitly]
        private void Awake() {
            m_network = WKT.Instance.Network;
            Build();
            m_canvas.SetActive(false);
        }

        [UsedImplicitly]
        private void OnDestroy() {
            if (m_canvas != null)
                Destroy(m_canvas);
        }

        [UsedImplicitly]
        private void LateUpdate() {
            // Not over the settings, which open from the pause menu and are wider than it.
            var gameManager = CL_GameManager.gMan;
            var show = WKTMultiplayerPause.IsOpen && (gameManager == null || gameManager.settingsMenu == null || !gameManager.settingsMenu.activeInHierarchy);
            if (show == m_shown) return;
            m_shown = show;
            if (show)
                Rebuild();
            m_canvas.SetActive(show);
        }

        /// <summary>Lists whoever is in the session right now. Players come and go, so the list is made fresh on every open.</summary>
        private void Rebuild() {
            foreach (var row in m_rows)
                Destroy(row);
            m_rows.Clear();
            WKTGameFont.Adopt(m_title);
            var local = m_network.LocalPlayer;
            foreach (var player in m_network.GetPlayersEnumerable()) {
                if (player == local) continue;
                m_rows.Add(CreateRow(player));
            }
            if (m_rows.Count == 0)
                m_rows.Add(CreateNote("Nobody else is here."));
            var height = c_padding * 2f + c_row_height * (m_rows.Count + 1);
            m_panel.sizeDelta = new Vector2(c_width, height);
        }

        private GameObject CreateRow(WKTPlayer player) {
            var row = CreateRect("Row " + player.Id, m_panel);
            PlaceRow(row);
            var nameText = CreateText(row, player.friend.Name, c_name_size);
            var nameRect = nameText.rectTransform;
            nameRect.anchorMin = Vector2.zero;
            nameRect.anchorMax = new Vector2(0f, 1f);
            nameRect.pivot = new Vector2(0f, 0.5f);
            nameRect.sizeDelta = new Vector2(c_name_width, 0f);
            nameText.color = player.Color;
            nameText.overflowMode = TextOverflowModes.Ellipsis;
            WKTVoicePlayerControl.Create(row).Bind(player, false);
            return row.gameObject;
        }

        private GameObject CreateNote(string note) {
            var row = CreateRect("Note", m_panel);
            PlaceRow(row);
            var text = CreateText(row, note, c_name_size);
            text.rectTransform.anchorMin = Vector2.zero;
            text.rectTransform.anchorMax = Vector2.one;
            text.color = new Color(1f, 1f, 1f, 0.6f);
            return row.gameObject;
        }

        /// <summary>Rows stack under the title from the top.</summary>
        private void PlaceRow(RectTransform row) {
            row.anchorMin = new Vector2(0f, 1f);
            row.anchorMax = new Vector2(1f, 1f);
            row.pivot = new Vector2(0.5f, 1f);
            row.sizeDelta = new Vector2(-c_padding * 2f, c_row_height);
            row.anchoredPosition = new Vector2(0f, -c_padding - c_row_height * (m_rows.Count + 1));
        }

        private void Build() {
            m_canvas = new GameObject("WKT Voice Pause Panel");
            var canvas = m_canvas.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = c_sorting_order;
            var scaler = m_canvas.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            m_canvas.AddComponent<GraphicRaycaster>();
            m_panel = CreateRect("Panel", m_canvas.transform);
            m_panel.anchorMin = m_panel.anchorMax = new Vector2(1f, 0.5f);
            m_panel.pivot = new Vector2(1f, 0.5f);
            m_panel.anchoredPosition = new Vector2(-c_margin, 0f);
            var background = m_panel.gameObject.AddComponent<Image>();
            background.color = new Color(0f, 0f, 0f, c_background_alpha);
            background.raycastTarget = false;
            var titleRect = CreateRect("Title", m_panel);
            titleRect.anchorMin = new Vector2(0f, 1f);
            titleRect.anchorMax = new Vector2(1f, 1f);
            titleRect.pivot = new Vector2(0.5f, 1f);
            titleRect.sizeDelta = new Vector2(-c_padding * 2f, c_row_height);
            titleRect.anchoredPosition = new Vector2(0f, -c_padding);
            m_title = CreateText(titleRect, "Voice Chat", c_title_size);
            m_title.rectTransform.anchorMin = Vector2.zero;
            m_title.rectTransform.anchorMax = Vector2.one;
        }

        private static TMP_Text CreateText(RectTransform parent, string value, float size) {
            var rect = CreateRect("Text", parent);
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            WKTGameFont.Adopt(text);
            text.text = value;
            text.fontSize = size;
            text.alignment = TextAlignmentOptions.MidlineLeft;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.raycastTarget = false;
            return text;
        }

        private static RectTransform CreateRect(string name, Transform parent) {
            var rect = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
            rect.SetParent(parent, false);
            return rect;
        }
    }
}
