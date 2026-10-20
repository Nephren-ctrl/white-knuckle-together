using System;
using JetBrains.Annotations;
using Steamworks;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// A player's row in the lobby: avatar, name (cut short with an ellipsis when long), voice controls, and at the right end
    /// their ping to the host, a struck-through "VC" when their voice chat is off, and "HOST". Each has its own space, so
    /// nothing overlaps. Clicking the row opens their Steam profile. The host has KICK and BAN buttons on everyone else's row,
    /// left of the name, which ask in a popup before they act.
    /// </summary>
    internal sealed class WKTLobbyPlayer : MonoBehaviour, IPointerClickHandler {
        private const string c_profile_url = "https://steamcommunity.com/profiles/";
        private const string c_host_tag = "HOST";
        private const string c_small_format = "<size=60%><color=#9A9A9A>{0}</color></size>";
        /// <summary>The player turned voice chat off entirely: they hear nobody and nobody hears them.</summary>
        private const string c_voice_off_tag = "<size=75%><color=#E07070><s>VC</s></color></size>";
        /// <summary>Room left at the row's right end for the tag: ping, voice off and host.</summary>
        private const float c_tag_width = 190f;
        /// <summary>Space between the name and the voice controls.</summary>
        private const float c_name_gap = 12f;
        /// <summary>A long name shrinks down to this fraction of its size before it is cut short.</summary>
        private const float c_name_min_scale = 0.6f;
        /// <summary>What the local player's voice controls take up: the speaking dot alone.</summary>
        private const float c_local_voice_width = 30f;
        /// <summary>The size of the host's KICK and BAN buttons.</summary>
        private const float c_moderation_width = 56f;
        private const float c_moderation_height = 30f;
        private const float c_moderation_font_size = 18f;
        private const float c_moderation_gap = 4f;
        private static readonly System.Text.StringBuilder s_tag = new();
        private static readonly Color s_kickColor = new(0.55f, 0.12f, 0.12f, 0.7f);
        private static readonly Color s_banColor = new(0.3f, 0.04f, 0.04f, 0.85f);
        [SerializeField] private RawImage? m_avatar;
        [SerializeField] private TMP_Text? m_name;
        [SerializeField] private TMP_Text? m_index;
        [SerializeField] private TMP_Text? m_tag;
        private WKTVoicePlayerControl? m_voice;
        private Button? m_kick;
        private Button? m_ban;
        private WKTPlayer? m_player;
        private int m_avatarRequest;
        private ushort m_shownPing = WKTPings.UNKNOWN;
        private float m_laidOutWidth = -1f;
        /// <summary>The name's own left margin, which the KICK and BAN buttons are added to.</summary>
        private float m_nameMarginLeft;
        private bool m_isLocal;

        [UsedImplicitly]
        private void Awake() {
            WKT.Instance.Network.Pings.Changed += ShowPing;
            // Something must catch the click: the row's own picture if it has one, else a clear one.
            if (!TryGetComponent<Graphic>(out _))
                gameObject.AddComponent<Image>().color = Color.clear;
        }

        [UsedImplicitly]
        private void OnDestroy() {
            m_avatarRequest++;
            var wkt = WKT.InstanceOrNull;
            if (wkt != null)
                wkt.Network.Pings.Changed -= ShowPing;
        }

        /// <summary>Opens the player's Steam profile in the Steam overlay, or in the browser when the overlay is off.</summary>
        public void OnPointerClick(PointerEventData eventData) {
            if (m_player == null || eventData.button != PointerEventData.InputButton.Left) return;
            var steamId = m_player.friend.Id;
            try {
                if (SteamUtils.IsOverlayEnabled) {
                    SteamFriends.OpenUserOverlay(steamId, "steamid");
                    return;
                }
            }
            catch (Exception e) {
                WKT.Instance.Logger.LogWarning($"Could not open the Steam overlay: {e.Message}");
            }
            Application.OpenURL(c_profile_url + steamId.Value);
        }

        public void SetupReferences(RawImage? avatar, TMP_Text name, TMP_Text? index, TMP_Text? tag) {
            m_avatar = avatar;
            m_name = name;
            m_nameMarginLeft = name.margin.x;
            m_index = index;
            m_tag = tag;
        }

        public void Setup(WKTPlayer player, int index) {
            if (m_name == null) {
                WKT.Instance.Logger.LogError("Failed to set up lobby player. References are not assigned.");
                return;
            }
            m_player = player;
            m_isLocal = player == WKT.Instance.Network.LocalPlayer;
            m_laidOutWidth = -1f;
            m_name.text = player.friend.Name;
            m_name.color = player.Color;
            if (m_voice == null)
                m_voice = WKTVoicePlayerControl.Create(transform);
            // The local row shows only the speaking dot, the control's leftmost part: it is moved up against the tag.
            var voiceShift = m_isLocal ? WKTVoicePlayerControl.WIDTH - c_local_voice_width : 0f;
            ((RectTransform)m_voice.transform).anchoredPosition = new Vector2(-c_tag_width + voiceShift, 0f);
            LayOutTag();
            m_voice.Bind(player, m_isLocal);
            RefreshTag(player);
            SetIndex(index);
            LoadAvatar(player);
        }

        /// <summary>The pings changed: the tag shows the new one.</summary>
        private void ShowPing() {
            if (m_player == null || WKT.Instance.Network.Pings.Get(m_player.Id) == m_shownPing) return;
            RefreshTag(m_player);
        }

        /// <summary>
        /// Keeps the name clear of the voice controls and the tag: its text stops (with an ellipsis) where they begin. KICK and
        /// BAN sit left of it, after the avatar. Redone
        /// whenever the row's width changes, since rows are sized by their list's layout.
        /// </summary>
        [UsedImplicitly]
        private void LateUpdate() {
            if (m_name == null || transform is not RectTransform row) return;
            var width = row.rect.width;
            if (Mathf.Approximately(width, m_laidOutWidth)) return;
            m_laidOutWidth = width;
            // The local player's row shows only the speaking dot, not the volume controls.
            var voiceWidth = m_isLocal ? c_local_voice_width : WKTVoicePlayerControl.WIDTH;
            var limit = row.rect.xMax - (c_tag_width + voiceWidth + c_name_gap);
            var nameRect = m_name.rectTransform;
            var nameLeft = row.InverseTransformPoint(nameRect.TransformPoint(new Vector3(nameRect.rect.xMin, 0f, 0f))).x;
            var nameRight = row.InverseTransformPoint(nameRect.TransformPoint(new Vector3(nameRect.rect.xMax, 0f, 0f))).x;
            var margin = m_name.margin;
            // KICK starts where the name would, right of the avatar, BAN follows, and the name moves over for both.
            var moderationShown = m_kick != null && m_kick.gameObject.activeSelf;
            if (moderationShown) {
                var kickLeft = nameLeft + m_nameMarginLeft - row.rect.xMin;
                ((RectTransform)m_kick!.transform).anchoredPosition = new Vector2(kickLeft, 0f);
                ((RectTransform)m_ban!.transform).anchoredPosition = new Vector2(kickLeft + c_moderation_width + c_moderation_gap, 0f);
            }
            margin.x = m_nameMarginLeft + (moderationShown ? c_moderation_width * 2f + c_moderation_gap + c_name_gap : 0f);
            margin.z = Mathf.Max(0f, nameRight - limit);
            m_name.margin = margin;
            m_name.textWrappingMode = TextWrappingModes.NoWrap;
            m_name.overflowMode = TextOverflowModes.Ellipsis;
            if (m_name.enableAutoSizing) return;
            var size = m_name.fontSize;
            m_name.fontSizeMax = size;
            m_name.fontSizeMin = size * c_name_min_scale;
            m_name.enableAutoSizing = true;
        }

        /// <summary>Only the host has the KICK and BAN buttons, and never on their own row.</summary>
        private void UpdateModeration() {
            var shown = m_player != null && !m_isLocal && WKT.Instance.Network.Mode == NetworkMode.Host;
            if (shown && m_kick == null) {
                m_kick = CreateModerationButton("Kick", "KICK", s_kickColor, OnKickClicked);
                m_ban = CreateModerationButton("Ban", "BAN", s_banColor, OnBanClicked);
            }
            if (m_kick == null || m_kick.gameObject.activeSelf == shown) return;
            m_kick.gameObject.SetActive(shown);
            m_ban!.gameObject.SetActive(shown);
            m_laidOutWidth = -1f;
        }

        private Button CreateModerationButton(string objectName, string labelText, Color color, UnityEngine.Events.UnityAction onClick) {
            var root = new GameObject(objectName, typeof(RectTransform));
            var rect = (RectTransform)root.transform;
            rect.SetParent(transform, false);
            // Placed beside the name by LateUpdate.
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 0.5f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.sizeDelta = new Vector2(c_moderation_width, c_moderation_height);
            root.AddComponent<Image>().color = color;
            var button = root.AddComponent<Button>();
            button.onClick.AddListener(onClick);
            var label = new GameObject("Text", typeof(RectTransform));
            var labelRect = (RectTransform)label.transform;
            labelRect.SetParent(rect, false);
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.sizeDelta = Vector2.zero;
            var text = label.AddComponent<TextMeshProUGUI>();
            WKTGameFont.Adopt(text);
            text.fontSize = c_moderation_font_size;
            text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.raycastTarget = false;
            text.text = labelText;
            return button;
        }

        private void OnKickClicked() {
            var player = m_player;
            if (player == null) return;
            var name = WKTChatText.Highlight(player.friend.Name);
            if (!WKTAlert.ShowConfirm("Kick Player", $"Kick {name} from the lobby? They can join again.", "Kick",
                    () => WKT.Instance.Network.KickPlayer(player)))
                WKTToast.Show("Could not kick the player.");
        }

        private void OnBanClicked() {
            var player = m_player;
            if (player == null) return;
            var name = WKTChatText.Highlight(player.friend.Name);
            if (!WKTAlert.ShowConfirm("Ban Player", $"Ban {name} from the lobby? They cannot join again until it closes.", "Ban",
                    () => WKT.Instance.Network.BanPlayer(player)))
                WKTToast.Show("Could not ban the player.");
        }

        private void LayOutTag() {
            if (m_tag == null) return;
            var tagRect = m_tag.rectTransform;
            tagRect.anchorMin = new Vector2(1f, 0f);
            tagRect.anchorMax = new Vector2(1f, 1f);
            tagRect.pivot = new Vector2(1f, 0.5f);
            tagRect.sizeDelta = new Vector2(c_tag_width, 0f);
            tagRect.anchoredPosition = Vector2.zero;
            m_tag.horizontalAlignment = HorizontalAlignmentOptions.Right;
            m_tag.verticalAlignment = VerticalAlignmentOptions.Middle;
            m_tag.textWrappingMode = TextWrappingModes.NoWrap;
            m_tag.overflowMode = TextOverflowModes.Overflow;
        }

        /// <summary>Call when the player may have become, or stopped being, the host, or turned voice chat on or off.</summary>
        public void RefreshTag(WKTPlayer player) {
            UpdateModeration();
            if (m_tag == null) return;
            var network = WKT.Instance.Network;
            m_shownPing = network.Pings.Get(player.Id);
            m_tag.text = FormatTag(WKTPings.Format(m_shownPing), network.HasVoiceChatOff(player), player.IsHost);
        }

        /// <summary>The tag's text: ping and voice off small and grey, then HOST, each only when it applies.</summary>
        public static string FormatTag(string ping, bool voiceOff, bool isHost) {
            s_tag.Clear();
            if (ping.Length > 0)
                s_tag.AppendFormat(c_small_format, ping);
            if (voiceOff) {
                if (s_tag.Length > 0)
                    s_tag.Append("  ");
                s_tag.Append(c_voice_off_tag);
            }
            if (isHost) {
                if (s_tag.Length > 0)
                    s_tag.Append("  ");
                s_tag.Append(c_host_tag);
            }
            return s_tag.ToString();
        }

        public void SetIndex(int index) {
            if (m_index != null)
                m_index.SetText("{0}", index + 1);
        }

        private async void LoadAvatar(WKTPlayer player) {
            if (m_avatar == null) return;
            // Hidden until loaded so rows never flash the template's placeholder picture.
            m_avatar.enabled = false;
            var request = ++m_avatarRequest;
            try {
                var texture = await WKTSteamAvatarCache.GetAsync(player.friend);
                // The row may have been destroyed or set up for another player while waiting.
                if (this == null || request != m_avatarRequest || texture == null || m_avatar == null) return;
                m_avatar.texture = texture;
                m_avatar.uvRect = new Rect(0f, 0f, 1f, 1f);
                m_avatar.enabled = true;
            }
            catch (Exception e) {
                WKT.Instance.Logger.LogError($"Failed to show avatar for player {player.Id}: {e}");
            }
        }
    }
}
