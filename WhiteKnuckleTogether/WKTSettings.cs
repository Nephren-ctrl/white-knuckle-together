namespace WhiteKnuckleTogether {
    public sealed class WKTSettings {
        public LobbyVisibilityId LocalLobbyVisibilityId { get; set; }
        public GamemodeId LocalSelectedGamemodeId { get; set; }
        public PlayModeId LocalPlayModeId { get; set; }
        public GooFollowId LocalGooFollowId { get; set; }
        public OrbRadiusId LocalOrbRadiusId { get; set; } = OrbRadiusId.Medium;
        public PlayerCollisionId LocalPlayerCollisionId { get; set; } = PlayerCollisionId.Soft;
        /// <summary>
        /// The shortcuts this player unticked as host, one bit per <see cref="ShortcutId"/>. Every other shortcut all players
        /// have unlocked is ticked, so a newly available one starts on.
        /// </summary>
        public byte LocalUntickedShortcuts { get; set; }
        /// <summary>The name of lobbies this player hosts. Empty for the default (see <see cref="WKTLobbyData.GetLobbyName"/>).</summary>
        public string LocalLobbyName { get; set; } = string.Empty;
    }
}