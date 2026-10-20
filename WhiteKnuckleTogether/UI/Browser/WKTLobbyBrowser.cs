using System;
using System.Collections.Generic;
using JetBrains.Annotations;
using Steamworks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Lobby = Steamworks.Data.Lobby;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// The multiplayer menu's Join tab: the public lobbies of this mod, from Steam's lobby list. Lobbies that cannot be joined
    /// (another version, a run under way, full) are listed too, greyed out with the reason. Refreshes whenever the tab is
    /// shown, and on its Refresh button. A join code lobby is not listed, but is found by typing its code into the header.
    /// </summary>
    internal sealed class WKTLobbyBrowser : MonoBehaviour {
        /// <summary>Steam returns at most 50 lobbies per request.</summary>
        private const int c_max_results = 50;
        // Sizes are in the menu canvas's units (1280 by 720).
        private const float c_header_height = 40f;
        private const float c_list_top_gap = 6f;
        private const float c_refresh_width = 130f;
        private const float c_refresh_height = 34f;
        private const float c_join_code_width = 90f;
        private const float c_code_input_width = 100f;
        private const float c_status_left = 260f;
        private const float c_status_font_size = 16f;
        private const float c_fallback_title_font_size = 22f;
        private const float c_row_spacing = 6f;
        private const float c_scrollbar_width = 12f;
        /// <summary>How far one notch of the mouse wheel scrolls the list.</summary>
        private const float c_wheel_step = 40f;
        private static readonly Comparison<WKTLobbyListing> s_compare = WKTLobbyListing.Compare;
        private static readonly Color s_statusColor = new(0.7f, 0.7f, 0.7f, 1f);
        private readonly List<WKTLobbyListing> m_listings = new();
        private readonly List<WKTLobbyListing> m_listingPool = new();
        private readonly List<WKTLobbyBrowserRow> m_rows = new();
        [SerializeField] private WKTLobbyBrowserRow? m_rowTemplate;
        [SerializeField] private RectTransform? m_content;
        [SerializeField] private ScrollRect? m_scroll;
        [SerializeField] private TMP_Text? m_status;
        [SerializeField] private Button? m_refresh;
        [SerializeField] private TMP_InputField? m_codeInput;
        [SerializeField] private Button? m_joinCode;
        /// <summary>The lobby a join code found, reused from search to search.</summary>
        private readonly WKTLobbyListing m_codeListing = new();
        private Camera? m_eventCamera;
        private bool m_isRefreshing;
        private bool m_isFindingCode;
        private bool m_wasJoining;

        [UsedImplicitly]
        private void Awake() {
            if (m_refresh != null)
                m_refresh.onClick.AddListener(Refresh);
            if (m_joinCode != null)
                m_joinCode.onClick.AddListener(JoinByCode);
            // Enter in the code field joins too.
            if (m_codeInput != null)
                m_codeInput.onSubmit.AddListener(OnCodeSubmitted);
        }

        [UsedImplicitly]
        private void OnEnable() => Refresh();

        [UsedImplicitly]
        private void Start() {
            var canvas = GetComponentInParent<Canvas>();
            if (canvas != null && canvas.rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay)
                m_eventCamera = canvas.rootCanvas.worldCamera;
        }

        /// <summary>Join buttons wait while a join is waiting for Steam, so one click cannot start two.</summary>
        [UsedImplicitly]
        private void Update() {
            ScrollWithWheel();
            var joining = WKT.Instance.Network.IsJoiningLobby;
            if (joining == m_wasJoining) return;
            m_wasJoining = joining;
            foreach (var row in m_rows)
                row.SetJoinInteractable(!joining);
            if (m_joinCode != null)
                m_joinCode.interactable = !joining;
        }

        /// <summary>
        /// The game's UI input module does not pass the mouse wheel on to scroll views, so the list scrolls itself while the
        /// pointer is over it.
        /// </summary>
        private void ScrollWithWheel() {
            var wheel = Input.mouseScrollDelta.y;
            if (wheel == 0f || m_scroll == null || m_content == null || m_scroll.viewport == null) return;
            if (!RectTransformUtility.RectangleContainsScreenPoint((RectTransform)m_scroll.transform, Input.mousePosition, m_eventCamera)) return;
            var hidden = m_content.rect.height - m_scroll.viewport.rect.height;
            if (hidden <= 0f) return;
            m_scroll.verticalNormalizedPosition = Mathf.Clamp01(m_scroll.verticalNormalizedPosition + wheel * c_wheel_step / hidden);
        }

        /// <summary>
        /// Builds the browser into <paramref name="tab"/>: a header with the status and a Refresh button, over a scrolling list.
        /// The templates and styles come from the game's own interface; any that are missing fall back to plain ones.
        /// </summary>
        public static WKTLobbyBrowser Create(RectTransform tab, GameObject? buttonFab, GameObject? inputFab, Scrollbar? scrollbarTemplate,
            Image? backgroundStyle, TMP_Text? titleStyle) {
            var browser = tab.gameObject.AddComponent<WKTLobbyBrowser>();
            var panel = WKTUIBuild.CreateRect("Lobby Browser", tab);
            panel.anchorMin = Vector2.zero;
            panel.anchorMax = Vector2.one;
            panel.offsetMin = Vector2.zero;
            panel.offsetMax = Vector2.zero;
            CreateHeader(browser, panel, buttonFab, inputFab, titleStyle);
            CreateList(browser, panel, scrollbarTemplate, backgroundStyle);
            // Rows are cloned from a template kept under an inactive holder, so the template itself never wakes up.
            var templates = WKTUIBuild.CreateRect("Templates", panel);
            templates.gameObject.SetActive(false);
            browser.m_rowTemplate = WKTLobbyBrowserRow.CreateTemplate(templates, buttonFab);
            return browser;
        }

        private static void CreateHeader(WKTLobbyBrowser browser, RectTransform panel, GameObject? buttonFab, GameObject? inputFab,
            TMP_Text? titleStyle) {
            var header = WKTUIBuild.CreateRect("Header", panel);
            header.anchorMin = new Vector2(0f, 1f);
            header.anchorMax = new Vector2(1f, 1f);
            header.pivot = new Vector2(0.5f, 1f);
            header.sizeDelta = new Vector2(0f, c_header_height);
            var title = WKTUIBuild.CreateText("Title", header, c_fallback_title_font_size, Color.white, HorizontalAlignmentOptions.Left);
            if (titleStyle != null) {
                title.font = titleStyle.font;
                title.fontSharedMaterial = titleStyle.fontSharedMaterial;
                title.fontSize = titleStyle.fontSize;
                title.fontStyle = titleStyle.fontStyle;
                title.color = titleStyle.color;
                title.characterSpacing = titleStyle.characterSpacing;
            }
            title.text = "Public Lobbies";
            var titleRect = title.rectTransform;
            titleRect.anchorMin = Vector2.zero;
            titleRect.anchorMax = new Vector2(0f, 1f);
            titleRect.pivot = new Vector2(0f, 0.5f);
            titleRect.sizeDelta = new Vector2(c_status_left, 0f);
            var status = WKTUIBuild.CreateText("Status", header, c_status_font_size, s_statusColor, HorizontalAlignmentOptions.Left);
            var statusRect = status.rectTransform;
            statusRect.anchorMin = Vector2.zero;
            statusRect.anchorMax = Vector2.one;
            statusRect.offsetMin = new Vector2(c_status_left, 0f);
            statusRect.offsetMax = new Vector2(-(c_refresh_width + c_join_code_width + c_code_input_width + c_row_spacing * 3f), 0f);
            browser.m_status = status;
            // From the right: Refresh, then the join code field and its Join button.
            browser.m_refresh = CreateHeaderButton(header, buttonFab, "Refresh", c_refresh_width, 0f);
            var right = c_refresh_width + c_row_spacing;
            browser.m_joinCode = CreateHeaderButton(header, buttonFab, "Join", c_join_code_width, right);
            right += c_join_code_width + c_row_spacing;
            if (inputFab == null) return;
            var input = Instantiate(inputFab, header, false).GetComponent<TMP_InputField>();
            input.name = "Join Code";
            input.contentType = TMP_InputField.ContentType.Custom;
            input.characterValidation = TMP_InputField.CharacterValidation.Digit;
            input.characterLimit = WKTLobbyConsts.JOIN_CODE_LENGTH;
            if (input.placeholder is TMP_Text placeholder)
                placeholder.text = "Code";
            PlaceRight((RectTransform)input.transform, right, c_code_input_width);
            input.gameObject.SetActive(true);
            browser.m_codeInput = input;
        }

        /// <summary>A button of the header, <paramref name="width"/> wide, ending <paramref name="right"/> from the header's right end.</summary>
        private static Button? CreateHeaderButton(RectTransform header, GameObject? buttonFab, string label, float width, float right) {
            if (buttonFab == null) return null;
            var button = Instantiate(buttonFab, header, false);
            button.name = label;
            button.GetComponentInChildren<TMP_Text>(true).text = label;
            PlaceRight((RectTransform)button.transform, right, width);
            button.SetActive(true);
            return button.GetComponent<Button>();
        }

        private static void PlaceRight(RectTransform rect, float right, float width) {
            rect.anchorMin = rect.anchorMax = new Vector2(1f, 0.5f);
            rect.pivot = new Vector2(1f, 0.5f);
            rect.sizeDelta = new Vector2(width, c_refresh_height);
            rect.anchoredPosition = new Vector2(-right, 0f);
        }

        private static void CreateList(WKTLobbyBrowser browser, RectTransform panel, Scrollbar? scrollbarTemplate, Image? backgroundStyle) {
            var list = WKTUIBuild.CreateRect("List", panel);
            list.anchorMin = Vector2.zero;
            list.anchorMax = Vector2.one;
            list.offsetMin = Vector2.zero;
            list.offsetMax = new Vector2(0f, -(c_header_height + c_list_top_gap));
            var background = list.gameObject.AddComponent<Image>();
            WKTUIBuild.CopyBackground(background, backgroundStyle);
            var scroll = list.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            // The wheel is handled by ScrollWithWheel, so a module that does pass it on cannot scroll twice.
            scroll.scrollSensitivity = 0f;
            var viewport = WKTUIBuild.CreateRect("Viewport", list);
            viewport.anchorMin = Vector2.zero;
            viewport.anchorMax = Vector2.one;
            viewport.offsetMin = Vector2.zero;
            viewport.offsetMax = new Vector2(-c_scrollbar_width, 0f);
            viewport.gameObject.AddComponent<RectMask2D>();
            // Catches the scroll wheel between and below the rows.
            viewport.gameObject.AddComponent<Image>().color = Color.clear;
            var content = WKTUIBuild.CreateRect("Content", viewport);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.sizeDelta = Vector2.zero;
            var layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            const int padding = (int)c_row_spacing;
            layout.padding = new RectOffset(padding, padding, padding, padding);
            layout.spacing = c_row_spacing;
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlWidth = true;
            layout.childForceExpandWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.viewport = viewport;
            scroll.content = content;
            if (scrollbarTemplate != null) {
                var scrollbar = Instantiate(scrollbarTemplate, list, false);
                scrollbar.name = "Scrollbar";
                scrollbar.onValueChanged = new Scrollbar.ScrollEvent();
                scrollbar.direction = Scrollbar.Direction.BottomToTop;
                var scrollbarRect = (RectTransform)scrollbar.transform;
                scrollbarRect.anchorMin = new Vector2(1f, 0f);
                scrollbarRect.anchorMax = Vector2.one;
                scrollbarRect.pivot = new Vector2(1f, 0.5f);
                scrollbarRect.sizeDelta = new Vector2(c_scrollbar_width, 0f);
                scrollbarRect.anchoredPosition = Vector2.zero;
                scrollbar.gameObject.SetActive(true);
                scroll.verticalScrollbar = scrollbar;
                scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
            }
            browser.m_content = content;
            browser.m_scroll = scroll;
        }

        /// <summary>Asks Steam for the public lobbies again. Ignored while a request is under way.</summary>
        public async void Refresh() {
            if (m_isRefreshing) return;
            m_isRefreshing = true;
            SetStatus("Searching...");
            if (m_refresh != null)
                m_refresh.interactable = false;
            Lobby[]? lobbies = null;
            var failed = false;
            try {
                lobbies = await SteamMatchmaking.LobbyList
                    .FilterDistanceWorldwide()
                    .WithKeyValue(WKTLobbyConsts.KEY_MOD, WKTLobbyConsts.MOD_ID)
                    .WithKeyValue(WKTLobbyConsts.KEY_LISTED, "1")
                    .WithMaxResults(c_max_results)
                    .RequestAsync();
            }
            catch (Exception e) {
                WKT.Instance.Logger.LogError($"Failed to list public lobbies: {e}");
                failed = true;
            }
            // The menu may have gone away while Steam answered.
            if (this == null) return;
            m_isRefreshing = false;
            if (m_refresh != null)
                m_refresh.interactable = true;
            if (failed) {
                SetStatus("Could not reach Steam.");
                ShowListings();
                return;
            }
            ReadListings(lobbies);
            ShowListings();
            SetStatus(m_listings.Count switch {
                0 => "No public lobbies found.",
                1 => "1 lobby",
                _ => m_listings.Count + " lobbies"
            });
        }

        /// <summary>Joins the lobby of <paramref name="listing"/> if it can be joined.</summary>
        public void Join(WKTLobbyListing listing) {
            if (listing.Status != WKTLobbyListingStatus.Joinable) return;
            WKT.Instance.Logger.LogInfo($"Joining \"{listing.Name}\" from the lobby browser.");
            WKT.Instance.Network.JoinListedLobby(listing.Id);
        }

        private void OnCodeSubmitted(string _) => JoinByCode();

        /// <summary>Finds the lobby with the code typed into the code field, and joins it if it can be joined.</summary>
        private async void JoinByCode() {
            if (m_isFindingCode || m_codeInput == null) return;
            if (!WKTLobbyData.TryParseJoinCode(m_codeInput.text, out var code)) {
                SetStatus($"A lobby code has {WKTLobbyConsts.JOIN_CODE_LENGTH} digits.");
                return;
            }
            m_isFindingCode = true;
            SetStatus($"Looking for lobby {code}...");
            Lobby? lobby = null;
            var failed = false;
            try {
                lobby = await WKTNetwork.FindJoinCodeLobbyAsync(code);
            }
            catch (Exception e) {
                WKT.Instance.Logger.LogError($"Failed to look for join code {code}: {e}");
                failed = true;
            }
            // The menu may have gone away while Steam answered.
            if (this == null) return;
            m_isFindingCode = false;
            if (failed) {
                SetStatus("Could not reach Steam.");
                return;
            }
            if (!lobby.HasValue) {
                SetStatus($"No lobby has the code {code}.");
                return;
            }
            m_codeListing.Read(lobby.Value, WKTPlugin.VERSION, WKT.Instance.Network.GameVersion);
            if (m_codeListing.Status != WKTLobbyListingStatus.Joinable) {
                SetStatus($"Lobby {code} cannot be joined: {WKTLobbyBrowserRow.FormatStatus(m_codeListing)}.");
                return;
            }
            SetStatus($"Joining lobby {code}...");
            Join(m_codeListing);
        }

        private void ReadListings(Lobby[]? lobbies) {
            m_listingPool.AddRange(m_listings);
            m_listings.Clear();
            if (lobbies == null) return;
            var network = WKT.Instance.Network;
            foreach (var lobby in lobbies) {
                WKTLobbyListing listing;
                if (m_listingPool.Count > 0) {
                    listing = m_listingPool[m_listingPool.Count - 1];
                    m_listingPool.RemoveAt(m_listingPool.Count - 1);
                }
                else
                    listing = new WKTLobbyListing();
                listing.Read(lobby, WKTPlugin.VERSION, network.GameVersion);
                m_listings.Add(listing);
            }
            m_listings.Sort(s_compare);
        }

        /// <summary>Shows a row per listing, reusing the rows already made, and scrolls back to the top.</summary>
        private void ShowListings() {
            if (m_content == null || m_rowTemplate == null) return;
            var config = WKTPlugin.ModConfig;
            var censor = config != null && config.CensorChat.Value;
            var joining = WKT.Instance.Network.IsJoiningLobby;
            for (var i = 0; i < m_listings.Count; i++) {
                if (i == m_rows.Count) {
                    var row = Instantiate(m_rowTemplate, m_content, false);
                    row.gameObject.SetActive(true);
                    m_rows.Add(row);
                }
                m_rows[i].gameObject.SetActive(true);
                m_rows[i].Show(m_listings[i], this, censor);
                m_rows[i].SetJoinInteractable(!joining);
            }
            for (var i = m_listings.Count; i < m_rows.Count; i++)
                m_rows[i].gameObject.SetActive(false);
            m_wasJoining = joining;
            if (m_scroll != null)
                m_scroll.verticalNormalizedPosition = 1f;
        }

        private void SetStatus(string text) {
            if (m_status != null)
                m_status.text = text;
        }
    }
}
