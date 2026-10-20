using JetBrains.Annotations;
using UnityEngine;
using UnityEngine.UI;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// Opens or closes a <see cref="WKTLobbyPopup"/> when its button is clicked. A component, not a listener added in code,
    /// because the game clones the lobby panel and a clone only keeps what is serialized.
    /// </summary>
    internal sealed class WKTLobbyPopupButton : MonoBehaviour {
        public WKTLobbyPopup? popup;
        /// <summary>Whether a click shows the popup. Otherwise it hides it.</summary>
        public bool opens;

        [UsedImplicitly]
        private void Awake() => GetComponent<Button>().onClick.AddListener(OnClick);

        private void OnClick() {
            if (popup == null) return;
            if (opens)
                popup.Show();
            else
                popup.Hide();
        }
    }
}
