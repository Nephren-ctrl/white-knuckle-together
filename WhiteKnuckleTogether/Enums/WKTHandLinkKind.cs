namespace WhiteKnuckleTogether {
    /// <summary>A step of two players taking hold of each other's hands (Grab Assists).</summary>
    internal enum WKTHandLinkKind : byte {
        /// <summary>Client to host: wants to take hold of another player's open hand.</summary>
        Request,
        /// <summary>Host to everyone: the two hands now hold each other.</summary>
        Link,
        /// <summary>Client to host, then host to everyone: a linked hand let go, so both do.</summary>
        Release
    }
}
