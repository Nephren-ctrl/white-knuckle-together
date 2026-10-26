namespace WhiteKnuckleTogether {
    /// <summary>
    /// What each play mode starts from, and which lobby settings it has at all. Picking a mode as host puts every setting to
    /// that mode's defaults; settings a mode does not have are hidden in the lobby and never apply to its runs.
    /// </summary>
    internal static class WKTPlayModeDefaults {
        private static readonly ModifierId[] s_coopOnly = { ModifierId.GrabAssists, ModifierId.ShareRoaches, ModifierId.KeepTogether };

        /// <summary>The race orb's size a mode starts at.</summary>
        public static OrbRadiusId DefaultOrbRadius => OrbRadiusId.Medium;

        /// <summary>Whether <paramref name="modifier"/> is a setting of <paramref name="mode"/>.</summary>
        public static bool IsAvailable(PlayModeId mode, ModifierId modifier) =>
            mode == PlayModeId.Coop || System.Array.IndexOf(s_coopOnly, modifier) < 0;

        /// <summary>
        /// The value <paramref name="modifier"/> starts at in <paramref name="mode"/>, or false in <paramref name="hasDefault"/>
        /// for one the mode leaves as the host had it (Iron Knuckle, Hard Mode).
        /// </summary>
        public static bool GetDefault(PlayModeId mode, ModifierId modifier, out bool hasDefault) {
            hasDefault = true;
            switch (modifier) {
                case ModifierId.IronKnuckle:
                case ModifierId.Hard:
                    hasDefault = false;
                    return false;
                case ModifierId.Instanced:
                    // A race is everyone against everyone: each climbs their own copy of the same world.
                    return mode == PlayModeId.Race;
                case ModifierId.ShareRoaches:
                case ModifierId.GrabAssists:
                case ModifierId.ProximityVoice:
                case ModifierId.DeadCanTalk:
                case ModifierId.ItemStealing:
                case ModifierId.ShareTimedHandholds:
                    // Co-op's ways of playing together; racers also talk across the whole race, not only to whoever is near.
                    return mode == PlayModeId.Coop;
                case ModifierId.InstancedArtifacts:
                    return true;
                case ModifierId.KeepTogether:
                    return false;
                default:
                    hasDefault = false;
                    return false;
            }
        }

        /// <summary>Puts every lobby setting to <paramref name="mode"/>'s defaults, as if the host had picked them.</summary>
        public static void Apply(PlayModeId mode) {
            WKTLobbyModifierToggle.ApplyDefaults(mode);
            WKTLobbyOrbRadiusDropdown.ApplyDefault();
        }
    }
}
