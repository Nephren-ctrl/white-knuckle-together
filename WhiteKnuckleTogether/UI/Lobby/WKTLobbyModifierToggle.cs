using System.Collections.Generic;
using JetBrains.Annotations;
using UnityEngine;
using UnityEngine.UI;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// The host picks the lobby modifiers with this toggle. Clients see the host's choice and cannot change it.
    /// </summary>
    internal sealed class WKTLobbyModifierToggle : MonoBehaviour {
        private static readonly List<WKTLobbyModifierToggle> s_toggles = new();
        public ModifierId modifierId;
        /// <summary>The value shown before the player picks one, and after leaving another host's lobby.</summary>
        public bool defaultValue;
        /// <summary>Whether <see cref="disabledBy"/> is set. The host cannot change this toggle while that modifier is on.</summary>
        public bool hasDisabledBy;
        public ModifierId disabledBy;
        /// <summary>How opaque a toggle another modifier turns off is shown, so it reads as unavailable even when ticked.</summary>
        private const float c_disabled_alpha = 0.4f;
        private Toggle? m_toggle;
        private CanvasGroup? m_group;

        /// <summary>Sets the toggle up before it is bound. Call right after adding the component.</summary>
        public void Configure(ModifierId modifier, bool isOnByDefault) {
            modifierId = modifier;
            defaultValue = isOnByDefault;
            GetComponent<Toggle>().SetIsOnWithoutNotify(isOnByDefault);
        }

        [UsedImplicitly]
        private void Awake() {
            s_toggles.Add(this);
            m_toggle = GetComponent<Toggle>();
            m_group = TryGetComponent<CanvasGroup>(out var group) ? group : gameObject.AddComponent<CanvasGroup>();
            var network = WKT.Instance.Network;
            network.OnHostStarted += OnHostStarted;
            network.OnClientStarted += ShowLobbyModifier;
            network.OnLobbyModifiersChanged += ShowLobbyModifier;
            network.OnNetworkStopping += OnNetworkStopping;
            network.OnHostChanged += OnHostChanged;
        }

        /// <summary>Bound here rather than in Awake, so <see cref="Configure"/> has set the starting value first.</summary>
        [UsedImplicitly]
        private void Start() => GetComponent<WKTToggleBinder>().Bind(OnValueChanged);

        [UsedImplicitly]
        private void OnEnable() => ShowLobbyModifier();

        [UsedImplicitly]
        private void OnDestroy() {
            s_toggles.Remove(this);
            GetComponent<WKTToggleBinder>().Unbind(OnValueChanged);
            var wkt = WKT.InstanceOrNull;
            if (wkt == null) return;
            wkt.Network.OnHostStarted -= OnHostStarted;
            wkt.Network.OnClientStarted -= ShowLobbyModifier;
            wkt.Network.OnLobbyModifiersChanged -= ShowLobbyModifier;
            wkt.Network.OnNetworkStopping -= OnNetworkStopping;
            wkt.Network.OnHostChanged -= OnHostChanged;
        }

        /// <summary>
        /// Puts every toggle the local player can change to <paramref name="mode"/>'s default, as if clicked, so the lobby
        /// hears of it. Clients cannot change toggles, so nothing happens for them.
        /// </summary>
        public static void ApplyDefaults(PlayModeId mode) {
            if (WKT.Instance.Network.Mode == NetworkMode.Client) return;
            foreach (var toggle in s_toggles) {
                var value = WKTPlayModeDefaults.GetDefault(mode, toggle.modifierId, out var hasDefault);
                if (!hasDefault || toggle.m_toggle == null || toggle.m_toggle.isOn == value) continue;
                toggle.m_toggle.isOn = value;
            }
        }

        private void OnValueChanged(bool value) {
            if (WKT.Instance.Network.Mode != NetworkMode.Host) return;
            if (value)
                WKT.Instance.Network.AddLobbyModifier(modifierId);
            else
                WKT.Instance.Network.RemoveLobbyModifier(modifierId);
        }

        /// <summary>The lobby only exists once the host has started, so a choice made before that is sent now.</summary>
        private void OnHostStarted() {
            if (m_toggle != null)
                OnValueChanged(m_toggle.isOn);
        }

        private void ShowLobbyModifier() {
            if (m_toggle == null) return;
            var network = WKT.Instance.Network;
            switch (network.Mode) {
                case NetworkMode.Client:
                    m_toggle.interactable = false;
                    m_toggle.SetIsOnWithoutNotify(network.HasLobbyModifier(modifierId));
                    break;
                case NetworkMode.Host:
                    m_toggle.interactable = !IsDisabledByOtherModifier(network);
                    break;
            }
            ShowDisabled(IsDisabledByOtherModifier(network));
        }

        private void ShowDisabled(bool disabled) {
            if (m_group != null)
                m_group.alpha = disabled ? c_disabled_alpha : 1f;
        }

        /// <summary>A player who took over as host keeps the old host's choice and may now change it.</summary>
        private void OnHostChanged() {
            if (m_toggle == null || WKT.Instance.Network.Mode != NetworkMode.Host) return;
            m_toggle.SetIsOnWithoutNotify(WKT.Instance.Network.HasLobbyModifier(modifierId));
            var disabled = IsDisabledByOtherModifier(WKT.Instance.Network);
            m_toggle.interactable = !disabled;
            ShowDisabled(disabled);
        }

        /// <summary>Drops the last host's choice so it is not carried into a lobby the local player hosts.</summary>
        private void OnNetworkStopping() {
            if (m_toggle == null || WKT.Instance.Network.Mode != NetworkMode.Client) return;
            m_toggle.interactable = true;
            m_toggle.SetIsOnWithoutNotify(defaultValue);
            ShowDisabled(false);
        }

        private bool IsDisabledByOtherModifier(WKTNetwork network) => hasDisabledBy && network.HasLobbyModifier(disabledBy);
    }
}
