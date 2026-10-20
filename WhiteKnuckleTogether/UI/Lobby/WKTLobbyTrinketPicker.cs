using JetBrains.Annotations;
using UnityEngine;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// Sets up the multiplayer lobby's trinket picker whenever it is shown, as the vanilla panel's open event does, and
    /// refreshes it when the trinkets everyone has unlocked change (see <see cref="WKTTrinkets"/>).
    /// </summary>
    internal sealed class WKTLobbyTrinketPicker : MonoBehaviour {
        private UI_TrinketPicker m_picker = null!;
        private bool m_initialized;

        [UsedImplicitly]
        private void Awake() {
            m_picker = GetComponent<UI_TrinketPicker>();
            WKT.Instance.Network.OnLobbyTrinketsChanged += OnTrinketsChanged;
        }

        /// <summary>After the picker's own OnEnable, which this component was added after.</summary>
        [UsedImplicitly]
        private void OnEnable() {
            try {
                m_picker.Initialize();
                m_initialized = true;
            }
            catch (System.Exception e) {
                WKT.Instance.Logger.LogError($"Failed to set up the lobby's trinket picker: {e}");
            }
        }

        [UsedImplicitly]
        private void OnDestroy() {
            var wkt = WKT.InstanceOrNull;
            if (wkt != null)
                wkt.Network.OnLobbyTrinketsChanged -= OnTrinketsChanged;
        }

        private void OnTrinketsChanged() {
            if (m_initialized && isActiveAndEnabled)
                m_picker.ReloadTrinkets();
        }
    }
}
