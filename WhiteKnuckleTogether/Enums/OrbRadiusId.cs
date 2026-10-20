namespace WhiteKnuckleTogether {
    /// <summary>How big the race orb is (see <see cref="WKTRaceOrb"/>). Picked by the host in the lobby.</summary>
    public enum OrbRadiusId : byte {
        /// <summary>15 m: stay close to the leader or fall behind fast.</summary>
        Small,
        /// <summary>30 m.</summary>
        Medium,
        /// <summary>45 m: room to fall behind and catch up.</summary>
        Large
    }
}
