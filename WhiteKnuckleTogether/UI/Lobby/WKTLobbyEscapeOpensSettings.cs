using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using JetBrains.Annotations;
using UnityEngine;
using UnityEngine.UI;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// On the lobby's Back button, in place of the game's <see cref="UI_CloseButtonOnBack"/>: Escape opens the settings over
    /// the lobby instead of pressing Back, which would leave it. Clicking Back still leaves. The lobby stays non-interactable
    /// behind the settings, and closing them hands control back to the lobby rather than to the main menu, as the settings
    /// screen would on its own.
    /// </summary>
    internal sealed class WKTLobbyEscapeOpensSettings : MonoBehaviour {
        private static readonly AccessTools.FieldRef<UI_MenuScreen, UI_Menu> s_screenMenu = AccessTools.FieldRefAccess<UI_MenuScreen, UI_Menu>("menu");
        private static readonly List<UI_CloseButtonOnBack> s_otherBackButtons = new();
        private Button m_back = null!;
        private UI_MenuScreen? m_settings;
        private CanvasGroup? m_lobbyGroup;
        private bool m_settingsOpen;

        [UsedImplicitly]
        private void Awake() => m_back = GetComponent<Button>();

        [UsedImplicitly]
        private void OnDisable() {
            // The lobby closed under the settings: closing them now just returns to the main menu.
            StopAllCoroutines();
            if (m_settingsOpen && m_settings != null)
                m_settings.closeEvent.RemoveListener(OnSettingsClosed);
            m_settingsOpen = false;
        }

        [UsedImplicitly]
        private void Update() {
            if (m_settingsOpen || !Input.GetKeyDown(KeyCode.Escape) && !InputManager.GetButton("UIBackButton").Down) return;
            if (WKTChat.OwnsKeyboard || WKTKeyRebindRow.OwnsEscape || !m_back.IsInteractable() || IsTakenByAnotherWindow()) return;
            // A frame later, like the game's own back buttons, so the settings' back button does not see this press and close them.
            StartCoroutine(OpenSettingsNextFrame());
        }

        private IEnumerator OpenSettingsNextFrame() {
            yield return null;
            if (m_settings == null) {
                var menu = FindAnyObjectByType<UI_SettingsMenu>(FindObjectsInactive.Include);
                m_settings = menu != null ? menu.GetComponent<UI_MenuScreen>() : null;
            }
            var lobbyScreen = GetComponentInParent<UI_MenuScreen>();
            m_lobbyGroup = lobbyScreen != null ? lobbyScreen.GetComponent<CanvasGroup>() : null;
            if (m_settings == null || m_lobbyGroup == null) {
                WKT.Instance.Logger.LogWarning("Could not open the settings from the lobby: the settings or play screen is missing.");
                yield break;
            }
            m_lobbyGroup.interactable = false;
            m_settings.closeEvent.AddListener(OnSettingsClosed);
            m_settingsOpen = true;
            m_settings.Open();
        }

        /// <summary>After the settings screen has handed control to the main menu: takes it back for the lobby.</summary>
        private void OnSettingsClosed() {
            m_settings!.closeEvent.RemoveListener(OnSettingsClosed);
            m_settingsOpen = false;
            var mainMenu = s_screenMenu(m_settings);
            if (mainMenu != null && mainMenu.TryGetComponent<CanvasGroup>(out var mainMenuGroup))
                mainMenuGroup.interactable = false;
            if (m_lobbyGroup != null)
                m_lobbyGroup.interactable = true;
        }

        /// <summary>A window open over the lobby (a trinket picker) closes on Escape itself.</summary>
        private bool IsTakenByAnotherWindow() {
            var panel = GetComponentInParent<UI_GamemodeScreen_Panel>();
            if (panel == null) return false;
            panel.GetComponentsInChildren(false, s_otherBackButtons);
            var taken = false;
            foreach (var other in s_otherBackButtons) {
                if (other.TryGetComponent<Button>(out var button) && button.IsInteractable()) {
                    taken = true;
                    break;
                }
            }
            s_otherBackButtons.Clear();
            return taken;
        }
    }
}
