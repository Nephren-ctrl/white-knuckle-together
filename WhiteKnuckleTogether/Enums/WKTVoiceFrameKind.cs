namespace WhiteKnuckleTogether {
    /// <summary>What a speaker's jitter buffer has for the next 20 ms of their voice.</summary>
    internal enum WKTVoiceFrameKind : byte {
        /// <summary>Not talking: nothing to play.</summary>
        Silence,
        /// <summary>The frame's own packet.</summary>
        Packet,
        /// <summary>The frame's packet was lost, but the next packet's error correction data can rebuild it.</summary>
        FromNext,
        /// <summary>The frame's packet was lost: conceal it.</summary>
        Lost
    }
}
