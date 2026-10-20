using System;
using JetBrains.Annotations;
using TMPro;
using UnityEngine;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// A lobby setting the host picks with a dropdown, whose option index is the setting's enum value. Clients see the host's
    /// choice and cannot change it. A player who takes over as host keeps the old host's choice and may then change it.
    /// </summary>
    internal abstract class WKTLobbyDropdownBase : MonoBehaviour {
        private TMP_Dropdown? m_dropdown;

        /// <summary>The choice the local player makes when hosting. Kept between lobbies.</summary>
        protected abstract int LocalValue { get; set; }

        /// <summary>The choice of the lobby the local player is in.</summary>
        protected abstract int GetLobbyValue(WKTNetwork network);

        /// <summary>Host only: writes the choice to the lobby.</summary>
        protected abstract void SetLobbyValue(WKTNetwork network, int value);

        protected abstract void SubscribeLobbyValueChanged(WKTNetwork network, Action handler);

        protected abstract void UnsubscribeLobbyValueChanged(WKTNetwork network, Action handler);

        /// <summary>Called after the local player picked <paramref name="value"/>, as host or before hosting.</summary>
        protected virtual void OnLocalValueChanged(int value) { }

        /// <summary>Whether the host cannot change the choice right now, because another lobby setting makes it meaningless.</summary>
        protected virtual bool IsLocked(WKTNetwork network) => false;

        [UsedImplicitly]
        private void Awake() {
            m_dropdown = GetComponent<TMP_Dropdown>();
            m_dropdown.onValueChanged.AddListener(OnValueChanged);
            var network = WKT.Instance.Network;
            network.OnClientStarted += ShowLobbyValue;
            network.OnLobbyModifiersChanged += ShowLobbyValue;
            SubscribeLobbyValueChanged(network, ShowLobbyValue);
            network.OnNetworkStopping += OnNetworkStopping;
            network.OnHostChanged += OnHostChanged;
        }

        [UsedImplicitly]
        private void OnEnable() => ShowLobbyValue();

        [UsedImplicitly]
        private void OnDestroy() {
            m_dropdown.OrNull()?.onValueChanged.RemoveListener(OnValueChanged);
            var wkt = WKT.InstanceOrNull;
            if (wkt == null) return;
            wkt.Network.OnClientStarted -= ShowLobbyValue;
            wkt.Network.OnLobbyModifiersChanged -= ShowLobbyValue;
            UnsubscribeLobbyValueChanged(wkt.Network, ShowLobbyValue);
            wkt.Network.OnNetworkStopping -= OnNetworkStopping;
            wkt.Network.OnHostChanged -= OnHostChanged;
        }

        private void OnValueChanged(int index) {
            LocalValue = index;
            // Before the lobby exists the setting is enough: the lobby is created with it.
            var network = WKT.Instance.Network;
            if (network.Mode == NetworkMode.Host)
                SetLobbyValue(network, index);
            OnLocalValueChanged(index);
        }

        /// <summary>Picks <paramref name="value"/> as if the local player had, such as a mode's default.</summary>
        public void SetLocalValue(int value) {
            if (m_dropdown == null || m_dropdown.value == value) return;
            // Through the dropdown, so the lobby and everything listening hear of it.
            m_dropdown.value = value;
        }

        private void ShowLobbyValue() {
            if (m_dropdown == null) return;
            var network = WKT.Instance.Network;
            switch (network.Mode) {
                case NetworkMode.Client:
                    m_dropdown.interactable = false;
                    m_dropdown.SetValueWithoutNotify(GetLobbyValue(network));
                    break;
                case NetworkMode.Host:
                    m_dropdown.interactable = !IsLocked(network);
                    break;
            }
        }

        private void OnHostChanged() {
            var network = WKT.Instance.Network;
            if (m_dropdown == null || network.Mode != NetworkMode.Host) return;
            LocalValue = GetLobbyValue(network);
            m_dropdown.interactable = !IsLocked(network);
        }

        /// <summary>Shows the local choice again, so the last host's is not carried into a lobby the local player hosts.</summary>
        private void OnNetworkStopping() {
            if (m_dropdown == null || WKT.Instance.Network.Mode != NetworkMode.Client) return;
            m_dropdown.interactable = true;
            m_dropdown.SetValueWithoutNotify(LocalValue);
        }
    }
}
