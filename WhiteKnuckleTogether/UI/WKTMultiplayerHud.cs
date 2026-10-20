using JetBrains.Annotations;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// The multiplayer HUD during a run: the name of the player the local player looks at, at any distance but never through
    /// walls, in that player's color (<see cref="WKTPlayerColors"/>). It sits below the crosshair, fading in while they look at
    /// the player and out when they look away. Run events go to the chat (see <see cref="WKTChat.PostEvent"/>).
    /// <para>Lives next to <see cref="WKTPlayerSync"/>, so it only exists during a multiplayer run.</para>
    /// </summary>
    internal sealed class WKTMultiplayerHud : MonoBehaviour {
        private const float c_fade_rate = 6f;
        private const float c_name_font_size = 30f;
        private const float c_name_offset = -70f;
        private const int c_sorting_order = short.MaxValue - 1;
        private WKTPlayerSync m_players = null!;
        private CanvasGroup m_nameGroup = null!;
        private TextMeshProUGUI m_name = null!;
        private GameObject m_canvas = null!;
        private float m_nameAlpha;
        private byte m_aimed = byte.MaxValue;

        public static WKTMultiplayerHud? Instance { get; private set; }

        [UsedImplicitly]
        private void Awake() {
            Instance = this;
            m_players = GetComponent<WKTPlayerSync>();
            Build();
        }

        [UsedImplicitly]
        private void OnDestroy() {
            if (Instance == this)
                Instance = null;
            if (m_canvas != null)
                Destroy(m_canvas);
        }

        [UsedImplicitly]
        private void LateUpdate() => UpdateAimedName();

        private void UpdateAimedName() {
            var aimed = FindAimedPlayer(out var name);
            if (aimed != byte.MaxValue && aimed != m_aimed) {
                WKTGameFont.Adopt(m_name);
                m_name.text = name;
                m_name.color = WKTPlayerColors.Get(aimed);
            }
            if (aimed != byte.MaxValue)
                m_aimed = aimed;
            m_nameAlpha = Mathf.MoveTowards(m_nameAlpha, aimed != byte.MaxValue ? 1f : 0f, Time.unscaledDeltaTime * c_fade_rate);
            m_nameGroup.alpha = m_nameAlpha;
        }

        /// <summary>The player whose body the camera's center ray reaches first with nothing in between, or none.</summary>
        private byte FindAimedPlayer(out string name) {
            name = string.Empty;
            var camera = Camera.main;
            var local = ENT_Player.playerObject;
            if (camera == null || local == null || local.dead) return byte.MaxValue;
            var cameraTransform = camera.transform;
            var ray = new Ray(cameraTransform.position, cameraTransform.forward);
            var best = byte.MaxValue;
            var bestAlong = float.PositiveInfinity;
            var bestPoint = Vector3.zero;
            for (var i = 0; i < WKTPlayerPositions.RemoteCount; i++) {
                var id = WKTPlayerPositions.GetRemoteId(i);
                if (!m_players.TryGetRemote(id, out var remote) || !remote.IsShown || !remote.TryHit(ray, out var point, out var along) || along >= bestAlong) continue;
                best = id;
                bestAlong = along;
                bestPoint = point;
                name = remote.DisplayName;
            }
            if (best == byte.MaxValue) return best;
            var mask = ~((1 << local.gameObject.layer) | (1 << LayerMask.NameToLayer("Ignore Raycast")) | (1 << LayerMask.NameToLayer("Inventory")));
            return Physics.Linecast(ray.origin, bestPoint, mask, QueryTriggerInteraction.Ignore) ? byte.MaxValue : best;
        }

        private void Build() {
            m_canvas = new GameObject(nameof(WKTMultiplayerHud));
            var canvas = m_canvas.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = c_sorting_order;
            var scaler = m_canvas.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            var nameObject = new GameObject("Aimed Name", typeof(RectTransform));
            var nameRect = (RectTransform)nameObject.transform;
            nameRect.SetParent(m_canvas.transform, false);
            nameRect.anchorMin = nameRect.anchorMax = new Vector2(0.5f, 0.5f);
            nameRect.pivot = new Vector2(0.5f, 0.5f);
            nameRect.anchoredPosition = new Vector2(0f, c_name_offset);
            nameRect.sizeDelta = new Vector2(800f, 60f);
            m_name = CreateText(nameRect, c_name_font_size, TextAlignmentOptions.Center);
            m_nameGroup = nameObject.AddComponent<CanvasGroup>();
            m_nameGroup.alpha = 0f;
            m_nameGroup.blocksRaycasts = false;
        }

        private static TextMeshProUGUI CreateText(Transform parent, float size, TextAlignmentOptions alignment) {
            var textObject = new GameObject("Text", typeof(RectTransform));
            textObject.transform.SetParent(parent, false);
            var rect = (RectTransform)textObject.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            var text = textObject.AddComponent<TextMeshProUGUI>();
            text.fontSize = size;
            text.color = Color.white;
            text.alignment = alignment;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.richText = true;
            text.raycastTarget = false;
            WKTGameFont.Adopt(text);
            return text;
        }
    }
}
