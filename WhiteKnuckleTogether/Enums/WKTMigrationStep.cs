namespace WhiteKnuckleTogether {
    /// <summary>What a client in the middle of a host migration does next. See <see cref="WKTMigration"/>.</summary>
    public enum WKTMigrationStep : byte {
        /// <summary>No migration, or an attempt to reach the new host is in flight or waiting to be retried.</summary>
        None,
        /// <summary>The local player takes over as host.</summary>
        Promote,
        /// <summary>Try to connect to the new host.</summary>
        Connect,
        /// <summary>The new host could not be reached in time. The session is over.</summary>
        GiveUp
    }
}
