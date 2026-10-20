using System;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// The host picks how players bump into each other (<see cref="PlayerCollisionId"/>) with this dropdown. Greyed out in
    /// instanced runs, which never have collisions.
    /// </summary>
    internal sealed class WKTLobbyPlayerCollisionDropdown : WKTLobbyDropdownBase {
        /// <summary>The dropdown option text for each <see cref="PlayerCollisionId"/>, in enum order.</summary>
        public static string[] OptionNames { get; } = { "No Collisions", "Soft Collisions", "Hard Collisions" };

        protected override int LocalValue {
            get => (int)WKT.Instance.Settings.LocalPlayerCollisionId;
            set => WKT.Instance.Settings.LocalPlayerCollisionId = (PlayerCollisionId)value;
        }

        protected override int GetLobbyValue(WKTNetwork network) => (int)network.LobbyPlayerCollision;

        protected override void SetLobbyValue(WKTNetwork network, int value) => network.SetLobbyPlayerCollision((PlayerCollisionId)value);

        protected override void SubscribeLobbyValueChanged(WKTNetwork network, Action handler) => network.OnLobbyPlayerCollisionChanged += handler;

        protected override void UnsubscribeLobbyValueChanged(WKTNetwork network, Action handler) => network.OnLobbyPlayerCollisionChanged -= handler;

        /// <summary>In instanced co-op the others are ghosts from other copies of the world: there is nobody to bump into.</summary>
        protected override bool IsLocked(WKTNetwork network) => network.HasLobbyModifier(ModifierId.Instanced);
    }
}
