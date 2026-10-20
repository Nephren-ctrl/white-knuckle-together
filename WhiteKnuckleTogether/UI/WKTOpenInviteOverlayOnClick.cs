using JetBrains.Annotations;
using UnityEngine;
using UnityEngine.UI;

namespace WhiteKnuckleTogether {
    internal sealed class WKTOpenInviteOverlayOnClick : MonoBehaviour {
        [UsedImplicitly]
        private void Awake() => GetComponent<Button>().onClick.AddListener(OpenInviteOverlay);

        private static void OpenInviteOverlay() => WKT.Instance.Network.OpenInviteOverlay();
    }
}
