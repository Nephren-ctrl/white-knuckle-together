using System;
using System.Collections.Generic;
using System.Text;
using Febucci.UI;
using Febucci.UI.Effects;
using JetBrains.Annotations;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using WhiteKnuckleTogether.Networking.LiteNetLib;
using SendType = Steamworks.Data.SendType;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// The text chat in the bottom left corner, in the lobby and during a run. The open chat key (Enter by default) starts
    /// typing; Enter sends and Escape closes. The history keys fill in messages sent before, Page Up / Page Down scroll the
    /// log, and so does the mouse wheel over it while typing. The log fades a while after the newest line unless typing.
    /// <para>
    /// Nothing of it takes clicks, so it never blocks the menus behind it, and it reads the keyboard itself rather than through
    /// the UI's selection, which the menus move around. While typing, the UI's own navigation (Enter submitting a selected
    /// button, arrow keys moving the selection) is switched off. In the lobby it has a dark background so the lobby's own
    /// text behind it cannot make it hard to read.
    /// </para>
    /// <para>
    /// Clients send their message to the host, which checks it against the same spam limits and relays it to everyone, the
    /// sender included, so every machine shows exactly the messages the host let through.
    /// </para>
    /// <para>
    /// Run events (a perk chosen, an item stolen, the dead revived) are grey lines too, posted by the machine where they
    /// happen (<see cref="PostEvent"/>). Their rich text may carry the game's text effects, which the line's TextAnimator
    /// plays; player messages never animate.
    /// </para>
    /// Exists while a multiplayer session does, across scene loads.
    /// </summary>
    internal sealed class WKTChat : MonoBehaviour {
        public const float HOLD_SECONDS = 5f;
        public const float FADE_SECONDS = 1f;
        public const int MAX_LINES = 100;
        private const float c_panel_width = 620f;
        private const float c_panel_height = 320f;
        private const float c_margin = 24f;
        private const float c_padding = 8f;
        private const float c_input_height = 40f;
        private const float c_font_size = 22f;
        private const float c_wheel_step = 60f;
        private const float c_page_fraction = 0.8f;
        private const float c_lobby_background_alpha = 0.55f;
        private const float c_run_background_alpha = 0.3f;
        private const float c_input_background_alpha = 0.6f;
        /// <summary>Above the run HUD and the lobby, below the toast.</summary>
        private const int c_sorting_order = short.MaxValue - 2;
        private const string c_system_color = "#A8A8A8";
        private const string c_notice_color = "#E07070";
        private const string c_placeholder = "Say something...";
        private const float c_caret_blink_seconds = 0.5f;
        private const char c_backspace = '\b';
        /// <summary>The text effect the game's own effects database is found by.</summary>
        private const string c_probe_effect = "shimmer";
        private static readonly WKTChatRateLimiter?[] s_hostLimiters = new WKTChatRateLimiter?[byte.MaxValue + 1];
        private static WKTChat? s_instance;
        /// <summary>The game's text effects, once looked up. Null if it has none, and event lines then show without them.</summary>
        private static AnimationsDatabase? s_effects;
        private static bool s_lookedUpEffects;
        /// <summary>The frame typing ended. The key that ended it must not also act on the game.</summary>
        private static int s_closedFrame = -1;
        private readonly List<ChatLine> m_lines = new();
        private readonly WKTChatHistory m_history = new();
        private readonly WKTChatRateLimiter m_limiter = new();
        private WKTNetwork m_network = null!;
        private WKTConfig m_config = null!;
        private CanvasGroup m_group = null!;
        private Image m_background = null!;
        private RectTransform m_panel = null!;
        private RectTransform m_viewport = null!;
        private RectTransform m_content = null!;
        private readonly StringBuilder m_draft = new(WKTChatText.MAX_LENGTH);
        private RectTransform m_inputRoot = null!;
        private TextMeshProUGUI m_inputText = null!;
        private string m_shownDraft = string.Empty;
        private float m_caretTime;
        private bool m_shownCaret;
        /// <summary>Whether this chat turned off the UI's navigation, so it turns it back on.</summary>
        private bool m_disabledNavigation;
        private float m_lastLineTime = float.NegativeInfinity;
        private float m_scroll;
        private bool m_isTyping;
        private bool m_relocksCursor;
        private bool m_welcomed;
        private int m_welcomeFrame = -1;

        /// <summary>The local player is typing a message.</summary>
        public static bool IsTyping => s_instance != null && s_instance.m_isTyping;

        /// <summary>
        /// The keyboard belongs to the chat: it is being typed in, was just closed, or is opening this frame. The game must not
        /// act on the keys meanwhile, menus included.
        /// </summary>
        public static bool OwnsKeyboard =>
            s_instance != null && (s_instance.m_isTyping || Time.frameCount <= s_closedFrame + 1 || s_instance.IsOpeningThisFrame);

        private bool IsOpeningThisFrame => m_config.OpenChat.IsDown && CanOpen();

        [UsedImplicitly]
        private void Awake() {
            m_network = WKT.Instance.Network;
            m_config = WKTPlugin.ModConfig!;
            m_network.Messages.Register(WKTMessageId.Chat, OnChatMessage);
            m_network.Messages.Register(WKTMessageId.ChatNotice, OnChatNotice);
            m_network.Messages.Register(WKTMessageId.ItemFound, OnItemFound);
            m_network.Messages.Register(WKTMessageId.ChatEvent, OnChatEvent);
            m_network.OnPlayerConnected += OnPlayerConnected;
            m_network.OnPlayerDisconnected += OnPlayerDisconnected;
            m_network.OnNetworkStopping += OnNetworkStopping;
            Build();
            m_group.alpha = 0f;
        }

        [UsedImplicitly]
        private void OnDestroy() {
            if (s_instance == this)
                s_instance = null;
            if (m_isTyping)
                Close();
            var wkt = WKT.InstanceOrNull;
            if (wkt == null) return;
            wkt.Network.Messages.Unregister(WKTMessageId.Chat);
            wkt.Network.Messages.Unregister(WKTMessageId.ChatNotice);
            wkt.Network.Messages.Unregister(WKTMessageId.ItemFound);
            wkt.Network.Messages.Unregister(WKTMessageId.ChatEvent);
            wkt.Network.OnPlayerConnected -= OnPlayerConnected;
            wkt.Network.OnPlayerDisconnected -= OnPlayerDisconnected;
            wkt.Network.OnNetworkStopping -= OnNetworkStopping;
        }

        [UsedImplicitly]
        private void Update() {
            if (m_isTyping)
                UpdateTyping();
            else if (IsOpeningThisFrame)
                Open();
            UpdateScrolling();
            UpdateFade();
        }

        /// <summary>Starts the chat for a new session. Call when the local player starts or joins one.</summary>
        public static void Create() {
            if (s_instance != null) return;
            var root = new GameObject(nameof(WKTChat));
            DontDestroyOnLoad(root);
            s_instance = root.AddComponent<WKTChat>();
            s_instance.m_welcomed = false;
            for (var i = 0; i < s_hostLimiters.Length; i++)
                s_hostLimiters[i]?.Reset();
        }

        /// <summary>Adds a grey line that is not a player's message, on this machine only.</summary>
        public static void PostSystem(string text) {
            if (s_instance != null)
                s_instance.AddLine($"<color={c_system_color}><i>{WKTChatText.Escape(text)}</i></color>");
        }

        /// <summary>
        /// The local player found an item nobody held before: a line in everyone's chat, the item's name in its outline's
        /// color. Nothing outside a session.
        /// </summary>
        public static void AnnounceFind(string itemName, Color color) {
            if (s_instance == null) return;
            var network = s_instance.m_network;
            var local = network.LocalPlayer;
            if (local == null) return;
            itemName = WKTChatText.Sanitize(itemName);
            if (itemName.Length == 0) return;
            s_instance.ShowFind(local.Id, local.friend.Name, itemName, color);
            var writer = network.BeginMessage(WKTMessageId.ItemFound);
            if (network.Mode == NetworkMode.Host) {
                writer.Put(local.Id);
                WriteFind(writer, itemName, color);
                network.Broadcast(writer, SendType.Reliable);
                return;
            }
            WriteFind(writer, itemName, color);
            network.SendToHost(writer, SendType.Reliable);
        }

        private static void WriteFind(NetDataWriter writer, string itemName, Color color) {
            writer.Put(itemName, WKTChatText.MAX_LENGTH);
            Color32 bytes = color;
            writer.Put(bytes.r);
            writer.Put(bytes.g);
            writer.Put(bytes.b);
        }

        private static bool TryReadFind(NetDataReader reader, out string itemName, out Color color) {
            color = Color.white;
            if (!WKTReaderUtility.TryGetBoundedString(reader, WKTChatText.MAX_LENGTH, out var raw) || !reader.TryGetByte(out var r) ||
                !reader.TryGetByte(out var g) || !reader.TryGetByte(out var b)) {
                itemName = string.Empty;
                return false;
            }
            itemName = WKTChatText.Sanitize(raw);
            color = new Color32(r, g, b, byte.MaxValue);
            return itemName.Length > 0;
        }

        /// <summary>A client found an item (to the host, which shows it and passes it on), or the host says someone did.</summary>
        private void OnItemFound(WKTPlayer sender, NetDataReader reader) {
            if (m_network.Mode == NetworkMode.Host) {
                if (sender.IsHost || !TryReadFind(reader, out var itemName, out var color)) return;
                ShowFind(sender.Id, sender.friend.Name, itemName, color);
                var relay = m_network.BeginMessage(WKTMessageId.ItemFound);
                relay.Put(sender.Id);
                WriteFind(relay, itemName, color);
                m_network.Broadcast(relay, SendType.Reliable, sender);
                return;
            }
            if (!sender.IsHost || !reader.TryGetByte(out var finderId) || !TryReadFind(reader, out var foundName, out var itemColor)) return;
            if (m_network.LocalPlayer != null && m_network.LocalPlayer.Id == finderId) return;
            var finderName = m_network.TryGetPlayer(finderId, out var finder) && finder != null ? finder.friend.Name : "Player";
            ShowFind(finderId, finderName, foundName, itemColor);
        }

        /// <summary>
        /// Shows a run event in every player's chat, this one's included. Rich text, which may use the game's text effects; put
        /// any player-chosen text through <see cref="WKTChatText.Escape"/> (or its name helpers) first. Nothing outside a
        /// session.
        /// </summary>
        public static void PostEvent(string richText) {
            if (s_instance == null || string.IsNullOrEmpty(richText) || s_instance.m_network.Mode == NetworkMode.None) return;
            var network = s_instance.m_network;
            var text = WKTChatRichText.Sanitize(richText);
            s_instance.ShowEvent(text);
            var writer = network.BeginMessage(WKTMessageId.ChatEvent);
            writer.Put(text, WKTChatRichText.MAX_LENGTH);
            if (network.Mode == NetworkMode.Host)
                network.Broadcast(writer, SendType.Reliable);
            else
                network.SendToHost(writer, SendType.Reliable);
        }

        /// <summary>The local player's name in their color, for <see cref="PostEvent"/>.</summary>
        public static string LocalName() {
            var local = s_instance != null ? s_instance.m_network.LocalPlayer : null;
            return local != null ? WKTChatText.PlayerName(local.Id, local.friend.Name) : WKTChatText.Highlight("Player");
        }

        /// <summary>A client's event (to the host, which shows it and passes it on), or the host passing one on.</summary>
        private void OnChatEvent(WKTPlayer sender, NetDataReader reader) {
            if (!WKTReaderUtility.TryGetBoundedString(reader, WKTChatRichText.MAX_LENGTH, out var raw)) return;
            var text = WKTChatRichText.Sanitize(raw);
            if (text.Length == 0) return;
            if (m_network.Mode == NetworkMode.Host) {
                if (sender.IsHost) return;
                var relay = m_network.BeginMessage(WKTMessageId.ChatEvent);
                relay.Put(text, WKTChatRichText.MAX_LENGTH);
                m_network.Broadcast(relay, SendType.Reliable, sender);
            }
            else if (!sender.IsHost) return;
            ShowEvent(text);
        }

        private void ShowEvent(string text) => AddLine($"<color={c_system_color}><i>{text}</i></color>", true);

        private void ShowFind(byte playerId, string playerName, string itemName, Color color) =>
            AddLine($"<color={c_system_color}><i><color=#{WKTPlayerColors.GetHex(playerId)}><b>{WKTChatText.Escape(playerName)}</b></color> found " +
                $"{WKTRunEvents.Article(itemName)} <color=#{ColorUtility.ToHtmlStringRGB(color)}>{WKTChatText.Escape(itemName)}</color>!</i></color>");

        #region Typing

        private bool CanOpen() =>
            !WKTKeyBinding.IsCapturing && !CommandConsole.IsConsoleVisible() && !IsOtherFieldFocused() && m_network.Mode != NetworkMode.None;

        /// <summary>Another text field, such as a name field of the game, already takes the keys.</summary>
        private bool IsOtherFieldFocused() {
            var eventSystem = EventSystem.current;
            if (eventSystem == null || eventSystem.currentSelectedGameObject == null) return false;
            return eventSystem.currentSelectedGameObject.TryGetComponent<TMP_InputField>(out var field) && field.isFocused;
        }

        private void Open() {
            m_isTyping = true;
            m_inputRoot.gameObject.SetActive(true);
            SetDraft(string.Empty);
            m_shownDraft = "\0";
            m_history.ResetCursor();
            m_relocksCursor = m_network.IsMultiplayerRun && Cursor.lockState == CursorLockMode.Locked;
            if (!m_relocksCursor) return;
            Cursor.lockState = CursorLockMode.None;
            CL_GameManager.showCursor = true;
        }

        private void Close() {
            m_isTyping = false;
            s_closedFrame = Time.frameCount;
            m_history.ResetCursor();
            m_inputRoot.gameObject.SetActive(false);
            m_scroll = 0f;
            // The log stays up for a while after typing, like after a new line.
            m_lastLineTime = Time.unscaledTime;
            if (!m_relocksCursor) return;
            m_relocksCursor = false;
            var gameManager = CL_GameManager.gMan;
            if (gameManager != null && (gameManager.isPaused || WKTMultiplayerPause.IsOpen)) return;
            Cursor.lockState = CursorLockMode.Locked;
            CL_GameManager.showCursor = false;
        }

        private void UpdateTyping() {
            // The game's console takes the keyboard when it opens, Enter included.
            if (Input.GetKeyDown(KeyCode.Escape) || CommandConsole.IsConsoleVisible()) {
                Close();
                return;
            }
            if (m_config.ChatHistoryUp.IsDown)
                FillFromHistory(m_history.Older(m_draft.ToString()));
            else if (m_config.ChatHistoryDown.IsDown)
                FillFromHistory(m_history.Newer());
            if (IsPasting())
                Append(WKTChatText.Sanitize(GUIUtility.systemCopyBuffer));
            foreach (var c in Input.inputString) {
                switch (c) {
                    case c_backspace:
                        RemoveLastChar();
                        continue;
                    case '\n':
                    case '\r':
                        Submit();
                        return;
                }
                if (!char.IsControl(c))
                    AppendChar(c);
            }
            RefreshInput();
        }

        private static bool IsPasting() =>
            Input.GetKeyDown(KeyCode.V) && (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl));

        private void FillFromHistory(string? text) {
            if (text != null)
                SetDraft(text);
        }

        private void SetDraft(string text) {
            m_draft.Clear();
            Append(text);
        }

        private void Append(string text) {
            foreach (var c in text)
                AppendChar(c);
        }

        private void AppendChar(char c) {
            if (m_draft.Length >= WKTChatText.MAX_LENGTH) return;
            m_draft.Append(c);
            m_caretTime = Time.unscaledTime;
        }

        private void RemoveLastChar() {
            var length = m_draft.Length;
            if (length == 0) return;
            // A surrogate pair goes as one character.
            var remove = length >= 2 && char.IsLowSurrogate(m_draft[length - 1]) && char.IsHighSurrogate(m_draft[length - 2]) ? 2 : 1;
            m_draft.Length -= remove;
            m_caretTime = Time.unscaledTime;
        }

        private void Submit() {
            var text = WKTChatText.Sanitize(m_draft.ToString());
            if (text.Length > 0)
                Send(text);
            Close();
        }

        /// <summary>The typed text with a blinking caret, growing upward when it wraps onto more lines.</summary>
        private void RefreshInput() {
            var caret = (int)((Time.unscaledTime - m_caretTime) / c_caret_blink_seconds) % 2 == 0;
            var draftChanged = m_draft.Length != m_shownDraft.Length || m_draft.ToString() != m_shownDraft;
            if (!draftChanged && caret == m_shownCaret) return;
            if (draftChanged)
                m_shownDraft = m_draft.ToString();
            m_shownCaret = caret;
            m_inputText.text = m_shownDraft.Length == 0 && !caret ? $"<alpha=#66><i>{c_placeholder}</i>" :
                WKTChatText.Escape(m_shownDraft) + (caret ? "|" : "<alpha=#00>|");
            var width = m_inputRoot.rect.width - c_padding * 2f;
            var height = Mathf.Max(c_input_height, m_inputText.GetPreferredValues(m_inputText.text, width, 0f).y + c_padding);
            m_inputRoot.sizeDelta = new Vector2(m_inputRoot.sizeDelta.x, height);
        }

        /// <summary>
        /// Call before the UI processes this frame's input. Turns the UI's navigation off while the chat has the keyboard, so
        /// Enter does not also press the selected button.
        /// </summary>
        public static void BeforeEventSystemUpdate(EventSystem eventSystem) {
            var chat = s_instance;
            if (chat == null) return;
            if (OwnsKeyboard) {
                if (!eventSystem.sendNavigationEvents) return;
                eventSystem.sendNavigationEvents = false;
                chat.m_disabledNavigation = true;
                return;
            }
            if (!chat.m_disabledNavigation) return;
            chat.m_disabledNavigation = false;
            eventSystem.sendNavigationEvents = true;
        }

        private void Send(string text) {
            m_history.Add(text);
            var verdict = m_limiter.TrySend(text, Time.unscaledTime);
            if (verdict != WKTChatVerdict.Allowed) {
                ShowNotice(verdict);
                return;
            }
            if (m_network.Mode == NetworkMode.Host) {
                var local = m_network.LocalPlayer;
                if (local != null)
                    Deliver(local, text);
                return;
            }
            var writer = m_network.BeginMessage(WKTMessageId.Chat);
            writer.Put(text, WKTChatText.MAX_LENGTH);
            m_network.SendToHost(writer, SendType.Reliable);
        }

        #endregion

        #region Network

        /// <summary>Host only: shows a player's message here and on every client.</summary>
        private void Deliver(WKTPlayer sender, string text) {
            ShowMessage(sender.Id, sender.friend.Name, text);
            var writer = m_network.BeginMessage(WKTMessageId.Chat);
            writer.Put(sender.Id);
            writer.Put(text, WKTChatText.MAX_LENGTH);
            m_network.Broadcast(writer, SendType.Reliable);
        }

        private void OnChatMessage(WKTPlayer sender, NetDataReader reader) {
            if (m_network.Mode == NetworkMode.Host) {
                if (sender.IsHost || !WKTReaderUtility.TryGetBoundedString(reader, WKTChatText.MAX_LENGTH, out var raw)) return;
                var text = WKTChatText.Sanitize(raw);
                if (text.Length == 0) return;
                var limiter = s_hostLimiters[sender.Id] ??= new WKTChatRateLimiter();
                var verdict = limiter.TrySend(text, Time.unscaledTime);
                if (verdict == WKTChatVerdict.Allowed) {
                    Deliver(sender, text);
                    return;
                }
                var notice = m_network.BeginMessage(WKTMessageId.ChatNotice);
                notice.Put((byte)verdict);
                m_network.SendToPlayer(sender, notice, SendType.Reliable);
                return;
            }
            if (!sender.IsHost || !reader.TryGetByte(out var playerId) ||
                !WKTReaderUtility.TryGetBoundedString(reader, WKTChatText.MAX_LENGTH, out var message) || message.Length == 0) return;
            var senderName = m_network.TryGetPlayer(playerId, out var player) && player != null ? player.friend.Name : "Player";
            ShowMessage(playerId, senderName, message);
        }

        private void OnChatNotice(WKTPlayer sender, NetDataReader reader) {
            if (!sender.IsHost || !reader.TryGetByte(out var verdict) || verdict == (byte)WKTChatVerdict.Allowed ||
                verdict > (byte)WKTChatVerdict.Duplicate) return;
            ShowNotice((WKTChatVerdict)verdict);
        }

        private void OnPlayerConnected(WKTPlayer player) {
            if (player == m_network.LocalPlayer) {
                m_welcomed = true;
                m_welcomeFrame = Time.frameCount;
                return;
            }
            // Everyone already there is announced when the local player is welcomed; only later arrivals are news.
            if (!m_welcomed && m_network.Mode == NetworkMode.Host)
                m_welcomed = true;
            if (!m_welcomed || Time.frameCount == m_welcomeFrame) return;
            PostSystem($"{player.friend.Name} joined");
        }

        private void OnPlayerDisconnected(WKTPlayer player) {
            if (player == m_network.LocalPlayer) return;
            s_hostLimiters[player.Id]?.Reset();
            PostSystem($"{player.friend.Name} left");
        }

        private void OnNetworkStopping() => Destroy(gameObject);

        #endregion

        #region Log

        private void ShowMessage(byte playerId, string senderName, string text) {
            if (m_config.CensorChat.Value)
                text = WKTChatFilter.Default.Censor(text);
            AddLine($"<color=#{WKTPlayerColors.GetHex(playerId)}><b>{WKTChatText.Escape(senderName)}</b></color>: {WKTChatText.Escape(text)}");
        }

        private void ShowNotice(WKTChatVerdict verdict) {
            var text = verdict == WKTChatVerdict.Duplicate ? "You just sent that." : "Slow down! Press Up to send that again.";
            AddLine($"<color={c_notice_color}><i>{text}</i></color>");
        }

        /// <summary>Adds a line to the log. <paramref name="animated"/> plays the game's text effects in it (event lines only).</summary>
        private void AddLine(string richText, bool animated = false) {
            ChatLine line;
            if (m_lines.Count >= MAX_LINES) {
                line = m_lines[0];
                m_lines.RemoveAt(0);
            }
            else
                line = CreateLine();
            WKTGameFont.Adopt(line.Text);
            var effects = animated ? GetEffects() : null;
            if (effects != null) {
                if (line.Animator.DatabaseBehaviors != effects)
                    line.Animator.DatabaseBehaviors = effects;
                line.Animator.enabled = true;
                line.Animator.SetText(richText);
            }
            else {
                // Off before the text changes, so a player's message is never parsed for effects. The animator draws its text
                // itself and turns the text's own drawing off once it starts up, whether or not it is enabled, which left
                // plain lines blank; they are drawn by the text again.
                line.Animator.enabled = false;
                line.Text.renderMode = TextRenderFlags.Render;
                line.Text.text = animated ? WKTChatRichText.StripEffects(richText) : richText;
            }
            line.Text.gameObject.SetActive(true);
            line.Text.transform.SetAsLastSibling();
            m_lines.Add(line);
            m_lastLineTime = Time.unscaledTime;
        }

        private void UpdateFade() {
            var target = 0f;
            var elapsed = Time.unscaledTime - m_lastLineTime;
            if (m_isTyping || elapsed < HOLD_SECONDS)
                target = 1f;
            else if (elapsed < HOLD_SECONDS + FADE_SECONDS)
                target = 1f - (elapsed - HOLD_SECONDS) / FADE_SECONDS;
            m_group.alpha = target;
            var inLobby = SceneManager.GetActiveScene().name == WKTSceneConsts.MAIN_MENU;
            var background = inLobby ? c_lobby_background_alpha : c_run_background_alpha;
            if (!Mathf.Approximately(m_background.color.a, background))
                m_background.color = new Color(0f, 0f, 0f, background);
            // The input's line is kept free even when not typing, so starting to type never shifts the log. Only a draft that
            // wraps onto more lines pushes it up.
            var inputHeight = m_isTyping ? Mathf.Max(c_input_height, m_inputRoot.rect.height) : c_input_height;
            var bottom = inputHeight + c_padding * 2f;
            if (!Mathf.Approximately(m_viewport.offsetMin.y, bottom))
                m_viewport.offsetMin = new Vector2(c_padding, bottom);
            // Only as tall as the lines shown and the input's line, so a short log has no big empty box behind it.
            var height = Mathf.Min(m_content.rect.height, m_viewport.rect.height) + c_padding * 3f + inputHeight;
            var backgroundRect = m_background.rectTransform;
            if (!Mathf.Approximately(backgroundRect.sizeDelta.y, height))
                backgroundRect.sizeDelta = new Vector2(0f, height);
        }

        private void UpdateScrolling() {
            if (m_group.alpha <= 0f && !m_isTyping) return;
            var page = m_viewport.rect.height * c_page_fraction;
            if (Input.GetKeyDown(KeyCode.PageUp))
                Scroll(page);
            else if (Input.GetKeyDown(KeyCode.PageDown))
                Scroll(-page);
            if (m_isTyping && RectTransformUtility.RectangleContainsScreenPoint(m_panel, Input.mousePosition)) {
                var wheel = Input.mouseScrollDelta.y;
                if (wheel != 0f)
                    Scroll(wheel * c_wheel_step);
            }
            var max = Mathf.Max(0f, m_content.rect.height - m_viewport.rect.height);
            m_scroll = Mathf.Clamp(m_scroll, 0f, max);
            m_content.anchoredPosition = new Vector2(0f, -m_scroll);
        }

        /// <summary>Positive scrolls toward older lines. Keeps the log up while scrolling.</summary>
        private void Scroll(float amount) {
            m_scroll += amount;
            if (!m_isTyping)
                m_lastLineTime = Time.unscaledTime;
        }

        #endregion

        #region Building

        private void Build() {
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = c_sorting_order;
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            // No raycaster: nothing of the chat ever takes clicks.
            m_group = gameObject.AddComponent<CanvasGroup>();
            m_group.blocksRaycasts = false;
            m_group.interactable = false;
            m_panel = CreateRect("Panel", transform);
            m_panel.anchorMin = m_panel.anchorMax = m_panel.pivot = Vector2.zero;
            m_panel.anchoredPosition = new Vector2(c_margin, c_margin);
            m_panel.sizeDelta = new Vector2(c_panel_width, c_panel_height);
            var backgroundRect = CreateRect("Background", m_panel);
            backgroundRect.anchorMin = Vector2.zero;
            backgroundRect.anchorMax = new Vector2(1f, 0f);
            backgroundRect.pivot = new Vector2(0.5f, 0f);
            backgroundRect.sizeDelta = Vector2.zero;
            m_background = backgroundRect.gameObject.AddComponent<Image>();
            m_background.color = Color.clear;
            m_background.raycastTarget = false;
            m_viewport = CreateRect("Viewport", m_panel);
            m_viewport.anchorMin = Vector2.zero;
            m_viewport.anchorMax = Vector2.one;
            m_viewport.offsetMin = new Vector2(c_padding, c_input_height + c_padding * 2f);
            m_viewport.offsetMax = new Vector2(-c_padding, -c_padding);
            m_viewport.gameObject.AddComponent<RectMask2D>();
            m_content = CreateRect("Content", m_viewport);
            m_content.anchorMin = Vector2.zero;
            m_content.anchorMax = new Vector2(1f, 0f);
            m_content.pivot = new Vector2(0.5f, 0f);
            m_content.sizeDelta = Vector2.zero;
            var layout = m_content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;
            layout.childAlignment = TextAnchor.LowerLeft;
            layout.spacing = 2f;
            m_content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            BuildInput();
        }

        private void BuildInput() {
            m_inputRoot = CreateRect("Input", m_panel);
            m_inputRoot.gameObject.SetActive(false);
            m_inputRoot.anchorMin = Vector2.zero;
            m_inputRoot.anchorMax = new Vector2(1f, 0f);
            m_inputRoot.pivot = new Vector2(0.5f, 0f);
            m_inputRoot.offsetMin = new Vector2(c_padding, c_padding);
            m_inputRoot.offsetMax = new Vector2(-c_padding, c_padding + c_input_height);
            var background = m_inputRoot.gameObject.AddComponent<Image>();
            background.color = new Color(0f, 0f, 0f, c_input_background_alpha);
            background.raycastTarget = false;
            m_inputText = CreateText("Text", m_inputRoot);
            m_inputText.rectTransform.offsetMin = new Vector2(c_padding, 0f);
            m_inputText.rectTransform.offsetMax = new Vector2(-c_padding, 0f);
            m_inputText.textWrappingMode = TextWrappingModes.Normal;
            m_inputText.richText = true;
        }

        private ChatLine CreateLine() {
            var text = CreateText("Line", m_content);
            text.textWrappingMode = TextWrappingModes.Normal;
            text.richText = true;
            text.alignment = TextAlignmentOptions.BottomLeft;
            var animator = text.gameObject.AddComponent<TextAnimator_TMP>();
            animator.enabled = false;
            animator.typewriterStartsAutomatically = false;
            animator.timeScale = TimeScale.Unscaled;
            // Lines show at once: no letters growing in or fading out.
            animator.DefaultAppearancesTags = Array.Empty<string>();
            animator.DefaultDisappearancesTags = Array.Empty<string>();
            return new ChatLine(text, animator);
        }

        private static TextMeshProUGUI CreateText(string name, Transform parent) {
            var rect = CreateRect(name, parent);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            WKTGameFont.Adopt(text);
            text.fontSize = c_font_size;
            text.color = Color.white;
            text.raycastTarget = false;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.alignment = TextAlignmentOptions.MidlineLeft;
            return text;
        }

        private static RectTransform CreateRect(string name, Transform parent) {
            var obj = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)obj.transform;
            rect.SetParent(parent, false);
            return rect;
        }

        #endregion

        /// <summary>
        /// The game's text effects database: Text Animator's default one if it has the game's effects, else whichever loaded
        /// database does. Looked up once.
        /// </summary>
        private static AnimationsDatabase? GetEffects() {
            if (s_lookedUpEffects) return s_effects;
            s_lookedUpEffects = true;
            try {
                var settings = TextAnimatorSettings.Instance;
                var fallback = settings != null ? settings.behaviors.defaultDatabase : null;
                if (fallback != null && fallback.ContainsKey(c_probe_effect))
                    s_effects = fallback;
                else {
                    foreach (var database in Resources.FindObjectsOfTypeAll<AnimationsDatabase>()) {
                        if (database == null || !database.ContainsKey(c_probe_effect)) continue;
                        s_effects = database;
                        break;
                    }
                }
            }
            catch (Exception e) {
                WKT.Instance.Logger.LogWarning($"Could not find the game's text effects: {e.Message}");
            }
            WKT.Instance.Logger.LogInfo(s_effects != null ? $"Chat text effects from {s_effects.name}." : "No text effects found; event lines show without them.");
            return s_effects;
        }

        private sealed class ChatLine {
            public readonly TextMeshProUGUI Text;
            public readonly TextAnimator_TMP Animator;

            public ChatLine(TextMeshProUGUI text, TextAnimator_TMP animator) {
                Text = text;
                Animator = animator;
            }
        }
    }
}
