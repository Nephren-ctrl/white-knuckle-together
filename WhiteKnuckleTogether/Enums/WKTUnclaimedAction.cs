namespace WhiteKnuckleTogether {
    /// <summary>What a client does with a world object of its own that the host has not claimed.</summary>
    internal enum WKTUnclaimedAction : byte {
        /// <summary>Leave it: the host has claimed it, or it is the client's own drop on its way to the host.</summary>
        Keep,
        /// <summary>Deactivate it: the client must have no world objects of its own.</summary>
        Hide,
        /// <summary>Stop tracking it: it is no longer a world object, such as an item that went into the bag.</summary>
        Forget
    }
}
