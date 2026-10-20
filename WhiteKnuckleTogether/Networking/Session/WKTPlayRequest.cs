namespace WhiteKnuckleTogether {
    /// <summary>Decides what pressing Play does. Pure, so it can be tested without the game.</summary>
    public static class WKTPlayRequest {
        public static WKTPlayAction Decide(NetworkMode mode, bool isMultiplayerGamemode) {
            switch (mode) {
                case NetworkMode.None:
                    return WKTPlayAction.RunOriginal;
                case NetworkMode.Host when isMultiplayerGamemode:
                    return WKTPlayAction.StartMultiplayerRun;
                case NetworkMode.Client when isMultiplayerGamemode:
                    return WKTPlayAction.Block;
                default:
                    return WKTPlayAction.LeaveLobbyThenRunOriginal;
            }
        }
    }
}
