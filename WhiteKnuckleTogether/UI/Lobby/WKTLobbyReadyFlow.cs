using System;
using JetBrains.Annotations;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// The lobby's way into a run. Only the host has the first screen's button into the trinket screen, and pressing it takes
    /// every player there, late joiners included (<see cref="WKTNetwork.LobbyPicking"/>); only the host can take them back.
    /// There, the screen's Play button is a Ready button for everyone. Once every player is ready, the run starts after a
    /// countdown the button shows (<see cref="WKTReadyCheck"/>).
    /// </summary>
    internal sealed class WKTLobbyReadyFlow : MonoBehaviour {
        private const string c_wait_label = "Waiting for everyone to ready up";
        [SerializeField] private UI_GamemodeScreen_Panel? m_panel;
        [SerializeField] private Button? m_escape;
        [SerializeField] private Button? m_ready;
        [SerializeField] private TMP_Text? m_readyText;
        [SerializeField] private Button? m_back;
        private WKTNetwork m_network = null!;
        private Action m_onReadyChanged = null!;
        private Action m_onPickingChanged = null!;
        private Action<WKTPlayer> m_onPlayer = null!;
        private int m_readyCount;
        private int m_total;
        private int m_shownSeconds = -1;

        [UsedImplicitly]
        private void Awake() {
            m_network = WKT.Instance.Network;
            m_onReadyChanged = OnReadyChanged;
            m_onPickingChanged = ApplyPicking;
            m_onPlayer = _ => OnReadyChanged();
            if (m_ready != null) {
                // The game's own listener would load the gamemode at once.
                m_ready.onClick = new Button.ButtonClickedEvent();
                m_ready.onClick.AddListener(ToggleReady);
            }
            // After the game's own listeners, which open and close the trinket screen on the host.
            if (m_escape != null)
                m_escape.onClick.AddListener(() => m_network.SetLobbyPicking(true));
            if (m_back != null)
                m_back.onClick.AddListener(OnBack);
        }

        [UsedImplicitly]
        private void OnEnable() {
            m_network.OnReadyChanged += m_onReadyChanged;
            m_network.OnLobbyPickingChanged += m_onPickingChanged;
            m_network.OnPlayerConnected += m_onPlayer;
            m_network.OnPlayerDisconnected += m_onPlayer;
            m_network.OnHostChanged += m_onPickingChanged;
            ApplyPicking();
            OnReadyChanged();
        }

        /// <summary>Again after the game's own set up of a new panel, which opens it on its first screen.</summary>
        [UsedImplicitly]
        private void Start() => ApplyPicking();

        [UsedImplicitly]
        private void OnDisable() {
            m_network.OnReadyChanged -= m_onReadyChanged;
            m_network.OnLobbyPickingChanged -= m_onPickingChanged;
            m_network.OnPlayerConnected -= m_onPlayer;
            m_network.OnPlayerDisconnected -= m_onPlayer;
            m_network.OnHostChanged -= m_onPickingChanged;
            m_network.SetLocalReady(false);
        }

        [UsedImplicitly]
        private void Update() {
            if (!m_network.LobbyPicking) return;
            var left = m_network.CountdownLeft;
            if (left >= 0f) {
                var seconds = WKTReadyCheck.ShownSeconds(left);
                if (seconds != m_shownSeconds) {
                    m_shownSeconds = seconds;
                    m_readyText?.SetText("STARTING IN {0}", seconds);
                }
                return;
            }
            if (m_network.IsLocalReady)
                WKTWaitHud.Report(c_wait_label, m_readyCount, m_total);
        }

        public void SetupReferences(UI_GamemodeScreen_Panel panel, Button? escape, Button? ready, TMP_Text? readyText, Button? back) {
            m_panel = panel;
            m_escape = escape;
            m_ready = ready;
            m_readyText = readyText;
            m_back = back;
        }

        private void ToggleReady() => m_network.SetLocalReady(!m_network.IsLocalReady);

        /// <summary>The host left the trinket screen: everyone goes back with them, unready.</summary>
        private void OnBack() {
            m_network.SetLocalReady(false);
            m_network.SetLobbyPicking(false);
        }

        private void OnReadyChanged() {
            m_readyCount = m_network.CountReady(out m_total);
            m_shownSeconds = -1;
            if (m_readyText == null || m_network.CountdownLeft >= 0f) return;
            m_readyText.SetText(m_network.IsLocalReady ? "UNREADY {0}/{1}" : "READY {0}/{1}", m_readyCount, m_total);
        }

        /// <summary>
        /// Only the host moves the lobby between its screens. Clients have neither the way in nor the way back, and show the
        /// screen the host is on.
        /// </summary>
        private void ApplyPicking() {
            var isHost = m_network.Mode == NetworkMode.Host;
            if (m_escape != null)
                m_escape.gameObject.SetActive(isHost);
            if (m_back != null)
                m_back.gameObject.SetActive(isHost);
            if (isHost || m_panel == null || m_panel.newGameSettingsPanel == null || m_panel.gamemodeInfoPanel == null) return;
            var picking = m_network.LobbyPicking;
            m_panel.newGameSettingsPanel.SetActive(picking);
            m_panel.gamemodeInfoPanel.SetActive(!picking);
            if (!picking)
                m_network.SetLocalReady(false);
        }
    }
}
