namespace WhiteKnuckleTogether {
    /// <summary>
    /// State for the multiplayer run in progress, if any. <see cref="IsMultiplayerRun"/> is the single gate every run
    /// feature checks first: it is only true between a StartRun being issued or received and the run ending, and only
    /// while the network is active. Singleplayer never begins a run, so every run feature stays inert there.
    /// </summary>
    public sealed class WKTSession {
        /// <summary>The run being played. Only meaningful while <see cref="IsRunActive"/>.</summary>
        public WKTRunStart Run { get; } = new();
        public bool IsRunActive { get; private set; }
        /// <summary>The local player's spawn slot. Only meaningful while <see cref="IsRunActive"/>.</summary>
        public byte LocalSlot { get; private set; }
        /// <summary>
        /// The shortcuts open in the run, one bit per <see cref="ShortcutId"/>. Worked out on each read, as the run's modifiers
        /// are settled after it begins. None while no run is active.
        /// </summary>
        public byte OpenShortcutMask => IsRunActive ? Run.GetOpenShortcutMask() : (byte)0;

        /// <summary>
        /// A run where everyone shares one world, and the host owns every enemy, item, prop and world event: co-op, or a race
        /// that is not instanced. False in an instanced run, where spawns match but every machine simulates its own copy.
        /// </summary>
        public bool IsSyncedWorld => IsRunActive && !Run.HasModifier(ModifierId.Instanced);
        /// <summary>A race (see <see cref="WKTRace"/>), instanced or not.</summary>
        public bool IsRace => IsRunActive && Run.PlayMode == PlayModeId.Race;
        /// <summary>A co-op run in a shared world: the rules only for climbing together, such as revives and grab assists.</summary>
        public bool IsSyncedCoop => IsSyncedWorld && Run.PlayMode == PlayModeId.Coop;
        /// <summary>Whether the run's roaches are one purse for everyone (see <see cref="WKTSharedRoaches"/>).</summary>
        public bool ShareRoaches => IsRunActive && Run.HasModifier(ModifierId.ShareRoaches);
        /// <summary>Whether players can take items out of each other's hands. Only in a <see cref="IsSyncedWorld"/> run.</summary>
        public bool ItemStealing => IsSyncedWorld && Run.HasModifier(ModifierId.ItemStealing);
        /// <summary>Whether each player picks their own artifact at an altar. Always in instanced co-op, where each has their own altar.</summary>
        public bool InstancedArtifacts => IsRunActive && (!IsSyncedWorld || Run.HasModifier(ModifierId.InstancedArtifacts));
        /// <summary>
        /// Whether players can take hold of each other's open hands (see <see cref="WKTGrabAssist"/>). Only in a
        /// <see cref="IsSyncedWorld"/> run: the bodies being pulled must be where everyone sees them.
        /// </summary>
        public bool GrabAssists => IsSyncedCoop && Run.HasModifier(ModifierId.GrabAssists);
        /// <summary>Whether voices come from each player's head and fade with distance, rather than being heard flat everywhere.</summary>
        public bool ProximityVoice => IsRunActive && Run.HasModifier(ModifierId.ProximityVoice);
        /// <summary>Whether the dead may talk to the living by holding focus (see <see cref="ModifierId.DeadCanTalk"/>).</summary>
        public bool DeadCanTalk => IsRunActive && Run.HasModifier(ModifierId.DeadCanTalk);
        /// <summary>Whether timed handholds break and fire their timers for everyone. Only possible in a <see cref="IsSyncedWorld"/> run.</summary>
        public bool ShareTimedHandholds => IsSyncedWorld && Run.HasModifier(ModifierId.ShareTimedHandholds);
        /// <summary>Which height the shared goo keeps up with. Only used in a <see cref="IsSyncedWorld"/> run.</summary>
        public GooFollowId GooFollow => Run.GooFollow;

        /// <summary>How players bump into each other in the run.</summary>
        public PlayerCollisionId PlayerCollision => Run.PlayerCollision;

        /// <summary>Whether a shortcut is open in the run: forced open when true, and kept closed when false.</summary>
        public bool IsShortcutOpen(ShortcutId id) => (OpenShortcutMask & (1 << (int)id)) != 0;

        public bool IsMultiplayerRun(NetworkMode mode) => IsRunActive && mode != NetworkMode.None;

        /// <summary>
        /// Begins a run from <paramref name="run"/>. False, leaving the session unchanged, if a run is already active or
        /// the local player has no slot in it.
        /// </summary>
        public bool TryBegin(WKTRunStart run, byte localPlayerId) {
            if (IsRunActive || !run.TryGetSlot(localPlayerId, out var slot)) return false;
            Run.CopyFrom(run);
            LocalSlot = slot;
            IsRunActive = true;
            return true;
        }

        public void End() {
            IsRunActive = false;
            LocalSlot = 0;
            Run.Clear();
        }
    }
}
