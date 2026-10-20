namespace WhiteKnuckleTogether {
    public enum LobbyVisibilityId {
        FriendsOnly,
        /// <summary>Steam's private lobby: only invited players can join.</summary>
        InviteOnly,
        Public,
        /// <summary>
        /// Not in the lobby browser: anyone who enters the lobby's join code finds it (see <see cref="WKTLobbyConsts.KEY_JOIN_CODE"/>).
        /// A public lobby to Steam, since Steam only searches public lobbies, so friends can also join it through Steam.
        /// </summary>
        JoinCode
    }
}
