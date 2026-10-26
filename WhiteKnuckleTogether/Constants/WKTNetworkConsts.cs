namespace WhiteKnuckleTogether {
    public static class WKTNetworkConsts {
        public const int SEND_RATE = 20;
        public const float SEND_DELTA = 1f / SEND_RATE;
        /// <summary>Bump whenever the wire format of any message changes.</summary>
        public const ushort PROTOCOL_VERSION = 38;
        /// <summary>Steam's maximum message size (k_cbMaxSteamNetworkingSocketsMessageSizeSend).</summary>
        public const int MAX_MESSAGE_SIZE = 512 * 1024;
        public const int MAX_VERSION_LENGTH = 64;
        public const float HANDSHAKE_TIMEOUT = 10f;
        /// <summary>How long a client keeps trying to reach the new host after the old one left.</summary>
        public const float MIGRATION_TIMEOUT = 20f;
        /// <summary>How long a client waits between attempts to reach the new host.</summary>
        public const float MIGRATION_RETRY_DELAY = 1f;
        /// <summary>
        /// How long the new host keeps a place for each player of the old session. Longer than
        /// <see cref="MIGRATION_TIMEOUT"/>, since players notice a crashed host at different times.
        /// </summary>
        public const float RECONNECT_TIMEOUT = 30f;
        /// <summary>
        /// Per progression profile: the most ids in each set, and the most upgrades across all facilities. Sized so a
        /// full profile of ASCII ids stays under <see cref="MAX_MESSAGE_SIZE"/>.
        /// </summary>
        public const int MAX_PROFILE_ENTRIES = 1024;
        public const int MAX_PROFILE_FACILITIES = 64;
        public const int MAX_PROFILE_ID_LENGTH = 96;
    }
}
