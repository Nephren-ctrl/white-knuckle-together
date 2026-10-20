using System;

namespace WhiteKnuckleTogether {
    /// <summary>The host picks the <see cref="PlayModeId"/> with this dropdown.</summary>
    internal sealed class WKTLobbyPlayModeDropdown : WKTLobbyDropdownBase {
        /// <summary>The dropdown option text for each <see cref="PlayModeId"/>, in enum order.</summary>
        public static string[] OptionNames { get; } = { "Co-op", "Race" };

        protected override int LocalValue {
            get => (int)WKT.Instance.Settings.LocalPlayModeId;
            set => WKT.Instance.Settings.LocalPlayModeId = (PlayModeId)value;
        }

        protected override int GetLobbyValue(WKTNetwork network) => (int)network.LobbyPlayMode;

        protected override void SetLobbyValue(WKTNetwork network, int value) => network.SetLobbyPlayMode((PlayModeId)value);

        protected override void SubscribeLobbyValueChanged(WKTNetwork network, Action handler) => network.OnLobbyPlayModeChanged += handler;

        protected override void UnsubscribeLobbyValueChanged(WKTNetwork network, Action handler) => network.OnLobbyPlayModeChanged -= handler;

        /// <summary>Each mode starts from its own settings, such as instanced for a race.</summary>
        protected override void OnLocalValueChanged(int value) => WKTPlayModeDefaults.Apply((PlayModeId)value);
    }
}
