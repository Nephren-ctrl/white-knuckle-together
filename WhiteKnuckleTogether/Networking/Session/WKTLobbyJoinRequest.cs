namespace WhiteKnuckleTogether {
    /// <summary>Decides what accepting a Steam invite does. Pure, so it can be tested without the game.</summary>
    public static class WKTLobbyJoinRequest {
        /// <param name="currentLobbyId">The lobby of the current session. Zero when there is none.</param>
        public static WKTLobbyJoinAction Decide(NetworkMode mode, bool isOnMainMenu, ulong requestedLobbyId, ulong currentLobbyId) {
            if (mode != NetworkMode.None && requestedLobbyId == currentLobbyId) return WKTLobbyJoinAction.Ignore;
            if (!isOnMainMenu) return WKTLobbyJoinAction.Defer;
            return mode == NetworkMode.None ? WKTLobbyJoinAction.Join : WKTLobbyJoinAction.LeaveThenJoin;
        }
    }
}
