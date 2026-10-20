namespace WhiteKnuckleTogether {
    /// <summary>Whether a chat message may be sent. Sent by the host as <see cref="WKTMessageId.ChatNotice"/> when it is not.</summary>
    public enum WKTChatVerdict : byte {
        Allowed,
        /// <summary>Too many messages too quickly.</summary>
        TooFast,
        /// <summary>The same message as just before.</summary>
        Duplicate
    }
}
