namespace WhiteKnuckleTogether {
    /// <summary>
    /// Whom a pull to another player brings over (see <see cref="WKTGatherCheck.GetLoadPull"/>): those near whoever started
    /// a shortcut, or, for level scripting that takes levels away, only whoever is in them.
    /// </summary>
    internal enum WKTLoadPull : byte {
        /// <summary>Not level scripting: a shortcut's start, which pulls those near whoever started it.</summary>
        None,
        /// <summary>Every level is made again (<c>SendToWorldLoader</c>): everyone far away is brought over.</summary>
        Everyone,
        /// <summary>Every branch but the trigger's is deleted (<c>DeleteOtherBranches</c>): only whoever is on another branch.</summary>
        OtherBranches,
        /// <summary>
        /// The Hadal Diver let its riders off: everyone else ends their own diver's ride, whose exit builds the branch it leads
        /// to and takes them into it (see <see cref="WKTDiverExit"/>). Nobody is pulled on top of that.
        /// </summary>
        Diver
    }
}
