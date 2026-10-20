using System;
using JetBrains.Annotations;

namespace WhiteKnuckleTogether {
    /// <summary>The host picks the race orb's size (<see cref="OrbRadiusId"/>) with this dropdown. Race only.</summary>
    internal sealed class WKTLobbyOrbRadiusDropdown : WKTLobbyDropdownBase {
        private static WKTLobbyOrbRadiusDropdown? s_instance;

        /// <summary>The dropdown option text for each <see cref="OrbRadiusId"/>, in enum order.</summary>
        public static string[] OptionNames { get; } = { "15 m", "30 m", "45 m" };

        protected override int LocalValue {
            get => (int)WKT.Instance.Settings.LocalOrbRadiusId;
            set => WKT.Instance.Settings.LocalOrbRadiusId = (OrbRadiusId)value;
        }

        [UsedImplicitly]
        private void Start() => s_instance = this;

        /// <summary>Back to the default size, when the host picks a play mode.</summary>
        public static void ApplyDefault() {
            if (s_instance != null && WKT.Instance.Network.Mode != NetworkMode.Client)
                s_instance.SetLocalValue((int)WKTPlayModeDefaults.DefaultOrbRadius);
        }

        protected override int GetLobbyValue(WKTNetwork network) => (int)network.LobbyOrbRadius;

        protected override void SetLobbyValue(WKTNetwork network, int value) => network.SetLobbyOrbRadius((OrbRadiusId)value);

        protected override void SubscribeLobbyValueChanged(WKTNetwork network, Action handler) => network.OnLobbyOrbRadiusChanged += handler;

        protected override void UnsubscribeLobbyValueChanged(WKTNetwork network, Action handler) => network.OnLobbyOrbRadiusChanged -= handler;
    }
}
