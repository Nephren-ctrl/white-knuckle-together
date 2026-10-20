namespace WhiteKnuckleTogether {
    /// <summary>Lobby modifiers. Sent as one bit each in a ushort, so there can be at most 16.</summary>
    public enum ModifierId : byte {
        IronKnuckle,
        Hard,
        /// <summary>Mod only. Every player gets the same spawns, but enemies, items and props are not synced.</summary>
        Instanced,
        /// <summary>
        /// Mod only. Handholds that give way after being held too long break, and fire their timers, for everyone. Off, each
        /// player's own hold only affects their own copy. Ignored when instanced.
        /// </summary>
        ShareTimedHandholds,
        /// <summary>Mod only. The run's roaches are one purse for everyone: deposits and purchases count for all players.</summary>
        ShareRoaches,
        /// <summary>Mod only. Players can take the item another player holds by holding grab on their hand. Ignored when instanced.</summary>
        ItemStealing,
        /// <summary>
        /// Mod only. Every player picks their own artifact at an artifact altar, from their own choices. Off, everyone sees
        /// the same two and the first pick is everyone's. Ignored when instanced (each world has its own altar).
        /// </summary>
        InstancedArtifacts,
        /// <summary>
        /// Mod only. Holding grab with an empty hand holds it out for others, and another player can take it: both are then
        /// held to each other like a handhold. Co-op only, ignored when instanced.
        /// </summary>
        GrabAssists,
        /// <summary>
        /// Mod only. Voices come from each player's head and fade with distance and walls. Off, everyone is heard flat at full
        /// volume wherever they are.
        /// </summary>
        ProximityVoice,
        /// <summary>
        /// Mod only. A dead player holding focus is heard by the living too, from where their spectator camera is, and shows
        /// there as a faint ghost head while they talk. Off, the dead are heard only by the dead.
        /// </summary>
        DeadCanTalk
    }
}
