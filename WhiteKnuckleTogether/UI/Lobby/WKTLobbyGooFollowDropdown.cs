using System;

namespace WhiteKnuckleTogether {
    /// <summary>The host picks what the shared goo follows (<see cref="GooFollowId"/>) with this dropdown. Locked while instanced.</summary>
    internal sealed class WKTLobbyGooFollowDropdown : WKTLobbyDropdownBase {
        /// <summary>The dropdown option text for each <see cref="GooFollowId"/>, in enum order.</summary>
        public static string[] OptionNames { get; } = { "Average", "Lowest Player", "Highest Player" };

        protected override int LocalValue {
            get => (int)WKT.Instance.Settings.LocalGooFollowId;
            set => WKT.Instance.Settings.LocalGooFollowId = (GooFollowId)value;
        }

        protected override int GetLobbyValue(WKTNetwork network) => (int)network.LobbyGooFollow;

        protected override void SetLobbyValue(WKTNetwork network, int value) => network.SetLobbyGooFollow((GooFollowId)value);

        protected override void SubscribeLobbyValueChanged(WKTNetwork network, Action handler) => network.OnLobbyGooFollowChanged += handler;

        protected override void UnsubscribeLobbyValueChanged(WKTNetwork network, Action handler) => network.OnLobbyGooFollowChanged -= handler;

        /// <summary>Every player has their own goo in an instanced run.</summary>
        protected override bool IsLocked(WKTNetwork network) => network.HasLobbyModifier(ModifierId.Instanced);
    }
}
