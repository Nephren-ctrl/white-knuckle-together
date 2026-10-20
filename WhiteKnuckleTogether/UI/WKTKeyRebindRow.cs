using System;
using JetBrains.Annotations;
using Rewired;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// A row of the game's Controls tab that rebinds one <see cref="WKTKeyBinding"/>. Cloned from one of the game's own rebind
    /// rows with its Rewired binder stripped. Clicking the key captures the next key or extra mouse button pressed, Escape
    /// cancels, and the row's reset and remove buttons restore the default key or clear it.
    /// </summary>
    internal sealed class WKTKeyRebindRow : MonoBehaviour {
        public const float CAPTURE_TIMEOUT = 5f;
        private const string c_listening_text = "Listening...";
        private const string c_ui_map_category = "UI";
        /// <summary>Every key and mouse button, but no joystick buttons: <c>KeyCode.JoystickButton0</c> starts them.</summary>
        private static readonly KeyCode[] s_capturableKeys = BuildCapturableKeys();
        /// <summary>The frame the last capture ended. The key that ended it must not also close the settings menu.</summary>
        private static int s_captureEndFrame = -1;
        private WKTKeyBinding? m_binding;
        private TMP_Text? m_keyText;
        private float m_captureStartTime;
        private int m_captureStartFrame;
        private bool m_isCapturing;

        /// <summary>A capture is running or just ended, so Escape belongs to it and not to the settings menu.</summary>
        public static bool OwnsEscape => WKTKeyBinding.IsCapturing || Time.frameCount <= s_captureEndFrame + 1;

        [UsedImplicitly]
        private void OnDisable() {
            if (m_isCapturing)
                EndCapture();
        }

        [UsedImplicitly]
        private void Update() {
            if (!m_isCapturing || Time.frameCount == m_captureStartFrame) return;
            if (Time.unscaledTime - m_captureStartTime > CAPTURE_TIMEOUT || Input.GetKeyDown(KeyCode.Escape)) {
                EndCapture();
                return;
            }
            if (!TryGetPressedKey(out var key)) return;
            m_binding!.Key = key;
            EndCapture();
        }

        /// <summary>Binds this row to <paramref name="binding"/>. <paramref name="keyText"/> shows the bound key on the rebind button.</summary>
        public void Setup(WKTKeyBinding binding, Button rebindButton, Button? resetButton, Button? removeButton, TMP_Text keyText) {
            m_binding = binding;
            m_keyText = keyText;
            rebindButton.onClick.AddListener(StartCapture);
            if (resetButton != null)
                resetButton.onClick.AddListener(ResetToDefault);
            if (removeButton != null)
                removeButton.onClick.AddListener(() => SetKey(KeyCode.None));
            RefreshText();
        }

        /// <summary>A short name for <paramref name="key"/> as the Controls tab shows it.</summary>
        public static string FormatKey(KeyCode key) {
            switch (key) {
                case KeyCode.None: return "None";
                case KeyCode.Return: return "Enter";
                case KeyCode.KeypadEnter: return "Keypad Enter";
                case KeyCode.UpArrow: return "Up";
                case KeyCode.DownArrow: return "Down";
                case KeyCode.LeftArrow: return "Left";
                case KeyCode.RightArrow: return "Right";
                case KeyCode.Mouse0: return "Left Mouse";
                case KeyCode.Mouse1: return "Right Mouse";
                case KeyCode.Mouse2: return "Middle Mouse";
                case KeyCode.LeftControl: return "Left Ctrl";
                case KeyCode.RightControl: return "Right Ctrl";
                case KeyCode.BackQuote: return "`";
            }
            if (key >= KeyCode.Alpha0 && key <= KeyCode.Alpha9)
                return ((char)('0' + (key - KeyCode.Alpha0))).ToString();
            if (key >= KeyCode.Mouse3 && key <= KeyCode.Mouse6)
                return "Mouse " + (key - KeyCode.Mouse0 + 1);
            return SpaceWords(key.ToString());
        }

        /// <summary>Whether <paramref name="key"/> may be bound. The left and right mouse buttons are the hands.</summary>
        public static bool IsBindable(KeyCode key) => key != KeyCode.None && key != KeyCode.Mouse0 && key != KeyCode.Mouse1 &&
                                                     key != KeyCode.Escape && key < KeyCode.JoystickButton0;

        public void ResetToDefault() {
            if (m_binding != null)
                SetKey(m_binding.DefaultKey);
        }

        private void SetKey(KeyCode key) {
            if (m_binding == null) return;
            m_binding.Key = key;
            RefreshText();
        }

        private void StartCapture() {
            if (m_isCapturing || WKTKeyBinding.IsCapturing) return;
            m_isCapturing = true;
            WKTKeyBinding.IsCapturing = true;
            m_captureStartTime = Time.unscaledTime;
            m_captureStartFrame = Time.frameCount;
            SetUIMapsEnabled(false);
            var overlay = UI_RebindOverlay.rebindOverlayInstance;
            if (overlay != null) {
                overlay.gameObject.SetActive(true);
                overlay.rebindText.text = c_listening_text;
            }
        }

        private void EndCapture() {
            m_isCapturing = false;
            WKTKeyBinding.IsCapturing = false;
            s_captureEndFrame = Time.frameCount;
            SetUIMapsEnabled(true);
            var overlay = UI_RebindOverlay.rebindOverlayInstance;
            if (overlay != null && overlay.gameObject.activeInHierarchy)
                overlay.Hide();
            RefreshText();
        }

        private void RefreshText() {
            if (m_keyText != null && m_binding != null)
                m_keyText.text = FormatKey(m_binding.Key);
        }

        private static bool TryGetPressedKey(out KeyCode key) {
            foreach (var candidate in s_capturableKeys) {
                if (!Input.GetKeyDown(candidate)) continue;
                key = candidate;
                return true;
            }
            key = KeyCode.None;
            return false;
        }

        /// <summary>The menu's own navigation would otherwise also act on the captured key, as in the game's own rebinding.</summary>
        private static void SetUIMapsEnabled(bool enabled) {
            if (!ReInput.isReady) return;
            var player = InputManager.GetPlayerInput();
            player?.controllers.maps.SetMapsEnabled(enabled, c_ui_map_category);
        }

        private static KeyCode[] BuildCapturableKeys() {
            var values = (KeyCode[])Enum.GetValues(typeof(KeyCode));
            return Array.FindAll(values, IsBindable);
        }

        private static string SpaceWords(string name) {
            var chars = new char[name.Length * 2];
            var length = 0;
            for (var i = 0; i < name.Length; i++) {
                if (i > 0 && char.IsUpper(name[i]) && !char.IsUpper(name[i - 1]))
                    chars[length++] = ' ';
                chars[length++] = name[i];
            }
            return new string(chars, 0, length);
        }
    }
}
