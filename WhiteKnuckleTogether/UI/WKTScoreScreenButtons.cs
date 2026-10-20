using System.Collections.Generic;
using JetBrains.Annotations;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// The end of run score screen of a multiplayer run. Its restart and main menu buttons would only act on this machine,
    /// so they are rewired: the host restarts the run for everyone, or takes everyone back to the lobby. Clients cannot
    /// restart, and their main menu button disconnects. Buttons are found by what they call, not where they sit, and
    /// follow the host changing.
    /// </summary>
    internal sealed class WKTScoreScreenButtons : MonoBehaviour {
        private const string c_restart_method = "RestartScene";
        private const string c_main_menu_method = "LoadMainMenu";
        private const string c_host_leave_text = "Back to Lobby";
        private const string c_client_leave_text = "Disconnect";
        private readonly List<Button> m_restartButtons = new();
        private readonly List<TMP_Text?> m_leaveTexts = new();
        private WKTNetwork m_network = null!;

        [UsedImplicitly]
        private void Awake() {
            m_network = WKT.Instance.Network;
            // Modify would change the gamemode's settings and restart on this machine. A multiplayer run's settings are the
            // lobby's, so the screen hides them when it shows.
            var screen = GetComponent<UI_ScoreScreen>();
            if (screen != null)
                screen.showSettingsGameObjects = false;
            RewireButtons();
            m_network.OnHostChanged += Refresh;
            Refresh();
        }

        [UsedImplicitly]
        private void OnDestroy() {
            if (WKT.InstanceOrNull != null)
                WKT.InstanceOrNull.Network.OnHostChanged -= Refresh;
        }

        /// <summary>Rewires <paramref name="screen"/>'s buttons, once.</summary>
        public static void Attach(UI_ScoreScreen screen) {
            if (screen.GetComponent<WKTScoreScreenButtons>() == null)
                screen.gameObject.AddComponent<WKTScoreScreenButtons>();
        }

        private void RewireButtons() {
            var logger = WKT.Instance.Logger;
            foreach (var button in GetComponentsInChildren<Button>(true)) {
                var onClick = button.onClick;
                var isRestart = false;
                var isLeave = false;
                for (var i = 0; i < onClick.GetPersistentEventCount(); i++) {
                    var method = onClick.GetPersistentMethodName(i);
                    logger.LogInfo($"Score screen button '{button.name}' calls {method}.");
                    switch (method) {
                        case c_restart_method:
                            isRestart = true;
                            break;
                        case c_main_menu_method:
                            isLeave = true;
                            break;
                        default:
                            continue;
                    }
                    onClick.SetPersistentListenerState(i, UnityEventCallState.Off);
                }
                if (isRestart) {
                    onClick.AddListener(OnRestartClicked);
                    m_restartButtons.Add(button);
                }
                if (!isLeave) continue;
                onClick.AddListener(OnLeaveClicked);
                m_leaveTexts.Add(button.GetComponentInChildren<TMP_Text>(true));
            }
            if (m_restartButtons.Count == 0)
                logger.LogWarning("Found no restart button on the score screen.");
            if (m_leaveTexts.Count == 0)
                logger.LogWarning("Found no main menu button on the score screen.");
        }

        /// <summary>Also called when another player takes over as host, which this player may now be.</summary>
        private void Refresh() {
            var isHost = m_network.Mode == NetworkMode.Host;
            foreach (var button in m_restartButtons)
                button.gameObject.SetActive(isHost);
            foreach (var text in m_leaveTexts) {
                if (text != null)
                    text.text = isHost ? c_host_leave_text : c_client_leave_text;
            }
        }

        private void OnRestartClicked() {
            if (m_network.Mode == NetworkMode.Host)
                m_network.RestartRunForAll();
        }

        private void OnLeaveClicked() {
            if (m_network.Mode == NetworkMode.Host)
                m_network.ReturnAllToLobby();
            else
                m_network.DisconnectAndReturnToMenu();
        }
    }
}
