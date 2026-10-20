namespace WhiteKnuckleTogether {
    /// <summary>The client's rules for world objects it built. Pure, so it can be tested without Unity.</summary>
    internal static class WKTClientRegistration {
        /// <summary>
        /// What to do, at the end of the frame, with an object the client registered without the host claiming it.
        /// </summary>
        /// <param name="claimed">The host bound it, or it is one of the client's own drops the host is about to bind.</param>
        /// <param name="keyed">It has a key every machine shares, so the host's bind for it may still arrive.</param>
        /// <param name="active">The object's own active flag.</param>
        /// <param name="inWorld">It is not in the bag or a hand.</param>
        /// <param name="replicated">It is not a player or carried by one.</param>
        public static WKTUnclaimedAction Decide(bool claimed, bool keyed, bool active, bool inWorld, bool replicated) {
            if (claimed) return WKTUnclaimedAction.Keep;
            // Moved into the bag, a hand or a player since it registered: not a world object any more.
            if (!replicated || !inWorld) return WKTUnclaimedAction.Forget;
            if (active) return WKTUnclaimedAction.Hide;
            // Switched off by the game itself. A keyed object waits for its bind, which decides whether it is shown. A keyless
            // one registers again if it is ever enabled in the world.
            return keyed ? WKTUnclaimedAction.Keep : WKTUnclaimedAction.Forget;
        }
    }
}
