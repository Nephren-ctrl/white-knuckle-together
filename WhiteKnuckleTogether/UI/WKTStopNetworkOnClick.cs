using JetBrains.Annotations;
using UnityEngine;
using UnityEngine.UI;

namespace WhiteKnuckleTogether {
    internal sealed class WKTStopNetworkOnClick : MonoBehaviour {
        [UsedImplicitly]
        private void Awake() => GetComponent<Button>().onClick.AddListener(StopNetwork);

        private static void StopNetwork() => WKT.Instance.Network.Stop();
    }
}