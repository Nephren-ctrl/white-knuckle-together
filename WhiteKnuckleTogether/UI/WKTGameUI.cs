using JetBrains.Annotations;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace WhiteKnuckleTogether {
    public sealed class WKTGameUI : MonoBehaviour {
        private const string c_pause_menu = "Canvas/Pause/Pause Menu";
        private const string c_layout = "Pause Buttons/Pause Layout";
        private const string c_exit = c_layout + "/Exit";
        /// <summary>The spacer between the pause buttons' rows.</summary>
        private const string c_gap = c_layout + "/Gap.01";
        private const string c_verify_exit = "Verify-Exit";
        /// <summary>The game's restart confirmation, which the host's Back to Lobby button opens instead.</summary>
        private const string c_verify_lobby = "Verify-Restart";
        private TMP_Text? m_verifyExitTitle;
        private GameObject? m_backToLobby;
        private GameObject? m_backToLobbyGap;

        [UsedImplicitly]
        private void Awake() {
            WKT.Instance.Logger.LogInfo("Initializing game UI.");
            var gameManager = CL_GameManager.gMan != null ? CL_GameManager.gMan.gameObject : GameObject.Find("GameManager");
            if (gameManager == null) {
                WKT.Instance.Logger.LogError("Failed to find GameManager.");
                return;
            }
            if (!WKTUIPath.TryFind(gameManager.transform, c_pause_menu, out var pauseMenu, true)) return;
            ModifyEndRunOptions(pauseMenu);
            ModifyVerifyExitTitle(pauseMenu);
            HookRunExit(pauseMenu);
            HookBackToLobby(pauseMenu);
            WKT.Instance.Network.OnHostChanged += RefreshForHost;
        }

        [UsedImplicitly]
        private void OnDestroy() {
            if (WKT.InstanceOrNull != null)
                WKT.InstanceOrNull.Network.OnHostChanged -= RefreshForHost;
        }

        /// <summary>
        /// The game's Restart button becomes the host's Back to Lobby, which opens the restart confirmation (see
        /// <see cref="HookBackToLobby"/>). It moves out of the exit row to the bottom of the pause buttons, under
        /// Disconnect and spaced like the rows above, and is shown only to the host.
        /// </summary>
        private void ModifyEndRunOptions(Transform pauseMenu) {
            if (!WKTUIPath.TryFind<RectTransform>(pauseMenu, c_layout + "/Settings", out var settingsRect, true)) return;
            if (WKTUIPath.TryFind<RectTransform>(pauseMenu, c_exit + "/Restart", out var restartRect, true) &&
                WKTUIPath.TryFind(pauseMenu, c_exit, out var exitRow, true) && WKTUIPath.TryFind(pauseMenu, c_gap, out var gap, true)) {
                var layout = exitRow.parent;
                m_backToLobbyGap = Instantiate(gap.gameObject, layout, false);
                m_backToLobbyGap.transform.SetSiblingIndex(exitRow.GetSiblingIndex() + 1);
                restartRect.SetParent(layout, false);
                restartRect.SetSiblingIndex(m_backToLobbyGap.transform.GetSiblingIndex() + 1);
                restartRect.sizeDelta = settingsRect.sizeDelta;
                var restartText = restartRect.GetComponentInChildren<TMP_Text>(true);
                if (restartText != null)
                    restartText.text = "Back to Lobby";
                m_backToLobby = restartRect.gameObject;
            }
            if (!WKTUIPath.TryFind<RectTransform>(pauseMenu, c_exit + "/Main Menu Button", out var endRunButtonRect, true) ||
                !WKTUIPath.TryFind<RectTransform>(endRunButtonRect, "Main Menu", out var endRunRect, true))
                return;
            endRunButtonRect.sizeDelta = settingsRect.sizeDelta;
            endRunRect.sizeDelta = settingsRect.sizeDelta;
            var endRunText = endRunRect.GetComponentInChildren<TMP_Text>(true);
            if (endRunText != null)
                endRunText.text = "Disconnect";
            WKT.Instance.Logger.LogInfo("Successfully modified End Run options.");
        }

        private void ModifyVerifyExitTitle(Transform pauseMenu) {
            if (WKTUIPath.TryFind(pauseMenu, c_verify_exit + "/Overview Titles/Title Text", out m_verifyExitTitle, true))
                WKT.Instance.Logger.LogInfo("Successfully modified verify exit title.");
            RefreshForHost();
        }

        /// <summary>Also called when another player takes over as host, which this player may now be.</summary>
        private void RefreshForHost() {
            var isHost = WKT.Instance.Network.Mode == NetworkMode.Host;
            if (m_backToLobby != null)
                m_backToLobby.SetActive(isHost);
            if (m_backToLobbyGap != null)
                m_backToLobbyGap.SetActive(isHost);
            if (m_verifyExitTitle == null) return;
            switch (WKT.Instance.Network.Mode) {
                case NetworkMode.Host:
                    m_verifyExitTitle.text = "ARE YOU SURE? <color=red>YOU ARE THE HOST.";
                    break;
                case NetworkMode.Client:
                    m_verifyExitTitle.text = "ARE YOU SURE?";
                    break;
            }
        }

        private void HookRunExit(Transform pauseMenu) {
            if (!WKTUIPath.TryFind<Button>(pauseMenu, c_verify_exit + "/Tab Selection Hor/Exit", out var exitButton, true)) return;
            exitButton.onClick.AddListener(OnDisconnectClicked);
            WKT.Instance.Logger.LogInfo("Successfully hooked onto run exit.");
        }

        private void OnDisconnectClicked() => WKT.Instance.Network.Stop();

        /// <summary>The restart confirmation asks about taking everyone back instead, and its button only does that.</summary>
        private static void HookBackToLobby(Transform pauseMenu) {
            if (WKTUIPath.TryFind<TMP_Text>(pauseMenu, c_verify_lobby + "/Overview Titles/Title Text", out var title, true))
                title.text = "<color=red>END THE RUN FOR EVERYONE?";
            if (!WKTUIPath.TryFind<Button>(pauseMenu, c_verify_lobby + "/Tab Selection Hor/Exit", out var confirmButton, true)) return;
            var confirmText = confirmButton.GetComponentInChildren<TMP_Text>(true);
            if (confirmText != null)
                confirmText.text = "Lobby";
            confirmButton.onClick = new Button.ButtonClickedEvent();
            confirmButton.onClick.AddListener(OnBackToLobbyClicked);
            WKT.Instance.Logger.LogInfo("Successfully hooked Back to Lobby.");
        }

        private static void OnBackToLobbyClicked() => WKT.Instance.Network.ReturnAllToLobby();
    }
}
