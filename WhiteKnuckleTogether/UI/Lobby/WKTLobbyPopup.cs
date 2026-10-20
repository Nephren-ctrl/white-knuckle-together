using JetBrains.Annotations;
using UnityEngine;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// A window shown over the lobby, such as the shortcut list. Its backdrop covers the lobby so nothing behind it can be
    /// clicked; clicking the backdrop or the window's close button hides it. It also hides when the lobby goes away.
    /// </summary>
    internal sealed class WKTLobbyPopup : MonoBehaviour {
        [UsedImplicitly]
        private void Awake() => WKT.Instance.Network.OnNetworkStopping += Hide;

        [UsedImplicitly]
        private void OnDestroy() {
            var wkt = WKT.InstanceOrNull;
            if (wkt != null)
                wkt.Network.OnNetworkStopping -= Hide;
        }

        public void Show() {
            gameObject.SetActive(true);
            // Drawn over every other part of the lobby.
            transform.SetAsLastSibling();
        }

        public void Hide() => gameObject.SetActive(false);
    }
}
