namespace WhiteKnuckleTogether {
    /// <summary>What happens to an entity sound played on this machine. See <see cref="WKTEntitySoundRules"/>.</summary>
    internal enum WKTSoundRoute : byte {
        /// <summary>Plays here only, as in singleplayer.</summary>
        Play,
        /// <summary>Plays here and goes to the other machines.</summary>
        PlayAndSend,
        /// <summary>Does not play: the machine simulating the entity sends its sounds.</summary>
        Mute
    }
}
