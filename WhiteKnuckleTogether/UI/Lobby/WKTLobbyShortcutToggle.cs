using JetBrains.Annotations;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// One shortcut in the lobby's list. A shortcut every player has unlocked shows its name, and the host ticks or unticks it
    /// for the run. Any other shows as redacted and stays unticked, so nobody learns of a shortcut they have not found.
    /// Clients see the host's choice and cannot change it. Iron Knuckle turns every shortcut off.
    /// </summary>
    internal sealed class WKTLobbyShortcutToggle : MonoBehaviour {
        /// <summary>Public so the game's clone of the lobby panel keeps it.</summary>
        public ShortcutId shortcutId;
        private Toggle? m_toggle;
        private TMP_Text? m_label;

        /// <summary>Sets the toggle up before it is bound. Call right after adding the component.</summary>
        public void Configure(ShortcutId id) => shortcutId = id;

        [UsedImplicitly]
        private void Awake() {
            m_toggle = GetComponent<Toggle>();
            m_label = GetComponentInChildren<TMP_Text>(true);
            var network = WKT.Instance.Network;
            network.OnHostStarted += Show;
            network.OnClientStarted += Show;
            network.OnLobbyShortcutsChanged += Show;
            network.OnLobbyModifiersChanged += Show;
            network.OnNetworkStopping += Show;
            network.OnHostChanged += Show;
        }

        [UsedImplicitly]
        private void Start() => GetComponent<WKTToggleBinder>().Bind(OnValueChanged);

        [UsedImplicitly]
        private void OnEnable() => Show();

        [UsedImplicitly]
        private void OnDestroy() {
            GetComponent<WKTToggleBinder>().Unbind(OnValueChanged);
            var wkt = WKT.InstanceOrNull;
            if (wkt == null) return;
            wkt.Network.OnHostStarted -= Show;
            wkt.Network.OnClientStarted -= Show;
            wkt.Network.OnLobbyShortcutsChanged -= Show;
            wkt.Network.OnLobbyModifiersChanged -= Show;
            wkt.Network.OnNetworkStopping -= Show;
            wkt.Network.OnHostChanged -= Show;
        }

        private void OnValueChanged(bool value) {
            var network = WKT.Instance.Network;
            // Only a choice the host can make counts. The binder also reports the shown state when it binds, which is
            // unticked for a redacted shortcut or under Iron Knuckle, and must not untick it for good.
            if (network.Mode != NetworkMode.Host || m_toggle == null || !m_toggle.interactable) return;
            network.SetLobbyShortcutTicked(shortcutId, value);
            // The host can only tick what every player has unlocked, so the shown state always follows the lobby's.
            Show();
        }

        private void Show() {
            if (m_toggle == null) return;
            var network = WKT.Instance.Network;
            var available = network.LobbyShortcutsAvailable;
            var ironKnuckle = network.HasLobbyModifier(ModifierId.IronKnuckle);
            if (m_label != null)
                m_label.text = WKTShortcuts.GetShownName(shortcutId, available);
            m_toggle.interactable = network.Mode == NetworkMode.Host && WKTShortcuts.Has(available, shortcutId) && !ironKnuckle;
            m_toggle.SetIsOnWithoutNotify(WKTShortcuts.Has(WKTShortcuts.GetOpenMask(network.LobbyShortcutsTicked, available, ironKnuckle), shortcutId));
        }
    }
}
