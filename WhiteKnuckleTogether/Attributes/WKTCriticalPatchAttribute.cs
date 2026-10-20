using System;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// Marks a Harmony patch class that multiplayer cannot safely run without. If any critical patch fails to apply,
    /// for example because a game update renamed its target, multiplayer is disabled instead of running unprotected.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    internal sealed class WKTCriticalPatchAttribute : Attribute {
        public WKTPatchCategory Category { get; }

        public WKTCriticalPatchAttribute(WKTPatchCategory category) => Category = category;
    }
}
