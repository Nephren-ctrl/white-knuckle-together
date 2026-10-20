using JetBrains.Annotations;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// The lobby button that opens the shortcut list. While no shortcut is unlocked by every player it reads as redacted and
    /// cannot be clicked, so it gives away nothing about shortcuts nobody has found.
    /// </summary>
    internal sealed class WKTLobbyShortcutsButton : MonoBehaviour {
        public const string LABEL = "Shortcuts";
        private Button? m_button;
        private TMP_Text? m_label;

        [UsedImplicitly]
        private void Awake() {
            m_button = GetComponent<Button>();
            m_label = GetComponentInChildren<TMP_Text>(true);
            var network = WKT.Instance.Network;
            network.OnHostStarted += Show;
            network.OnClientStarted += Show;
            network.OnLobbyShortcutsChanged += Show;
            network.OnNetworkStopping += Show;
            network.OnHostChanged += Show;
        }

        [UsedImplicitly]
        private void OnEnable() => Show();

        [UsedImplicitly]
        private void OnDestroy() {
            var wkt = WKT.InstanceOrNull;
            if (wkt == null) return;
            wkt.Network.OnHostStarted -= Show;
            wkt.Network.OnClientStarted -= Show;
            wkt.Network.OnLobbyShortcutsChanged -= Show;
            wkt.Network.OnNetworkStopping -= Show;
            wkt.Network.OnHostChanged -= Show;
        }

        private void Show() {
            var anyAvailable = WKT.Instance.Network.LobbyShortcutsAvailable != 0;
            if (m_button != null)
                m_button.interactable = anyAvailable;
            if (m_label != null)
                m_label.text = anyAvailable ? LABEL : WKTShortcuts.REDACTED;
        }
    }
}
