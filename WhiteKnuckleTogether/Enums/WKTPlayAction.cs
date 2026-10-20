namespace WhiteKnuckleTogether {
    /// <summary>What pressing Play on a gamemode does, depending on the network and the gamemode.</summary>
    public enum WKTPlayAction : byte {
        /// <summary>Offline: the game's own behavior, untouched.</summary>
        RunOriginal,
        /// <summary>A singleplayer gamemode while in a lobby: leave the lobby, then play it as plain singleplayer.</summary>
        LeaveLobbyThenRunOriginal,
        /// <summary>A multiplayer gamemode on a client: only the host starts runs.</summary>
        Block,
        /// <summary>A multiplayer gamemode on the host: start the run for everyone.</summary>
        StartMultiplayerRun
    }
}
