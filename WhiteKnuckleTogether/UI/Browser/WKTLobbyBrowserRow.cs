using System.Text;
using JetBrains.Annotations;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// A lobby in the lobby browser: its name with the players in it below, then how many there are, the ping to the host,
    /// and a Join button, or why it cannot be joined. A lobby that cannot be joined is greyed out.
    /// </summary>
    internal sealed class WKTLobbyBrowserRow : MonoBehaviour {
        // Sizes are in the menu canvas's units (1280 by 720).
        public const float HEIGHT = 56f;
        private const float c_padding = 14f;
        private const float c_spacing = 12f;
        private const float c_players_width = 56f;
        private const float c_ping_width = 76f;
        private const float c_action_width = 140f;
        private const float c_join_height = 34f;
        private const float c_name_font_size = 20f;
        private const float c_members_font_size = 14f;
        private const float c_detail_font_size = 16f;
        private const float c_unjoinable_alpha = 0.45f;
        private const string c_members_separator = ", ";
        private const string c_unknown_ping = "? ms";
        private static readonly Color s_membersColor = new(0.6f, 0.6f, 0.6f, 1f);
        private static readonly Color s_statusColor = new(0.88f, 0.44f, 0.44f, 1f);
        private static readonly StringBuilder s_sb = new();
        [SerializeField] private TMP_Text? m_name;
        [SerializeField] private TMP_Text? m_members;
        [SerializeField] private TMP_Text? m_players;
        [SerializeField] private TMP_Text? m_ping;
        [SerializeField] private TMP_Text? m_status;
        [SerializeField] private Button? m_join;
        [SerializeField] private CanvasGroup? m_group;
        private WKTLobbyBrowser? m_browser;
        private WKTLobbyListing? m_listing;

        [UsedImplicitly]
        private void Awake() {
            if (m_join != null)
                m_join.onClick.AddListener(OnJoinClicked);
        }

        /// <summary>A row with no lobby yet, for <see cref="WKTLobbyBrowser"/> to clone. Built under an inactive parent so nothing wakes up.</summary>
        public static WKTLobbyBrowserRow CreateTemplate(Transform parent, GameObject? buttonFab) {
            var rect = WKTUIBuild.CreateRect("Lobby Row", parent);
            rect.gameObject.AddComponent<LayoutElement>().preferredHeight = HEIGHT;
            // A plain tint: the game's panel style is too faint to tell rows apart.
            var background = rect.gameObject.AddComponent<Image>();
            WKTUIBuild.CopyBackground(background, null);
            background.raycastTarget = false;
            var row = rect.gameObject.AddComponent<WKTLobbyBrowserRow>();
            row.m_group = rect.gameObject.AddComponent<CanvasGroup>();
            var detailsWidth = c_players_width + c_ping_width + c_action_width + c_spacing * 3f + c_padding;
            // Each text's box must be taller than its line, or the ellipsis overflow hides the line altogether.
            row.m_name = WKTUIBuild.CreateText("Name", rect, c_name_font_size, Color.white, HorizontalAlignmentOptions.Left);
            Place(row.m_name.rectTransform, 0f, 0.4f, 1f, 1f, c_padding, -detailsWidth);
            row.m_name.verticalAlignment = VerticalAlignmentOptions.Bottom;
            row.m_members = WKTUIBuild.CreateText("Members", rect, c_members_font_size, s_membersColor, HorizontalAlignmentOptions.Left);
            Place(row.m_members.rectTransform, 0f, 0f, 1f, 0.42f, c_padding, -detailsWidth);
            row.m_members.verticalAlignment = VerticalAlignmentOptions.Top;
            var right = c_padding + c_action_width + c_spacing;
            row.m_ping = WKTUIBuild.CreateText("Ping", rect, c_detail_font_size, s_membersColor, HorizontalAlignmentOptions.Right);
            PlaceRight(row.m_ping.rectTransform, right, c_ping_width);
            right += c_ping_width + c_spacing;
            row.m_players = WKTUIBuild.CreateText("Players", rect, c_detail_font_size, Color.white, HorizontalAlignmentOptions.Right);
            PlaceRight(row.m_players.rectTransform, right, c_players_width);
            row.m_status = WKTUIBuild.CreateText("Status", rect, c_detail_font_size, s_statusColor, HorizontalAlignmentOptions.Right);
            PlaceRight(row.m_status.rectTransform, c_padding, c_action_width);
            if (buttonFab != null) {
                var join = Object.Instantiate(buttonFab, rect, false);
                join.name = "Join";
                join.GetComponentInChildren<TMP_Text>(true).text = "Join";
                var joinRect = (RectTransform)join.transform;
                joinRect.anchorMin = joinRect.anchorMax = new Vector2(1f, 0.5f);
                joinRect.pivot = new Vector2(1f, 0.5f);
                joinRect.sizeDelta = new Vector2(c_action_width, c_join_height);
                joinRect.anchoredPosition = new Vector2(-c_padding, 0f);
                join.SetActive(true);
                row.m_join = join.GetComponent<Button>();
            }
            return row;
        }

        /// <summary>Stretches <paramref name="rect"/> over part of the row's height, inset from its left and right ends.</summary>
        private static void Place(RectTransform rect, float minX, float minY, float maxX, float maxY, float left, float right) {
            rect.anchorMin = new Vector2(minX, minY);
            rect.anchorMax = new Vector2(maxX, maxY);
            rect.offsetMin = new Vector2(left, 0f);
            rect.offsetMax = new Vector2(right, 0f);
        }

        /// <summary>A column of the row's full height, <paramref name="width"/> wide, ending <paramref name="right"/> from its right end.</summary>
        private static void PlaceRight(RectTransform rect, float right, float width) {
            rect.anchorMin = new Vector2(1f, 0f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 0.5f);
            rect.sizeDelta = new Vector2(width, 0f);
            rect.anchoredPosition = new Vector2(-right, 0f);
        }

        public void Show(WKTLobbyListing listing, WKTLobbyBrowser browser, bool censor) {
            m_listing = listing;
            m_browser = browser;
            if (m_name != null)
                m_name.text = Censor(listing.Name, censor);
            if (m_members != null)
                m_members.text = FormatMembers(listing, censor);
            if (m_players != null)
                m_players.SetText("{0}/{1}", listing.MemberCount, listing.MaxMembers);
            if (m_ping != null)
                m_ping.text = listing.Ping == WKTLobbyListing.UNKNOWN_PING ? c_unknown_ping : WKTPings.Format(WKTPings.Clamp(listing.Ping));
            var joinable = listing.Status == WKTLobbyListingStatus.Joinable;
            if (m_status != null) {
                m_status.gameObject.SetActive(!joinable);
                m_status.text = FormatStatus(listing);
            }
            if (m_join != null)
                m_join.gameObject.SetActive(joinable);
            if (m_group != null)
                m_group.alpha = joinable ? 1f : c_unjoinable_alpha;
        }

        /// <summary>Joining is held back while another join is waiting for Steam.</summary>
        public void SetJoinInteractable(bool interactable) {
            if (m_join != null)
                m_join.interactable = interactable;
        }

        private void OnJoinClicked() {
            if (m_browser != null && m_listing != null)
                m_browser.Join(m_listing);
        }

        private static string Censor(string text, bool censor) => censor ? WKTChatFilter.Default.Censor(text) : text;

        private static string FormatMembers(WKTLobbyListing listing, bool censor) {
            s_sb.Clear();
            foreach (var member in listing.Members) {
                if (s_sb.Length > 0)
                    s_sb.Append(c_members_separator);
                s_sb.Append(Censor(member, censor));
            }
            var result = s_sb.ToString();
            s_sb.Clear();
            return result;
        }

        /// <summary>Why <paramref name="listing"/> cannot be joined, in a few words. Empty if it can.</summary>
        public static string FormatStatus(WKTLobbyListing listing) => listing.Status switch {
            WKTLobbyListingStatus.ModVersionMismatch => listing.ModVersion.Length > 0 ? "Needs v" + listing.ModVersion : "Other mod version",
            WKTLobbyListingStatus.GameVersionMismatch => "Other game version",
            WKTLobbyListingStatus.InRun => "In a run",
            WKTLobbyListingStatus.Full => "Full",
            _ => string.Empty
        };
    }
}
