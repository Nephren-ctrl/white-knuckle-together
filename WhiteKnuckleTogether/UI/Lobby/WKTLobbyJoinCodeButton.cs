using JetBrains.Annotations;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// The lobby button showing a join code lobby's code, for everyone in it. Clicking it copies the code to share. Hidden in
    /// other lobbies, and until the host has picked the code.
    /// </summary>
    internal sealed class WKTLobbyJoinCodeButton : MonoBehaviour {
        public const string LABEL = "Code";
        private TMP_Text? m_label;
        private CanvasGroup? m_group;
        private LayoutElement? m_layout;

        [UsedImplicitly]
        private void Awake() {
            m_label = GetComponentInChildren<TMP_Text>(true);
            // Hidden through these rather than SetActive, which Unity refuses while the lobby panel is being activated.
            m_group = gameObject.AddComponent<CanvasGroup>();
            m_layout = gameObject.AddComponent<LayoutElement>();
            // Added here, not where the button is made: the game clones the lobby panel and a clone only keeps what is serialized.
            GetComponent<Button>().onClick.AddListener(OnClick);
            var network = WKT.Instance.Network;
            network.OnHostStarted += Show;
            network.OnClientStarted += Show;
            network.OnLobbyJoinCodeChanged += Show;
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
            wkt.Network.OnLobbyJoinCodeChanged -= Show;
            wkt.Network.OnNetworkStopping -= Show;
            wkt.Network.OnHostChanged -= Show;
        }

        private void Show() {
            var code = WKT.Instance.Network.LobbyJoinCode;
            var shown = code.Length > 0;
            if (m_label != null)
                m_label.text = LABEL + " " + code;
            if (m_group != null) {
                m_group.alpha = shown ? 1f : 0f;
                m_group.interactable = shown;
                m_group.blocksRaycasts = shown;
            }
            if (m_layout != null)
                m_layout.ignoreLayout = !shown;
        }

        private static void OnClick() {
            var code = WKT.Instance.Network.LobbyJoinCode;
            if (code.Length == 0) return;
            GUIUtility.systemCopyBuffer = code;
            WKTToast.Show($"Copied the lobby code {code}.");
        }
    }
}
