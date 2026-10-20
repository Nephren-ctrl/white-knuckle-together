using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BepInEx;
using DarkMachine.UI;
using JetBrains.Annotations;
using Steamworks;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace WhiteKnuckleTogether {
    public sealed class WKTMainMenuUI : MonoBehaviour {
        private const string c_multiplayer_campaign_panel = "multiplayer-campaign";
        /// <summary>The game's competitive <c>GamemodeSetting.id</c> (<c>M_Gamemode.IsCompetitive</c>).</summary>
        private const string c_competitive_setting = "competitive";
        private const string c_screens = "Screens";
        private const string c_main_menu_canvas = "Canvas - Main Menu";
        private const string c_play_game_button = "Main Menu/Main Menu Buttons/Play Game";
        /// <summary>The seed field of the main menu's seed popup, the template for the mod's text fields.</summary>
        private const string c_seed_input = "Main Menu/Seed-Popup/Seed Input";
        private const string c_play_screen = "Canvas - Screen - Play";
        private const string c_play_pane = c_play_screen + "/Play Menu/Play Pane";
        private const string c_play_gamemode_screen = c_play_screen + "/Play Menu/GamemodeScreen";
        private const string c_logbook = "Canvas - Screen - Other/Logbook";
        private const string c_settings_menu = "Canvas - Screen - Settings/Settings Menu";
        private const string c_settings_video = "SettingsParent/Settings Pane/Video Settings/Main Panel/Tab - Video/Column - Video";
        private const string c_main_tab = "Play Pane - Scroll View Tab - Main";
        private const string c_main_tab_content = c_main_tab + "/Viewport/Content";
        private const string c_campaign_button = c_main_tab_content + "/Mode Selection Button - Campaign Variant";
        private const string c_panel_info_screen = "Pages/Gamemode_Info_Screen";
        private const string c_panel_new_game_tabs = "Pages/Gamemode_NewGame_Screen/Tab Selection Hor";
        private const string c_lobby_player_fab = "LobbyPlayerFab";
        // Keeps the lobby content below the gamemode title, which sits at the top of the Mode Page.
        private const float c_board_top_inset = 120f;
        private const float c_board_side_margin = 50f;
        /// <summary>The settings section's narrowest width; it takes whatever the player list leaves on the lobby board.</summary>
        private const float c_settings_width = 600f;
        /// <summary>Room for the avatar, the name, the host's KICK and BAN buttons, the voice controls and the tag (see <see cref="WKTLobbyPlayer"/>).</summary>
        private const float c_player_list_width = 700f;
        /// <summary>The least room between the settings and the player list.</summary>
        private const float c_board_gap = 60f;
        private const float c_section_padding = 16f;
        /// <summary>Lobby modifier toggles sit in this many columns, so the settings fit above the lobby's buttons.</summary>
        private const int c_modifier_columns = 3;
        /// <summary>Lobby dropdowns sit side by side in this many columns, so the settings fit above the lobby's buttons.</summary>
        private const int c_dropdown_columns = 4;
        private const float c_fallback_toggle_height = 30f;
        private const float c_popup_width = 420f;
        private const float c_popup_backdrop_alpha = 0.6f;
        private const float c_section_spacing = 10f;
        private const float c_section_header_height = 50f;
        private const float c_fallback_row_height = 62f;
        private const float c_fallback_header_font_size = 32f;
        /// <summary>Where the Join tab sits in the Play pane (1280 by 550): below the tab buttons, above Close, clear of the facility badge.</summary>
        private const float c_join_tab_top_inset = 85f;
        private const float c_join_tab_bottom_inset = 85f;
        private const float c_join_tab_side_inset = 100f;
        private static readonly List<Component> s_componentCache = new();
        private static readonly List<GameObject> s_rootCache = new();
        private static readonly List<string> s_optionCache = new();
        private static readonly StringBuilder s_sb = new();
        private GameObject? m_screens;
        private Button? m_playGameButton;
        private Transform? m_templateRoot;
        private GameObject? m_playPane;
        private GameObject? m_playTabs;
        private GameObject? m_playTabObjects;
        private GameObject? m_playModifierList;
        private GameObject? m_playMutators;
        private UI_GamemodeScreen? m_playGamemodeScreen;
        private GameObject? m_multiplayerTabs;
        private GameObject? m_multiplayerTabObjects;
        private GameObject? m_multiplayerModifierList;
        private GameObject? m_multiplayerLobbyVisibility;
        private GameObject? m_multiplayerLobbyName;
        /// <summary>The Host tab's gamemodes. The host options only show while it is the open tab.</summary>
        private GameObject? m_hostTabObject;
        private GameObject? m_buttonFab;
        private GameObject? m_dropdownFab;
        private GameObject? m_toggleFab;
        private GameObject? m_sliderFab;
        private GameObject? m_inputFab;
        private GameObject? m_lobbyPlayerFab;
        private TMP_FontAsset? m_standardFont;
        private TMP_Text? m_sectionHeaderStyle;
        private Image? m_sectionBackgroundStyle;
        private Button? m_multiplayerToggleButton;
        private TMP_Text? m_multiplayerToggleButtonText;
        private M_Gamemode? m_multiplayerCampaignGamemode;
        private bool m_multiplayerShown;

        [UsedImplicitly]
        private void Awake() {
            WKT.Instance.Logger.LogInfo("Initializing main menu UI.");
            WKT.Instance.Network.OnClientStarted += OnNetworkClientStarted;
            WKT.Instance.Network.OnPlayerConnected += OnNetworkPlayerConnected;
            WKT.Instance.Network.OnPlayerDisconnected += OnNetworkPlayerDisconnected;
            WKT.Instance.Network.OnNetworkStopping += OnNetworkStopping;
            WKT.Instance.Network.OnHostChanged += OnNetworkHostChanged;
            if (!TryInitializeScreens()) {
                WKT.Instance.Logger.LogError("Main menu multiplayer UI is unavailable. Screens could not be found.");
                return;
            }
            // Each stage is isolated so a game update breaking one part of the UI cannot take down the others or
            // the vanilla main menu.
            RunStage("find main menu references", FindMainMenuReferences);
            RunStage("initialize multiplayer gamemodes", InitializeGamemodes);
            RunStage("inject multiplayer UI", InjectMultiplayerUI);
            // Dumped last so the generated templates are included.
            DumpHierarchyIfEnabled();
            WKT.Instance.Logger.LogInfo("Finished initializing main menu UI.");
            RunStage("restore the active session", RestoreActiveSession);
            // A session that ended mid-run is explained once the menu it returned to is up.
            ShowSessionEndReason();
        }

        [UsedImplicitly]
        private void OnDestroy() {
            WKTSteamAvatarCache.Clear();
            if (WKT.InstanceOrNull == null) return;
            WKT.InstanceOrNull.Network.OnClientStarted -= OnNetworkClientStarted;
            WKT.InstanceOrNull.Network.OnPlayerConnected -= OnNetworkPlayerConnected;
            WKT.InstanceOrNull.Network.OnPlayerDisconnected -= OnNetworkPlayerDisconnected;
            WKT.InstanceOrNull.Network.OnNetworkStopping -= OnNetworkStopping;
            WKT.InstanceOrNull.Network.OnHostChanged -= OnNetworkHostChanged;
        }

        private static void RunStage(string stageName, Action stage) {
            try {
                stage();
            }
            catch (Exception e) {
                WKT.Instance.Logger.LogError($"Failed to {stageName}: {e}");
            }
        }

        private static void SetActive(GameObject? obj, bool active) {
            if (obj != null)
                obj.SetActive(active);
        }

        private static void StretchToParent(RectTransform rect) {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(.5f, .5f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static void DumpHierarchyIfEnabled() {
            var config = WKTPlugin.ModConfig;
            if (config == null || !config.DumpMainMenuHierarchy.Value) return;
            try {
                SceneManager.GetActiveScene().GetRootGameObjects(s_rootCache);
                s_sb.Clear();
                foreach (var root in s_rootCache)
                    WKTUIPath.AppendHierarchy(root.transform, s_sb);
                s_rootCache.Clear();
                var path = Path.Combine(Paths.BepInExRootPath, WKTConfig.MAIN_MENU_HIERARCHY_FILE);
                File.WriteAllText(path, s_sb.ToString());
                s_sb.Clear();
                WKT.Instance.Logger.LogInfo($"Wrote main menu hierarchy to {path}.");
            }
            catch (Exception e) {
                WKT.Instance.Logger.LogError($"Failed to dump main menu hierarchy: {e}");
            }
        }

        private bool TryInitializeScreens() {
            m_screens = GameObject.Find(c_screens);
            if (m_screens == null) return false;
            // Templates live under an inactive root so cloning them never runs Awake / OnEnable. Game components
            // like UI_GamemodeOptionsText throw when enabled outside of the game's own setup flow.
            var templateRoot = new GameObject("WKT Templates", typeof(RectTransform));
            templateRoot.SetActive(false);
            m_templateRoot = templateRoot.transform;
            m_templateRoot.SetParent(m_screens.transform, false);
            return true;
        }

        /// <summary>
        /// Clones <paramref name="source"/> into the inactive template root and strips game setting binders.
        /// </summary>
        private GameObject CreateTemplate(GameObject source, string templateName) {
            var template = Instantiate(source, m_templateRoot, false);
            template.name = templateName;
            WKTSettingsRowCloner.StripBinders(template);
            return template;
        }

        /// <summary>
        /// Clones a template while still inert so it can be configured before its components wake up. Call
        /// <see cref="Attach"/> afterwards.
        /// </summary>
        private GameObject? Spawn(GameObject? template, string templateName) {
            if (template != null) return Instantiate(template, m_templateRoot, false);
            WKT.Instance.Logger.LogError($"Failed to create {templateName}. Template is unavailable.");
            return null;
        }

        private static void Attach(GameObject obj, Transform parent) {
            obj.transform.SetParent(parent, false);
            obj.SetActive(true);
        }

        private Button? CreateButton(string text, Transform parent) {
            var obj = Spawn(m_buttonFab, "button");
            if (obj == null) return null;
            obj.GetComponentInChildren<TMP_Text>(true).text = text;
            Attach(obj, parent);
            return obj.GetComponent<Button>();
        }

        private Toggle? CreateToggle(string text, Transform parent) {
            var obj = Spawn(m_toggleFab, "toggle");
            if (obj == null) return null;
            obj.GetComponentInChildren<TMP_Text>(true).text = text;
            Attach(obj, parent);
            return obj.GetComponent<Toggle>();
        }

        private TMP_Dropdown? CreateDropdown(string text, Transform parent, params string[] options) {
            var obj = Spawn(m_dropdownFab, "dropdown");
            if (obj == null) return null;
            obj.GetComponent<TMP_Text>().text = text;
            var dropdown = obj.GetComponentInChildren<TMP_Dropdown>(true);
            dropdown.ClearOptions();
            s_optionCache.AddRange(options);
            dropdown.AddOptions(s_optionCache);
            s_optionCache.Clear();
            Attach(obj, parent);
            return dropdown;
        }

        private WKTLobbyPlayer? CreateLobbyPlayer(Transform parent) {
            var obj = Spawn(m_lobbyPlayerFab, "lobby player");
            if (obj == null) return null;
            Attach(obj, parent);
            return obj.GetComponent<WKTLobbyPlayer>();
        }

        private void FindMainMenuReferences() {
            var screens = m_screens!.transform;
            if (WKTUIPath.TryFind(screens, c_play_pane, out var playPane)) {
                m_playPane = playPane.gameObject;
                if (WKTUIPath.TryFind(playPane, "Tabs", out var tabs))
                    m_playTabs = tabs.gameObject;
                if (WKTUIPath.TryFind(playPane, "Tab Objects", out var tabObjects))
                    m_playTabObjects = tabObjects.gameObject;
                if (WKTUIPath.TryFind(playPane, "Modifier List", out var modifierList))
                    m_playModifierList = modifierList.gameObject;
                if (WKTUIPath.TryFind(playPane, "Mutators", out var mutators))
                    m_playMutators = mutators.gameObject;
            }
            var mainMenuCanvas = GameObject.Find(c_main_menu_canvas);
            if (mainMenuCanvas != null) {
                if (WKTUIPath.TryFind<Button>(mainMenuCanvas.transform, c_play_game_button, out var playGameButton, true))
                    m_playGameButton = playGameButton;
                InitializeInputTemplate(mainMenuCanvas.transform);
            }
            m_playGamemodeScreen = UI_GamemodeScreen.instance;
            if (m_playGamemodeScreen == null && WKTUIPath.TryFind<UI_GamemodeScreen>(screens, c_play_gamemode_screen, out var gamemodeScreen, true))
                m_playGamemodeScreen = gamemodeScreen;
            if (WKTUIPath.TryFind(screens, c_logbook, out var logbook)) {
                if (WKTUIPath.TryFind<TMP_Text>(logbook, "Stat Page/Board/Stats/Stats/Scoregrid-Main/stat-score", out var fontSource, true))
                    m_standardFont = fontSource.font;
                InitializeButtonTemplate(logbook);
            }
            if (WKTUIPath.TryFind(screens, c_settings_menu, out var settingsMenu)) {
                InitializeDropdownTemplate(settingsMenu);
                InitializeToggleTemplate(settingsMenu);
                InitializeSliderTemplate(settingsMenu);
            }
            // The mod's text fields sit beside dropdowns, so they look like them.
            if (m_inputFab != null && m_dropdownFab != null)
                MatchDropdownStyle(m_inputFab.GetComponent<TMP_InputField>(), m_dropdownFab.GetComponentInChildren<TMP_Dropdown>(true));
            LogReferenceSummary();
        }

        private void InitializeButtonTemplate(Transform logbook) {
            if (!WKTUIPath.TryFind<Button>(logbook, "Tab Selection Hor/Stats", out var source, true)) return;
            m_buttonFab = CreateTemplate(source.gameObject, "ButtonFab");
            // Replacing the event also drops persistent (inspector assigned) listeners, which RemoveAllListeners keeps.
            m_buttonFab.GetComponent<Button>().onClick = new Button.ButtonClickedEvent();
        }

        private void InitializeInputTemplate(Transform mainMenuCanvas) {
            if (!WKTUIPath.TryFind<TMP_InputField>(mainMenuCanvas, c_seed_input, out var source, true)) return;
            m_inputFab = CreateTemplate(source.gameObject, "InputFab");
            var input = m_inputFab.GetComponent<TMP_InputField>();
            // The seed field's own listeners (and its digits only rule) belong to the seed popup.
            input.onValueChanged = new TMP_InputField.OnChangeEvent();
            input.onEndEdit = new TMP_InputField.SubmitEvent();
            input.onSubmit = new TMP_InputField.SubmitEvent();
            input.onSelect = new TMP_InputField.SelectionEvent();
            input.onDeselect = new TMP_InputField.SelectionEvent();
            input.contentType = TMP_InputField.ContentType.Standard;
            input.lineType = TMP_InputField.LineType.SingleLine;
            input.SetTextWithoutNotify(string.Empty);
        }

        private void InitializeDropdownTemplate(Transform settingsMenu) {
            // The resolution dropdown is the only Settings_Resolution, which is more stable than its path.
            var resolution = settingsMenu.GetComponentInChildren<Settings_Resolution>(true);
            Transform source;
            if (resolution != null)
                source = resolution.transform;
            else if (!WKTUIPath.TryFind(settingsMenu, c_settings_video + "/Screen Resolution", out source, true))
                return;
            m_dropdownFab = CreateTemplate(source.gameObject, "DropdownFab");
            m_dropdownFab.GetComponentInChildren<TMP_Dropdown>(true).onValueChanged = new TMP_Dropdown.DropdownEvent();
        }

        private void InitializeToggleTemplate(Transform settingsMenu) {
            if (!WKTUIPath.TryFind<Toggle>(settingsMenu, c_settings_video + "/Fullscreen Toggle", out var source, true)) return;
            m_toggleFab = CreateTemplate(source.gameObject, "ToggleFab");
            var toggle = m_toggleFab.GetComponent<Toggle>();
            toggle.onValueChanged = new Toggle.ToggleEvent();
            toggle.SetIsOnWithoutNotify(false);
        }

        private void InitializeSliderTemplate(Transform settingsMenu) {
            if (!WKTUIPath.TryFind(settingsMenu, c_settings_video + "/SliderAsset - Brightness", out var source, true)) return;
            m_sliderFab = CreateTemplate(source.gameObject, "SliderFab");
            var slider = m_sliderFab.GetComponentInChildren<SubmitSlider>(true);
            if (slider == null) {
                WKT.Instance.Logger.LogError("Slider template has no SubmitSlider component.");
                Destroy(m_sliderFab);
                m_sliderFab = null;
                return;
            }
            slider.onValueChanged = new SubmitSlider.SliderEvent();
            slider.onFinishChange = new SubmitSlider.SliderEvent();
            slider.minValue = 0f;
            slider.maxValue = 1f;
        }

        private void LogReferenceSummary() {
            const int reference_count = 13;
            s_sb.Clear();
            var found = 0;
            found += AppendIfMissing(m_playPane, "Play Pane");
            found += AppendIfMissing(m_playTabs, "Tabs");
            found += AppendIfMissing(m_playTabObjects, "Tab Objects");
            found += AppendIfMissing(m_playModifierList, "Modifier List");
            found += AppendIfMissing(m_playMutators, "Mutators");
            found += AppendIfMissing(m_playGamemodeScreen, "Gamemode Screen");
            found += AppendIfMissing(m_playGameButton, "Play Game Button");
            found += AppendIfMissing(m_standardFont, "Font");
            found += AppendIfMissing(m_buttonFab, "Button");
            found += AppendIfMissing(m_dropdownFab, "Dropdown");
            found += AppendIfMissing(m_toggleFab, "Toggle");
            found += AppendIfMissing(m_sliderFab, "Slider");
            found += AppendIfMissing(m_inputFab, "Input");
            if (found == reference_count)
                WKT.Instance.Logger.LogInfo($"Main menu UI: {found}/{reference_count} references OK.");
            else
                WKT.Instance.Logger.LogError($"Main menu UI: {found}/{reference_count} references OK. Missing: {s_sb}.");
            s_sb.Clear();
        }

        private static int AppendIfMissing(Object? reference, string referenceName) {
            if (reference != null) return 1;
            if (s_sb.Length > 0)
                s_sb.Append(", ");
            s_sb.Append(referenceName);
            return 0;
        }

        private static VerticalLayoutGroup AddVerticalLayout(GameObject obj, bool controlChildHeight, float spacing) {
            var layout = obj.AddComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlWidth = true;
            layout.childForceExpandWidth = true;
            layout.childControlHeight = controlChildHeight;
            layout.childForceExpandHeight = false;
            layout.spacing = spacing;
            return layout;
        }

        /// <summary>
        /// Hides the vanilla board widgets (stats, leaderboard controls) while keeping anything that shows the
        /// gamemode title.
        /// </summary>
        private static void HideVanillaBoardChildren(Transform board) {
            for (var i = 0; i < board.childCount; i++) {
                var child = board.GetChild(i);
                if (child.GetComponentInChildren<UI_GamemodeText>(true) == null)
                    child.gameObject.SetActive(false);
            }
        }

        /// <summary>
        /// Removes the vanilla leaderboard without breaking the game, which calls Refresh on it unconditionally and
        /// lets Refresh toggle its own active state. Parenting it under an inactive holder keeps it hidden regardless.
        /// </summary>
        private void HideLeaderboard(UI_GamemodeScreen_Panel panel) {
            var leaderboard = panel.leaderboard;
            if (leaderboard == null) {
                WKT.Instance.Logger.LogWarning("Multiplayer campaign panel has no leaderboard to hide.");
                return;
            }
            leaderboard.refreshOnEnable = false;
            leaderboard.gameObject.AddComponent<WKTDisabledLeaderboard>();
            m_sectionHeaderStyle = leaderboard.title;
            // The leaderboard image only acts as a mask when its graphic is hidden, so it is no visible style source.
            var mask = leaderboard.GetComponent<Mask>();
            if (mask == null || mask.showMaskGraphic)
                m_sectionBackgroundStyle = leaderboard.GetComponent<Image>();
            var holder = WKTUIBuild.CreateRect("WKT Hidden", panel.transform);
            holder.gameObject.SetActive(false);
            leaderboard.transform.SetParent(holder, false);
        }

        private void CreateLobbyPlayerFab(Leaderboard_Score? scorePrefab) {
            if (scorePrefab != null) {
                m_lobbyPlayerFab = CreateTemplate(scorePrefab.gameObject, c_lobby_player_fab);
                var score = m_lobbyPlayerFab.GetComponent<Leaderboard_Score>();
                var nameText = score.nameText;
                var positionText = score.positionText;
                var scoreText = score.scoreText;
                var profile = score.profile;
                // The row opens leaderboard entry details when clicked, which is meaningless for a lobby player.
                DestroyImmediate(score);
                // Iron Knuckle / Hard Mode run badges.
                WKTUIPath.DestroyIfFound(m_lobbyPlayerFab.transform, "BubbleDetails");
                m_lobbyPlayerFab.GetComponentsInChildren(true, s_componentCache);
                foreach (var component in s_componentCache) {
                    if (component is Button button)
                        button.onClick = new Button.ButtonClickedEvent();
                }
                s_componentCache.Clear();
                if (nameText != null) {
                    m_lobbyPlayerFab.AddComponent<WKTLobbyPlayer>().SetupReferences(profile, nameText, positionText, scoreText);
                    return;
                }
                WKT.Instance.Logger.LogWarning("Leaderboard row has no name text. Falling back to a basic lobby row.");
                Destroy(m_lobbyPlayerFab);
            }
            CreateFallbackLobbyPlayerFab();
        }

        private void CreateFallbackLobbyPlayerFab() {
            var rowRect = WKTUIBuild.CreateRect(c_lobby_player_fab, m_templateRoot!);
            rowRect.sizeDelta = new Vector2(0f, c_fallback_row_height);
            m_lobbyPlayerFab = rowRect.gameObject;
            var avatarRect = WKTUIBuild.CreateRect("Avatar", rowRect);
            avatarRect.anchorMin = new Vector2(0f, .5f);
            avatarRect.anchorMax = new Vector2(0f, .5f);
            avatarRect.pivot = new Vector2(0f, .5f);
            avatarRect.sizeDelta = new Vector2(c_fallback_row_height - 12f, c_fallback_row_height - 12f);
            avatarRect.anchoredPosition = new Vector2(8f, 0f);
            var avatar = avatarRect.gameObject.AddComponent<RawImage>();
            var nameRect = WKTUIBuild.CreateRect("Name", rowRect);
            nameRect.anchorMin = Vector2.zero;
            nameRect.anchorMax = Vector2.one;
            nameRect.offsetMin = new Vector2(c_fallback_row_height + 8f, 0f);
            nameRect.offsetMax = new Vector2(-8f, 0f);
            var nameText = nameRect.gameObject.AddComponent<TextMeshProUGUI>();
            nameText.horizontalAlignment = HorizontalAlignmentOptions.Left;
            nameText.verticalAlignment = VerticalAlignmentOptions.Middle;
            nameText.textWrappingMode = TextWrappingModes.NoWrap;
            nameText.overflowMode = TextOverflowModes.Ellipsis;
            nameText.fontSize = 28f;
            if (m_standardFont != null)
                nameText.font = m_standardFont;
            var tagText = Instantiate(nameText, rowRect, false);
            tagText.name = "Tag";
            tagText.horizontalAlignment = HorizontalAlignmentOptions.Right;
            rowRect.gameObject.AddComponent<WKTLobbyPlayer>().SetupReferences(avatar, nameText, null, tagText);
        }

        private TMP_Text CreateSectionHeader(string text, Transform parent) {
            var rect = WKTUIBuild.CreateRect("Header", parent);
            rect.gameObject.AddComponent<LayoutElement>().preferredHeight = c_section_header_height;
            var header = rect.gameObject.AddComponent<TextMeshProUGUI>();
            header.text = text;
            header.horizontalAlignment = HorizontalAlignmentOptions.Left;
            header.verticalAlignment = VerticalAlignmentOptions.Middle;
            header.textWrappingMode = TextWrappingModes.NoWrap;
            if (m_sectionHeaderStyle != null) {
                header.font = m_sectionHeaderStyle.font;
                header.fontSharedMaterial = m_sectionHeaderStyle.fontSharedMaterial;
                header.fontSize = m_sectionHeaderStyle.fontSize;
                header.fontStyle = m_sectionHeaderStyle.fontStyle;
                header.color = m_sectionHeaderStyle.color;
                header.characterSpacing = m_sectionHeaderStyle.characterSpacing;
            }
            else {
                if (m_standardFont != null)
                    header.font = m_standardFont;
                header.fontSize = c_fallback_header_font_size;
            }
            return header;
        }

        /// <summary>
        /// Creates a boxed lobby section in <paramref name="board"/>'s row that grows with its content: at least
        /// <paramref name="width"/> wide, and wider if <paramref name="flexible"/>. Returns the container that section entries
        /// go into.
        /// </summary>
        private RectTransform CreateLobbySection(string objectName, string title, RectTransform board, float width, bool flexible,
            out TMP_Text header) {
            var section = WKTUIBuild.CreateRect(objectName, board);
            // Grows downwards as its content fills in, so both sections keep their tops level.
            section.pivot = new Vector2(0.5f, 1f);
            var element = section.gameObject.AddComponent<LayoutElement>();
            element.minWidth = width;
            element.preferredWidth = width;
            element.flexibleWidth = flexible ? 1f : 0f;
            var background = section.gameObject.AddComponent<Image>();
            if (m_sectionBackgroundStyle != null) {
                background.sprite = m_sectionBackgroundStyle.sprite;
                background.type = m_sectionBackgroundStyle.type;
                background.color = m_sectionBackgroundStyle.color;
                background.pixelsPerUnitMultiplier = m_sectionBackgroundStyle.pixelsPerUnitMultiplier;
            }
            else
                background.color = new Color(0f, 0f, 0f, .35f);
            background.raycastTarget = false;
            const int padding = (int)c_section_padding;
            AddVerticalLayout(section.gameObject, true, c_section_spacing).padding = new RectOffset(padding, padding, padding, padding);
            section.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            header = CreateSectionHeader(title, section);
            // Entries keep their own heights, so the content container sums them instead of resizing them.
            var content = WKTUIBuild.CreateRect("Content", section);
            AddVerticalLayout(content.gameObject, false, c_section_spacing);
            return content;
        }

        private UI_GamemodeScreen_Panel? CreateMultiplayerCampaignPanel(UI_GamemodeScreen_Panel? originalPanel) {
            if (originalPanel == null) {
                WKT.Instance.Logger.LogError("Failed to create multiplayer campaign panel. Original panel is null.");
                return null;
            }
            // The game instantiates and enables this template itself in UI_GamemodeScreen.Initialize, so it must keep
            // the original's active state and only stay inert through the template root.
            var cloneObj = CreateTemplate(originalPanel.gameObject, "Gamemode_Panel_Multiplayer_Campaign");
            var panel = cloneObj.GetComponent<UI_GamemodeScreen_Panel>();
            panel.openEvent = new UnityEvent();
            panel.closeEvent = new UnityEvent();
            panel.hasSaveObjects.Clear();
            panel.noSaveObjects.Clear();
            panel.id = c_multiplayer_campaign_panel;
            // The cleared open event is what set the pickers up for the vanilla panel.
            foreach (var picker in cloneObj.GetComponentsInChildren<UI_TrinketPicker>(true))
                picker.gameObject.AddComponent<WKTLobbyTrinketPicker>();
            var panelTrs = cloneObj.transform;
            // Prefer the known path, then the panel's own serialized reference, then a name search.
            var infoScreen = panelTrs.Find(c_panel_info_screen);
            if (infoScreen == null && panel.gamemodeInfoPanel != null)
                infoScreen = panel.gamemodeInfoPanel.transform;
            if (infoScreen == null && !WKTUIPath.TryFind(panelTrs, c_panel_info_screen, out infoScreen, true)) {
                Destroy(cloneObj);
                return null;
            }
            if (!WKTUIPath.TryFind<RectTransform>(infoScreen, "Mode Page", out var modePage, true) ||
                !WKTUIPath.TryFind<RectTransform>(modePage, "Board", out var board, true) ||
                !WKTUIPath.TryFind(infoScreen, "Tab Selection Hor", out var tabSelection, true)) {
                Destroy(cloneObj);
                return null;
            }
            AddReadyFlow(panel, panelTrs, tabSelection);
            for (var i = 0; i < tabSelection.childCount; i++) {
                var child = tabSelection.GetChild(i);
                child.gameObject.SetActive(child.name is "Play" or "Back");
                if (child.name is not "Back") continue;
                child.gameObject.AddComponent<WKTStopNetworkOnClick>();
                // Escape opens the settings in the lobby instead of leaving it.
                if (child.TryGetComponent<UI_CloseButtonOnBack>(out var closeOnBack))
                    DestroyImmediate(closeOnBack);
                child.gameObject.AddComponent<WKTLobbyEscapeOpensSettings>();
            }
            CreateLobbyPlayerFab(panel.leaderboard != null ? panel.leaderboard.scorePrefab : null);
            HideLeaderboard(panel);
            HideVanillaBoardChildren(board);
            StretchToParent(modePage);
            StretchToParent(board);
            board.offsetMax = new Vector2(0f, -c_board_top_inset);
            // The sections share one row, so they can never overlap whatever the board's width.
            var row = board.GetComponent<HorizontalLayoutGroup>();
            if (row == null)
                row = board.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.padding = new RectOffset((int)c_board_side_margin, (int)c_board_side_margin, 0, 0);
            row.spacing = c_board_gap;
            row.childAlignment = TextAnchor.UpperLeft;
            row.reverseArrangement = false;
            row.childScaleWidth = false;
            row.childScaleHeight = false;
            row.childControlWidth = true;
            row.childForceExpandWidth = false;
            // Each section sizes its own height to its content.
            row.childControlHeight = false;
            row.childForceExpandHeight = false;
            // Settings
            var settingsContent = CreateLobbySection("Lobby Settings", "Settings", board, c_settings_width, true, out _);
            settingsContent.GetComponent<VerticalLayoutGroup>().childControlWidth = false;
            var shortcutsPopup = CreateShortcutPopup(modePage);
            CreateLobbySettingsElements(settingsContent, shortcutsPopup);
            // Player list
            var playersContent = CreateLobbySection("Player List", "Players", board, c_player_list_width, false, out var playersHeader);
            playersContent.parent.gameObject.AddComponent<WKTLobbyPlayerList>().SetupReferences(playersHeader, playersContent);
            return panel;
        }

        /// <summary>The first screen's Play goes to the trinket screen, whose Play readies the player (see <see cref="WKTLobbyReadyFlow"/>).</summary>
        private static void AddReadyFlow(UI_GamemodeScreen_Panel panel, Transform panelTrs, Transform infoTabs) {
            WKTUIPath.TryFind<Button>(infoTabs, "Play", out var escape);
            Button? ready = null;
            Button? back = null;
            TMP_Text? readyText = null;
            // No search by name: the first screen's tabs have the same name.
            if (WKTUIPath.TryFind(panelTrs, c_panel_new_game_tabs, out var newGameTabs)) {
                if (WKTUIPath.TryFind<Button>(newGameTabs, "Play", out var play)) {
                    ready = play;
                    readyText = play.GetComponentInChildren<TMP_Text>(true);
                }
                if (WKTUIPath.TryFind<Button>(newGameTabs, "Back", out var newGameBack))
                    back = newGameBack;
            }
            panel.gameObject.AddComponent<WKTLobbyReadyFlow>().SetupReferences(panel, escape, ready, readyText, back);
        }

        private void CreateLobbySettingsElements(RectTransform lobbySettingRect, WKTLobbyPopup shortcutsPopup) {
            var buttons = CreateButtonRow(lobbySettingRect);
            // Invite friends: the only way into an invite-only lobby, and works for every visibility.
            var inviteButton = CreateButton("Invite Friends", buttons);
            if (inviteButton != null)
                inviteButton.gameObject.AddComponent<WKTOpenInviteOverlayOnClick>();
            // The shortcut list opens in its own window, as the settings have no room for it. Redacted until the lobby says
            // which shortcuts every player has unlocked.
            var shortcutsButton = CreateButton(WKTShortcuts.REDACTED, buttons);
            if (shortcutsButton != null) {
                AddPopupButton(shortcutsButton, shortcutsPopup, true);
                shortcutsButton.gameObject.AddComponent<WKTLobbyShortcutsButton>();
            }
            // A join code lobby's code, to share. Only shown in those lobbies.
            var joinCodeButton = CreateButton(WKTLobbyJoinCodeButton.LABEL, buttons);
            if (joinCodeButton != null)
                joinCodeButton.gameObject.AddComponent<WKTLobbyJoinCodeButton>();
            var dropdowns = CreateGrid("Dropdowns", lobbySettingRect, c_dropdown_columns, m_dropdownFab, c_fallback_row_height);
            var playMode = CreateDropdown("Play Mode", dropdowns, WKTLobbyPlayModeDropdown.OptionNames);
            if (playMode != null) {
                playMode.SetValueWithoutNotify((int)WKT.Instance.Settings.LocalPlayModeId);
                playMode.gameObject.AddComponent<WKTLobbyPlayModeDropdown>();
            }
            var gooFollow = CreateDropdown("Mass Follows", dropdowns, WKTLobbyGooFollowDropdown.OptionNames);
            if (gooFollow != null) {
                gooFollow.SetValueWithoutNotify((int)WKT.Instance.Settings.LocalGooFollowId);
                gooFollow.gameObject.AddComponent<WKTLobbyGooFollowDropdown>();
            }
            var playerCollision = CreateDropdown("Player Collisions", dropdowns, WKTLobbyPlayerCollisionDropdown.OptionNames);
            if (playerCollision != null) {
                playerCollision.SetValueWithoutNotify((int)WKT.Instance.Settings.LocalPlayerCollisionId);
                playerCollision.gameObject.AddComponent<WKTLobbyPlayerCollisionDropdown>();
            }
            var orbRadius = CreateDropdown("Orb Radius", dropdowns, WKTLobbyOrbRadiusDropdown.OptionNames);
            if (orbRadius != null) {
                orbRadius.SetValueWithoutNotify((int)WKT.Instance.Settings.LocalOrbRadiusId);
                orbRadius.gameObject.AddComponent<WKTLobbyOrbRadiusDropdown>();
                // The dropdown sits in a labelled row: the row is what shows or hides.
                dropdowns.gameObject.AddComponent<WKTLobbyModeVisibility>().AddRaceOnly(orbRadius.transform.parent.gameObject);
            }
            FitDropdownsToCells(dropdowns);
            SizeGrid(dropdowns);
            if (dropdowns.TryGetComponent<WKTLobbyModeVisibility>(out var dropdownVisibility))
                dropdownVisibility.Refresh();
            var modifiers = CreateGrid("Modifiers", lobbySettingRect, c_modifier_columns, m_toggleFab, c_fallback_toggle_height);
            CreateModifierToggle("Iron Knuckle Mode", modifiers, ModifierId.IronKnuckle, false);
            CreateModifierToggle("Hard Mode", modifiers, ModifierId.Hard, false);
            CreateModifierToggle("Instanced", modifiers, ModifierId.Instanced, false);
            var shareTimedHandholds = CreateModifierToggle("Share Timed Handholds", modifiers, ModifierId.ShareTimedHandholds, true);
            if (shareTimedHandholds != null) {
                shareTimedHandholds.hasDisabledBy = true;
                shareTimedHandholds.disabledBy = ModifierId.Instanced;
            }
            var modifierVisibility = modifiers.gameObject.AddComponent<WKTLobbyModeVisibility>();
            var shareRoaches = CreateModifierToggle("Share Roaches", modifiers, ModifierId.ShareRoaches, true);
            if (shareRoaches != null)
                modifierVisibility.AddCoopOnly(shareRoaches.gameObject);
            var itemStealing = CreateModifierToggle("Item Stealing", modifiers, ModifierId.ItemStealing, true);
            if (itemStealing != null) {
                itemStealing.hasDisabledBy = true;
                itemStealing.disabledBy = ModifierId.Instanced;
            }
            var instancedArtifacts = CreateModifierToggle("Own Artifact Picks", modifiers, ModifierId.InstancedArtifacts, true);
            if (instancedArtifacts != null) {
                instancedArtifacts.hasDisabledBy = true;
                instancedArtifacts.disabledBy = ModifierId.Instanced;
            }
            var grabAssists = CreateModifierToggle("Grab Assists", modifiers, ModifierId.GrabAssists, true);
            if (grabAssists != null) {
                grabAssists.hasDisabledBy = true;
                grabAssists.disabledBy = ModifierId.Instanced;
                modifierVisibility.AddCoopOnly(grabAssists.gameObject);
            }
            CreateModifierToggle("Proximity Voice", modifiers, ModifierId.ProximityVoice, true);
            CreateModifierToggle("Dead Can Talk", modifiers, ModifierId.DeadCanTalk, true);
            modifierVisibility.Refresh();
            SizeGrid(modifiers);
        }

        /// <summary>Makes <paramref name="button"/> show or hide <paramref name="popup"/>, in a way the game's clone of the panel keeps.</summary>
        private static void AddPopupButton(Button button, WKTLobbyPopup popup, bool opens) {
            var popupButton = button.gameObject.AddComponent<WKTLobbyPopupButton>();
            popupButton.popup = popup;
            popupButton.opens = opens;
        }

        /// <summary>A row of buttons side by side, each at its own size, as tall as the button template.</summary>
        private RectTransform CreateButtonRow(Transform parent) {
            var rect = WKTUIBuild.CreateRect("Buttons", parent);
            rect.pivot = new Vector2(0f, 1f);
            var height = m_buttonFab != null ? ((RectTransform)m_buttonFab.transform).rect.height : 0f;
            rect.sizeDelta = new Vector2(c_settings_width - c_section_padding * 2f, height > 0f ? height : c_fallback_toggle_height);
            var row = rect.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.childAlignment = TextAnchor.UpperLeft;
            row.childControlWidth = false;
            row.childControlHeight = false;
            row.childForceExpandWidth = false;
            row.childForceExpandHeight = false;
            row.spacing = c_section_spacing;
            return rect;
        }

        /// <summary>
        /// The window listing the campaign's shortcuts, one per line, hidden until opened. A backdrop over
        /// <paramref name="page"/> keeps the lobby behind it from being clicked, and closes the window when clicked.
        /// </summary>
        private WKTLobbyPopup CreateShortcutPopup(RectTransform page) {
            var backdrop = WKTUIBuild.CreateRect("Shortcuts Popup", page);
            StretchToParent(backdrop);
            var dim = backdrop.gameObject.AddComponent<Image>();
            dim.color = new Color(0f, 0f, 0f, c_popup_backdrop_alpha);
            var popup = backdrop.gameObject.AddComponent<WKTLobbyPopup>();
            var backdropButton = backdrop.gameObject.AddComponent<Button>();
            backdropButton.transition = Selectable.Transition.None;
            AddPopupButton(backdropButton, popup, false);
            var window = WKTUIBuild.CreateRect("Window", backdrop);
            window.anchorMin = window.anchorMax = new Vector2(0.5f, 0.5f);
            window.pivot = new Vector2(0.5f, 0.5f);
            window.sizeDelta = new Vector2(c_popup_width, 0f);
            var background = window.gameObject.AddComponent<Image>();
            if (m_sectionBackgroundStyle != null) {
                background.sprite = m_sectionBackgroundStyle.sprite;
                background.type = m_sectionBackgroundStyle.type;
                background.pixelsPerUnitMultiplier = m_sectionBackgroundStyle.pixelsPerUnitMultiplier;
            }
            // Opaque, so the lobby behind never shows through the list.
            background.color = new Color(0.05f, 0.05f, 0.05f, 1f);
            const int padding = (int)c_section_padding;
            AddVerticalLayout(window.gameObject, true, c_section_spacing).padding = new RectOffset(padding, padding, padding, padding);
            window.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            CreateSectionHeader("Shortcuts", window);
            var toggleHeight = m_toggleFab != null ? ((RectTransform)m_toggleFab.transform).rect.height : 0f;
            foreach (var id in WKTShortcuts.All) {
                var toggle = CreateToggle(WKTShortcuts.REDACTED, window);
                if (toggle == null) continue;
                toggle.gameObject.AddComponent<LayoutElement>().preferredHeight = toggleHeight > 0f ? toggleHeight : c_fallback_toggle_height;
                toggle.gameObject.AddComponent<WKTToggleBinder>();
                toggle.GetComponentInChildren<TMP_Text>(true).textWrappingMode = TextWrappingModes.NoWrap;
                toggle.gameObject.AddComponent<WKTLobbyShortcutToggle>().Configure(id);
            }
            var close = CreateButton("Close", window);
            if (close != null) {
                var closeHeight = ((RectTransform)close.transform).rect.height;
                close.gameObject.AddComponent<LayoutElement>().preferredHeight = closeHeight > 0f ? closeHeight : c_fallback_toggle_height;
                AddPopupButton(close, popup, false);
            }
            popup.Hide();
            return popup;
        }

        /// <summary>The dropdown template is as wide as a full settings row; in a grid each box spans its own cell instead.</summary>
        private static void FitDropdownsToCells(RectTransform grid) {
            for (var i = 0; i < grid.childCount; i++) {
                var dropdown = grid.GetChild(i).GetComponentInChildren<TMP_Dropdown>(true);
                if (dropdown == null) continue;
                var rect = (RectTransform)dropdown.transform;
                rect.anchorMin = new Vector2(0f, rect.anchorMin.y);
                rect.anchorMax = new Vector2(1f, rect.anchorMax.y);
                rect.offsetMin = new Vector2(0f, rect.offsetMin.y);
                rect.offsetMax = new Vector2(0f, rect.offsetMax.y);
            }
        }

        /// <summary>
        /// A block that lays lobby settings out in <paramref name="columns"/> columns, each cell as tall as
        /// <paramref name="template"/>, widened to fill the settings section (<see cref="WKTLobbyGridFit"/>). Size it with
        /// <see cref="SizeGrid"/>.
        /// </summary>
        private static RectTransform CreateGrid(string name, Transform parent, int columns, GameObject? template, float fallbackHeight) {
            var rect = WKTUIBuild.CreateRect(name, parent);
            var grid = rect.gameObject.AddComponent<GridLayoutGroup>();
            var height = template != null ? ((RectTransform)template.transform).rect.height : 0f;
            var width = (c_settings_width - c_section_padding * 2f - c_section_spacing * (columns - 1)) / columns;
            grid.cellSize = new Vector2(width, height > 0f ? height : fallbackHeight);
            grid.spacing = new Vector2(c_section_spacing, c_section_spacing);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = columns;
            grid.childAlignment = TextAnchor.UpperLeft;
            // Grows right and down from its top-left corner, so a late resize can never shift it out of place.
            rect.pivot = new Vector2(0f, 1f);
            rect.gameObject.AddComponent<WKTLobbyGridFit>();
            return rect;
        }

        /// <summary>
        /// Sizes the grid to its shown entries. Call once they are all added, and again when entries are shown or hidden. The
        /// parent layout only reads child sizes and places them before a ContentSizeFitter on the child would run, so the size
        /// has to be known up front.
        /// </summary>
        public static void SizeGrid(RectTransform rect) {
            var grid = rect.GetComponent<GridLayoutGroup>();
            var columns = grid.constraintCount;
            var shown = 0;
            for (var i = 0; i < rect.childCount; i++)
                if (rect.GetChild(i).gameObject.activeSelf) shown++;
            var rows = (shown + columns - 1) / columns;
            var cell = grid.cellSize;
            var spacing = grid.spacing;
            rect.sizeDelta = new Vector2(columns * cell.x + (columns - 1) * spacing.x, rows * cell.y + Mathf.Max(rows - 1, 0) * spacing.y);
        }

        private WKTLobbyModifierToggle? CreateModifierToggle(string text, Transform parent, ModifierId modifierId, bool isOnByDefault) {
            var toggle = CreateToggle(text, parent);
            if (toggle == null) return null;
            toggle.gameObject.AddComponent<WKTToggleBinder>();
            // Each grid column has room for the whole name on one line.
            toggle.GetComponentInChildren<TMP_Text>(true).textWrappingMode = TextWrappingModes.NoWrap;
            var modifierToggle = toggle.gameObject.AddComponent<WKTLobbyModifierToggle>();
            // Start from the remembered play mode's default, so the toggle matches the mode shown before anything is picked.
            var modeDefault = WKTPlayModeDefaults.GetDefault(WKT.Instance.Settings.LocalPlayModeId, modifierId, out var hasModeDefault);
            modifierToggle.Configure(modifierId, hasModeDefault ? modeDefault : isOnByDefault);
            return modifierToggle;
        }

        private void InitializeGamemodes() {
            if (m_playTabObjects == null) {
                WKT.Instance.Logger.LogError("Failed to initialize multiplayer gamemodes. Tab Objects is null.");
                return;
            }
            // Campaign
            if (!WKTUIPath.TryFind<UI_Gamemode_Button>(m_playTabObjects.transform, c_campaign_button, out var campaignButton, true)) return;
            var originalCampaignGamemode = campaignButton.GetGamemode();
            if (originalCampaignGamemode == null) {
                WKT.Instance.Logger.LogError("Failed to initialize multiplayer campaign. Campaign button has no gamemode.");
                return;
            }
            var gamemode = Instantiate(originalCampaignGamemode);
            gamemode.allowAchievements = false;
            gamemode.allowCheats = false;
            gamemode.allowCompetitive = false;
            // Competitive runs are not a thing in multiplayer: the setting is not offered at all.
            gamemode.gamemodeSettings = gamemode.gamemodeSettings.FindAll(setting => setting != null && setting.id != c_competitive_setting);
            gamemode.allowCheatedScores = false;
            gamemode.allowLeaderboardScoring = false;
            gamemode.allowHeightAchievements = false;
            gamemode.scoreLeaderboardHard = false;
            gamemode.scoreLeaderboardIronKnuckle = false;
            gamemode.scoreLeaderboardScore = false;
            gamemode.scoreLeaderboardTime = false;
            // No categories makes Leaderboard_Panel.Refresh bail out before querying Steam.
            gamemode.leaderboardCategories = 0;
            gamemode.showBankAmount = false;
            gamemode.gamemodeName = "Multiplayer Campaign";
            gamemode.saveRoaches = false;
            gamemode.forceStartingRoaches = true;
            gamemode.startingRoaches = 10;
            gamemode.roachBankID = "multiplayer-campaign";
            gamemode.introText = "ESCAPE... TOGETHER";
            var panel = CreateMultiplayerCampaignPanel(gamemode.gamemodePanel);
            if (panel == null) {
                WKT.Instance.Logger.LogError("Multiplayer campaign is unavailable. Its panel could not be created.");
                Destroy(gamemode);
                return;
            }
            gamemode.gamemodePanel = panel;
            m_multiplayerCampaignGamemode = gamemode;
            WKTMultiplayerGamemodes.Register(GamemodeId.Campaign, gamemode);
        }

        private HorizontalLayoutGroup? CreateMultiplayerButtonsLayout() {
            if (m_playPane == null) {
                WKT.Instance.Logger.LogError("Failed to create multiplayer layout. Play Pane is null.");
                return null;
            }
            var obj = new GameObject("Multiplayer Toggle Layout", typeof(RectTransform));
            obj.transform.SetParent(m_playPane.transform, false);
            var rect = obj.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(200f, 50f);
            rect.anchorMin = new Vector2(1f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(1f, 0f);
            rect.anchoredPosition = new Vector2(-20f, 20f);
            var layout = obj.AddComponent<HorizontalLayoutGroup>();
            layout.childAlignment = TextAnchor.MiddleRight;
            return layout;
        }

        private void CreateMultiplayerToggleButton() {
            var layout = CreateMultiplayerButtonsLayout();
            if (layout == null) return;
            m_multiplayerToggleButton = CreateButton("Multiplayer", layout.transform);
            if (m_multiplayerToggleButton == null) return;
            m_multiplayerToggleButtonText = m_multiplayerToggleButton.GetComponentInChildren<TMP_Text>();
            m_multiplayerToggleButton.onClick.AddListener(OnMultiplayerToggleClicked);
        }

        private void CreateMultiplayerPlayUI() {
            if (m_playPane == null || m_playTabs == null || m_playTabObjects == null || m_playModifierList == null || m_playMutators == null) {
                WKT.Instance.Logger.LogError("Failed to create multiplayer play UI. Play Pane references are missing.");
                return;
            }
            var playPane = m_playPane.transform;
            // Tabs
            m_multiplayerTabs = Instantiate(m_playTabs, playPane);
            m_multiplayerTabs.name = "Multiplayer Tabs";
            var tabsRoot = m_multiplayerTabs.transform;
            WKTUIPath.DestroyIfFound(tabsRoot, "Tab Buttons/ModeButton_Challenge");
            WKTUIPath.DestroyIfFound(tabsRoot, "Tab Buttons/ModeButton_Event");
            WKTUIPath.DestroyIfFound(tabsRoot, "Tab Buttons/ModeButton_Pax");
            WKTUIPath.DestroyIfFound(tabsRoot, "Tab Buttons/ModeButton_Custom");
            WKTUIPath.DestroyIfFound(tabsRoot, "Tab Buttons/ModeButton_DEV");
            if (!WKTUIPath.TryFind<Button>(tabsRoot, "Tab Buttons/ModeButton_Main", out var hostTab, true)) return;
            var hostTabText = hostTab.GetComponentInChildren<TMP_Text>();
            hostTabText.text = "Host";
            // The endless tab's button becomes the Join tab's.
            TMP_Text? joinTabText = null;
            if (WKTUIPath.TryFind<Button>(tabsRoot, "Tab Buttons/ModeButton_Endless", out var joinTab, true)) {
                joinTabText = joinTab.GetComponentInChildren<TMP_Text>();
                joinTabText.text = "Join";
            }
            // Tab Objects
            m_multiplayerTabObjects = Instantiate(m_playTabObjects, playPane);
            m_multiplayerTabObjects.name = "Multiplayer Tab Objects";
            var mtoRoot = m_multiplayerTabObjects.transform;
            const string roach_root = c_main_tab_content + "/Mode - Major Section Break - Base.02/Section Title";
            WKTUIPath.DestroyIfFound(mtoRoot, roach_root + "/Roach Counter - Hard");
            WKTUIPath.DestroyIfFound(mtoRoot, roach_root + "/Roach Counter - Regular");
            WKTUIPath.DestroyIfFound(mtoRoot, c_main_tab_content + "/Mode Selection Button - Tutorial");
            WKTUIPath.DestroyIfFound(mtoRoot, c_main_tab_content + "/Mode Selection Button - Training Sector");
            WKTUIPath.DestroyIfFound(mtoRoot, c_main_tab_content + "/Mode - Major Section Break - Base.01");
            if (!WKTUIPath.TryFind(mtoRoot, c_main_tab, out var mainTab, true) ||
                !WKTUIPath.TryFind<Button>(mtoRoot, c_campaign_button, out var campaignButton, true))
                return;
            var gamemodeButton = campaignButton.GetComponent<UI_Gamemode_Button>();
            if (gamemodeButton != null)
                Destroy(gamemodeButton);
            var selectTrigger = campaignButton.GetComponent<UI_SelectEventTrigger>();
            if (selectTrigger != null)
                Destroy(selectTrigger);
            // Modifier List
            m_multiplayerModifierList = Instantiate(m_playModifierList, playPane);
            m_multiplayerModifierList.name = "Multiplayer Modifier List";
            WKTUIPath.DestroyIfFound(m_multiplayerModifierList.transform, "IronKnuckleMode");
            WKTUIPath.DestroyIfFound(m_multiplayerModifierList.transform, "Hard Mode");
            CreateLobbyVisibilityDropdown(playPane);
            CreateLobbyNameInput(playPane);
            m_hostTabObject = mainTab.gameObject;
            // Set up components
            var tabGroup = m_multiplayerTabs.GetComponent<UI_TabGroup>();
            tabGroup.tabs.Clear();
            tabGroup.tabs.Add(new UI_TabGroup.Tab {
                name = "host",
                button = hostTab,
                buttonText = hostTabText,
                tabObject = mainTab.gameObject
            });
            if (joinTab != null) {
                tabGroup.tabs.Add(new UI_TabGroup.Tab {
                    name = "join",
                    button = joinTab,
                    buttonText = joinTabText,
                    tabObject = CreateJoinTab(mtoRoot).gameObject
                });
            }
            tabGroup.EventOnChangeTab.AddListener(OnMultiplayerTabChanged);
            tabGroup.SelectTab("host");
            campaignButton.onClick.AddListener(OnMultiplayerCampaignClicked);
            SetMultiplayerPlayMenuActive(false);
        }

        /// <summary>Sits in the empty top-right corner of the Play pane, where it has room unlike the Options row.</summary>
        private void CreateLobbyVisibilityDropdown(Transform playPane) {
            var dropdown = CreateDropdown("Lobby Visibility", playPane, LocalLobbyVisibilityDropdown.OptionNames);
            if (dropdown == null) return;
            dropdown.SetValueWithoutNotify((int)WKT.Instance.Settings.LocalLobbyVisibilityId);
            dropdown.gameObject.AddComponent<LocalLobbyVisibilityDropdown>();
            // CreateDropdown returns the inner dropdown; the labelled root is the child of the pane.
            var root = dropdown.transform;
            while (root.parent != playPane)
                root = root.parent;
            root.name = "Multiplayer Lobby Visibility";
            var rect = (RectTransform)root;
            rect.anchorMin = new Vector2(1f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            rect.anchoredPosition = new Vector2(-20f, -20f);
            m_multiplayerLobbyVisibility = root.gameObject;
        }

        /// <summary>
        /// Sits under the lobby visibility and is labelled the same way: the name the lobby browser shows for lobbies this
        /// player hosts. Built from a labelled dropdown row whose dropdown is swapped for a text field.
        /// </summary>
        private void CreateLobbyNameInput(Transform playPane) {
            if (m_multiplayerLobbyVisibility == null || m_inputFab == null) return;
            var root = Spawn(m_dropdownFab, "lobby name");
            if (root == null) return;
            root.name = "Multiplayer Lobby Name";
            root.GetComponent<TMP_Text>().text = "Lobby Name";
            var dropdown = root.GetComponentInChildren<TMP_Dropdown>(true);
            var dropdownRect = (RectTransform)dropdown.transform;
            var input = Instantiate(m_inputFab, dropdownRect.parent, false).GetComponent<TMP_InputField>();
            var inputRect = (RectTransform)input.transform;
            inputRect.anchorMin = dropdownRect.anchorMin;
            inputRect.anchorMax = dropdownRect.anchorMax;
            inputRect.pivot = dropdownRect.pivot;
            inputRect.sizeDelta = dropdownRect.sizeDelta;
            inputRect.anchoredPosition = dropdownRect.anchoredPosition;
            inputRect.SetSiblingIndex(dropdownRect.GetSiblingIndex());
            input.characterLimit = WKTLobbyConsts.MAX_LOBBY_NAME_LENGTH;
            // Left empty, the lobby is named after the player.
            if (input.placeholder is TMP_Text placeholder)
                placeholder.text = WKTLobbyData.GetLobbyName(null, SteamClient.Name);
            input.gameObject.AddComponent<LocalLobbyNameInput>();
            input.gameObject.SetActive(true);
            DestroyImmediate(dropdown.gameObject);
            Attach(root, playPane);
            var rect = (RectTransform)root.transform;
            var visibility = (RectTransform)m_multiplayerLobbyVisibility.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            var height = visibility.rect.height;
            rect.anchoredPosition = visibility.anchoredPosition - new Vector2(0f, (height > 0f ? height : c_fallback_row_height) + c_section_spacing);
            m_multiplayerLobbyName = root;
        }

        /// <summary>
        /// Makes the seed popup's text field look like the settings dropdown it sits beside: the dropdown's box, and its
        /// caption's font, colour, alignment and inset for the text.
        /// </summary>
        private static void MatchDropdownStyle(TMP_InputField input, TMP_Dropdown dropdown) {
            const float placeholder_alpha = 0.45f;
            if (dropdown.targetGraphic is Image box && input.targetGraphic is Image field) {
                field.sprite = box.sprite;
                field.type = box.type;
                field.color = box.color;
                field.pixelsPerUnitMultiplier = box.pixelsPerUnitMultiplier;
            }
            var caption = dropdown.captionText;
            if (caption == null) return;
            if (input.textViewport != null) {
                var captionRect = caption.rectTransform;
                var viewport = input.textViewport;
                viewport.anchorMin = captionRect.anchorMin;
                viewport.anchorMax = captionRect.anchorMax;
                viewport.pivot = captionRect.pivot;
                viewport.offsetMin = captionRect.offsetMin;
                viewport.offsetMax = captionRect.offsetMax;
            }
            StyleLike(input.textComponent, caption, caption.color);
            if (input.placeholder is TMP_Text placeholder) {
                var color = caption.color;
                color.a *= placeholder_alpha;
                StyleLike(placeholder, caption, color);
            }
        }

        private static void StyleLike(TMP_Text text, TMP_Text style, Color color) {
            text.font = style.font;
            text.fontSharedMaterial = style.fontSharedMaterial;
            text.fontSize = style.fontSize;
            text.fontStyle = style.fontStyle;
            text.characterSpacing = style.characterSpacing;
            text.horizontalAlignment = style.horizontalAlignment;
            text.verticalAlignment = style.verticalAlignment;
            text.color = color;
        }

        /// <summary>
        /// The Join tab: the lobby browser, between the tab buttons and Close. Taller than the Host tab's gamemodes, so more
        /// lobbies fit, and inset at the sides to stay clear of the facility badge in the top-left corner.
        /// </summary>
        private RectTransform CreateJoinTab(Transform tabObjects) {
            var rect = WKTUIBuild.CreateRect("Multiplayer Join Tab", tabObjects);
            // Inactive before the browser is added, so it only asks Steam for lobbies once the tab is opened.
            rect.gameObject.SetActive(false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(c_join_tab_side_inset, c_join_tab_bottom_inset);
            rect.offsetMax = new Vector2(-c_join_tab_side_inset, -c_join_tab_top_inset);
            var scrollbar = m_dropdownFab != null ? m_dropdownFab.GetComponentInChildren<Scrollbar>(true) : null;
            WKTLobbyBrowser.Create(rect, m_buttonFab, m_inputFab, scrollbar, m_sectionBackgroundStyle, m_sectionHeaderStyle);
            return rect;
        }

        private void OnMultiplayerTabChanged() => ShowHostOptions(m_multiplayerShown);

        /// <summary>The lobby visibility and name only matter for hosting, so they show on the Host tab alone.</summary>
        private void ShowHostOptions(bool multiplayerShown) {
            var shown = multiplayerShown && (m_hostTabObject == null || m_hostTabObject.activeSelf);
            SetActive(m_multiplayerLobbyVisibility, shown);
            SetActive(m_multiplayerLobbyName, shown);
        }

        private void InjectMultiplayerUI() {
            CreateMultiplayerPlayUI();
            CreateMultiplayerToggleButton();
        }

        private void SetDefaultPlayMenuActive(bool active) {
            SetActive(m_playTabs, active);
            SetActive(m_playTabObjects, active);
            SetActive(m_playModifierList, active);
            SetActive(m_playMutators, active);
        }

        private void SetMultiplayerPlayMenuActive(bool active) {
            SetActive(m_multiplayerTabs, active);
            SetActive(m_multiplayerTabObjects, active);
            SetActive(m_multiplayerModifierList, active);
            ShowHostOptions(active);
        }

        private static bool TryGetLobbyPlayerList(out WKTLobbyPlayerList lobbyPlayerList) {
            lobbyPlayerList = null!;
            var gamemodeScreen = UI_GamemodeScreen.instance;
            if (gamemodeScreen == null || gamemodeScreen.activePanels == null ||
                !gamemodeScreen.activePanels.TryGetValue(c_multiplayer_campaign_panel, out var panel) || panel == null) {
                WKT.Instance.Logger.LogError("Failed to find multiplayer campaign panel.");
                return false;
            }
            lobbyPlayerList = panel.GetComponentInChildren<WKTLobbyPlayerList>(true);
            if (lobbyPlayerList != null) return true;
            WKT.Instance.Logger.LogError("Multiplayer campaign panel has no lobby player list.");
            return false;
        }

        private void SetMultiplayerShown(bool shown) {
            m_multiplayerShown = shown;
            SetDefaultPlayMenuActive(!shown);
            SetMultiplayerPlayMenuActive(shown);
            if (m_multiplayerToggleButtonText != null)
                m_multiplayerToggleButtonText.text = shown ? "Singleplayer" : "Multiplayer";
        }

        /// <summary>
        /// Takes the player from any main menu screen to the lobby panel of <paramref name="gamemode"/>, the way the
        /// host gets there by clicking through the menus.
        /// </summary>
        private bool TryOpenMultiplayerLobby(M_Gamemode gamemode) {
            if (m_playGamemodeScreen == null || m_playGameButton == null) {
                WKT.Instance.Logger.LogError("Cannot open lobby. Gamemode Screen or Play Game button is unavailable.");
                return false;
            }
            // Clicking it opens the Play screen and closes the other main menu screens.
            m_playGameButton.onClick.Invoke();
            SetMultiplayerShown(true);
            m_playGamemodeScreen.Initialize(gamemode);
            return true;
        }

        private void OnMultiplayerToggleClicked() => SetMultiplayerShown(!m_multiplayerShown);

        private void OnMultiplayerCampaignClicked() {
            WKT.Instance.Logger.LogInfo("Campaign clicked.");
            if (m_playGamemodeScreen == null || m_multiplayerCampaignGamemode == null) {
                WKT.Instance.Logger.LogError("Cannot create campaign lobby. Gamemode Screen or multiplayer campaign is unavailable.");
                return;
            }
            m_playGamemodeScreen.Initialize(m_multiplayerCampaignGamemode);
            // A session still running from before, like one this player took over as host, keeps its lobby.
            if (WKT.Instance.Network.Mode != NetworkMode.None) {
                WKT.Instance.Logger.LogInfo("Opened the lobby of the active session.");
                return;
            }
            WKT.Instance.Settings.LocalSelectedGamemodeId = GamemodeId.Campaign;
            WKT.Instance.Network.StartHost();
        }

        /// <summary>
        /// A session outlives a run, but this menu was just created and knows nothing about it. Shows its lobby again with
        /// everyone in it, so the player is not left in an invisible session.
        /// </summary>
        private void RestoreActiveSession() {
            var network = WKT.Instance.Network;
            if (network.Mode == NetworkMode.None) return;
            var gamemodeId = WKT.Instance.Settings.LocalSelectedGamemodeId;
            if (!WKTMultiplayerGamemodes.TryGet(gamemodeId, out var gamemode) || !TryOpenMultiplayerLobby(gamemode)) {
                WKT.Instance.Logger.LogError($"Cannot show the {gamemodeId} lobby of the active session. Leaving the session.");
                network.Stop();
                return;
            }
            foreach (var player in network.GetPlayersEnumerable())
                OnNetworkPlayerConnected(player);
            WKT.Instance.Logger.LogInfo($"Restored the {gamemodeId} lobby of the active session.");
        }

        private void OnNetworkClientStarted() {
            var gamemodeId = WKT.Instance.Settings.LocalSelectedGamemodeId;
            if (WKTMultiplayerGamemodes.TryGet(gamemodeId, out var gamemode) && TryOpenMultiplayerLobby(gamemode)) {
                WKT.Instance.Logger.LogInfo($"Opened {gamemodeId} lobby for joined session.");
                return;
            }
            WKT.Instance.Logger.LogError($"Cannot show the {gamemodeId} lobby. Leaving the session.");
            WKT.Instance.Network.Stop();
        }

        private void OnNetworkPlayerConnected(WKTPlayer player) {
            WKT.Instance.Logger.LogInfo($"Player {player.Id} joined.");
            if (!TryGetLobbyPlayerList(out var playerList) || playerList.TryGetLobbyPlayer(player, out _)) return;
            var lobbyPlayer = CreateLobbyPlayer(playerList.RowRoot);
            if (lobbyPlayer == null) {
                WKT.Instance.Logger.LogError("Failed to create lobby player.");
                return;
            }
            playerList.RegisterPlayer(player, lobbyPlayer);
            WKT.Instance.Logger.LogInfo($"Added lobby player for player {player.Id}.");
        }

        private void OnNetworkPlayerDisconnected(WKTPlayer player) {
            WKT.Instance.Logger.LogInfo($"Player {player.Id} disconnected.");
            if (!TryGetLobbyPlayerList(out var playerList)) return;
            if (!playerList.UnregisterPlayer(player)) {
                WKT.Instance.Logger.LogError("Failed to unregister lobby player.");
                return;
            }
            WKT.Instance.Logger.LogInfo($"Removed lobby player for player {player.Id}.");
        }

        private void OnNetworkStopping() {
            CloseMultiplayerLobby();
            ShowSessionEndReason();
            if (!TryGetLobbyPlayerList(out var playerList)) return;
            playerList.Clear();
        }

        private void OnNetworkHostChanged() {
            if (TryGetLobbyPlayerList(out var playerList))
                playerList.RefreshTags();
        }

        /// <summary>Tells the player the host disconnected, if that is why the session ended.</summary>
        private static void ShowSessionEndReason() {
            if (WKT.Instance.Network.ConsumeEndReason() != WKTSessionEndReason.HostDisconnected) return;
            WKTAlert.ShowNotice("Disconnected", "The host disconnected and nobody could take over. The session has ended.");
        }

        /// <summary>
        /// Leaves the lobby panel when the session ends without the player pressing Back, like when the host leaves or
        /// the client is rejected.
        /// </summary>
        private void CloseMultiplayerLobby() {
            var gamemodeScreen = m_playGamemodeScreen;
            // The gamemode selector is only locked while a gamemode window is open, so this skips a window Back closed.
            if (gamemodeScreen == null || gamemodeScreen.gamemodeSelectorGroup == null || gamemodeScreen.gamemodeSelectorGroup.interactable ||
                !WKTMultiplayerGamemodes.TryGetId(gamemodeScreen.GetGamemode(), out _))
                return;
            gamemodeScreen.CloseWindow();
            WKT.Instance.Logger.LogInfo("Closed the lobby because the session ended.");
        }
    }
}
