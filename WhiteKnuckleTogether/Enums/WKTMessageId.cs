namespace WhiteKnuckleTogether {
    /// <summary>
    /// The first byte of every message. Values are part of the wire protocol, so existing values must never be
    /// reordered. Bump <see cref="WKTNetworkConsts.PROTOCOL_VERSION"/> when changing them.
    /// </summary>
    public enum WKTMessageId : byte {
        // Reserved for the connection handshake and player list. Handled internally by the server and client.
        Hello = 0,
        Reject = 1,
        Welcome = 2,
        PlayerJoined = 3,
        PlayerLeft = 4,
        /// <summary>The host is leaving. Payload: the id of the player who takes over.</summary>
        HostMigrate = 5,
        // Gameplay messages. Handled by handlers registered on WKTMessageDispatcher.
        StartRun = 16,
        PlayerState = 17,
        PlayerStateRelay = 18,
        /// <summary>Host to clients, reliable: a world object exists. Payload: <see cref="WKTEntityBind"/>.</summary>
        EntityBind = 19,
        /// <summary>Host to clients, reliable: a world object is gone. Payload: handle.</summary>
        EntityDespawn = 20,
        /// <summary>Host to clients, reliable: a world object was activated or deactivated. Payload: handle, active.</summary>
        EntityActivation = 21,
        /// <summary>Host to clients, or a client's claim to the host, reliable: a world object was killed. Payload: <see cref="WKTEntityEvent"/> damage, killer id.</summary>
        EntityKill = 22,
        /// <summary>Client to host, reliable: the client hit a world object. Payload: <see cref="WKTEntityEvent"/> damage.</summary>
        DamageRequest = 23,
        /// <summary>Host to clients, unreliable: a batch of <see cref="WKTEntityState"/>.</summary>
        EntityStates = 24,
        /// <summary>Client to host, reliable: the client picked up an item. Payload: handle.</summary>
        PickupRequest = 25,
        /// <summary>Host to the client who asked, reliable: whether the pickup stands. Payload: handle, accepted.</summary>
        PickupResult = 26,
        /// <summary>Client to host, reliable: the client dropped an item. Payload: <see cref="WKTDropRequest"/>.</summary>
        DropRequest = 27,
        /// <summary>
        /// Client to host, reliable: the client grabbed a prop or a handhold on a creature it rides. Payload:
        /// <see cref="WKTEntityGrab"/>.
        /// </summary>
        EntityGrab = 28,
        /// <summary>Host to clients, reliable: which machine simulates a world object now. Payload: <see cref="WKTEntityAuthority"/>.</summary>
        EntityAuthority = 29,
        /// <summary>
        /// Client to host, unreliable: a batch of <see cref="WKTEntityState"/> for the world objects the client simulates. The
        /// host takes only the entries of objects the client owns.
        /// </summary>
        OwnerStates = 30,
        /// <summary>Client to host, reliable: the client let go of a world object with every hand. Payload: <see cref="WKTEntityRelease"/>.</summary>
        EntityRelease = 31,
        /// <summary>Client to host, reliable: the client's player set off a world event. Payload: component key, event index.</summary>
        EventRelay = 32,
        /// <summary>Host to clients, reliable: a door opened or closed. Payload: door key, open.</summary>
        DoorState = 33,
        /// <summary>Host to one client, reliable: the host's world did something to that client's player. Payload: <see cref="WKTPlayerEffect"/>.</summary>
        PlayerEffect = 34,
        /// <summary>
        /// Client to host and host to clients, reliable: an enemy the sender simulates fired. The host passes on a client's.
        /// Payload: <see cref="WKTShotFired"/> (see <see cref="WKTEnemyShots"/>).
        /// </summary>
        ProjectileFired = 35,
        /// <summary>
        /// Host to clients, reliable: the host ran the world part of an event, so every client runs it too. Payload: component
        /// key, event index.
        /// </summary>
        EventApplied = 36,
        /// <summary>Host to clients, reliable: a barnacle tongue changed state. Payload: <see cref="WKTTongueEvent"/>.</summary>
        TongueState = 37,
        /// <summary>
        /// Client to host, reliable: an enemy hit, grabbed or let go of the client's own player on its screen. Payload:
        /// <see cref="WKTPlayerHit"/>.
        /// </summary>
        PlayerHit = 38,
        /// <summary>Host to clients, reliable: an enemy's attack struck, so clients check it against their own player. Payload: <see cref="WKTAttackEvent"/>.</summary>
        AttackPerformed = 39,
        /// <summary>
        /// Client to host and host to clients, reliable: a player fired a hand item, shown to the others as a cosmetic copy.
        /// Payload: <see cref="WKTPlayerShot"/>.
        /// </summary>
        PlayerShot = 40,
        /// <summary>Client to host and host to clients, reliable: a player placed a piton. Payload: <see cref="WKTPitonPlaced"/>.</summary>
        PitonPlaced = 41,
        /// <summary>Client to host and host to clients, reliable: a placed piton was hammered or is gone. Payload: <see cref="WKTPitonChange"/>.</summary>
        PitonChange = 42,
        /// <summary>Client to host, reliable: the client's own player died or came back. Payload: dead (bool).</summary>
        PlayerDeath = 43,
        /// <summary>Host to clients, reliable: a player died or came back, as that player decided. Payload: player id, dead (bool).</summary>
        PlayerDeathRelay = 44,
        /// <summary>Host to clients, reliable: every player is dead, so the run is over and its stats show. No payload.</summary>
        RunOver = 45,
        /// <summary>Host to clients, reliable: the host ended the run, and everyone goes back to the lobby. No payload.</summary>
        ReturnToLobby = 46,
        /// <summary>
        /// Host to clients, reliable: the host restarts the run. Everyone goes back to the main menu, where the host's
        /// <see cref="StartRun"/> follows. No payload.
        /// </summary>
        RestartRun = 47,
        /// <summary>
        /// Client to host and host to clients, reliable: a player threw their return rebar, or the host replays one stuck in a
        /// wall to a late joiner. Payload: <see cref="WKTReturnRebarThrown"/>.
        /// </summary>
        ReturnRebarThrown = 48,
        /// <summary>
        /// Client to host and host to clients, reliable: a thrown return rebar stuck, was recalled, claimed, released or is gone.
        /// Payload: <see cref="WKTReturnRebarEvent"/>.
        /// </summary>
        ReturnRebarEvent = 49,
        /// <summary>Host to clients, unreliable: where the death goo is. Payload: <see cref="WKTGooPose"/>.</summary>
        GooState = 50,
        /// <summary>
        /// Client to host, unreliable: how the client steers a creature several players ride, which the host simulates.
        /// Payload: <see cref="WKTRiderInput"/>.
        /// </summary>
        RiderInput = 51,
        /// <summary>
        /// Client to host and host to one client, reliable: a rider's bucking timer for a flier, handed to the machine that
        /// simulates it next. Payload: <see cref="WKTRiderBuck"/>.
        /// </summary>
        RiderBuck = 52,
        /// <summary>
        /// Client to host and host to clients, reliable: a flier bucked one of its riders, so everyone sees and hears it.
        /// Payload: handle.
        /// </summary>
        FlierBucked = 53,
        /// <summary>
        /// Owning client to host and host to clients, unreliable: the ropes the sender simulates. Payload: send sequence, count,
        /// then that many <see cref="WKTRopeState"/> entries.
        /// </summary>
        RopeStates = 54,
        /// <summary>
        /// Client to host, reliable: the client took hold of a rope or let go of it with every hand. Payload: a byte
        /// (<see cref="WKTRopeState.HOLD_GRABBED"/> or 0), then the rope's <see cref="WKTRopeState"/> as the client shows it.
        /// </summary>
        RopeHold = 55,
        /// <summary>
        /// Host to clients, reliable: who simulates a rope now, and its state to carry on from. Payload: the owner's player id
        /// (or <see cref="WKTEntityAuthority.HOST"/>), then a <see cref="WKTRopeState"/> entry.
        /// </summary>
        RopeAuthority = 56,
        /// <summary>
        /// Client to host: a push a rope's holder gives it while the host simulates it. Unreliable for a swing, reliable for a
        /// grab's one-off push. Payload: <see cref="WKTRopeForce"/>.
        /// </summary>
        RopeForce = 57,
        /// <summary>
        /// Client to host and host to clients, reliable: a thrown rebar left a rope in a wall, or (replayed by the host to a
        /// late joiner) one is there. Payload: <see cref="WKTRopePlaced"/>.
        /// </summary>
        RopePlaced = 58,
        /// <summary>
        /// Client to host and host to clients, reliable: a handhold the sender's player holds played one of its own sounds,
        /// such as an icy hold slipping. Payload: <see cref="WKTHandholdSound"/>.
        /// </summary>
        HandholdSound = 59,
        /// <summary>
        /// Client to host and host to clients, reliable: a timed handhold broke or its timer ran out, with
        /// <see cref="ModifierId.ShareTimedHandholds"/> on. Payload: <see cref="WKTHandholdEvent"/>.
        /// </summary>
        HandholdEvent = 60,
        /// <summary>
        /// Client to host and host to clients, unreliable: how long a breakable handhold that counts its total hold time has
        /// been held, while the sender's player holds it. Payload: key, then the time as a float.
        /// </summary>
        HandholdHoldTime = 61,
        /// <summary>
        /// Client to host and host to clients, reliable: a world entity the sender simulates played or stopped one of its
        /// sounds. The host passes on a client's to the other players near it. Payload: <see cref="WKTEntitySound"/>.
        /// </summary>
        EntitySound = 62,
        /// <summary>
        /// Client to host and host to clients, reliable: a world entity the sender simulates invoked one of its own UnityEvents,
        /// such as its death, attack or hunt event. The host passes on a client's to the other players near it. Payload: the
        /// entity's handle, then the event's index (see <see cref="WKTEntityEvents"/>), then from clients only the id of the
        /// player whose hit set it off (<see cref="WKTDamageCause"/>, 255 for nobody).
        /// </summary>
        EntityUnityEvent = 63,
        /// <summary>
        /// Client to host and host to clients, unreliable: how a world entity the sender simulates looks, beyond its pose and
        /// animator, such as a creature's body segments or model, sent when it changes and once a second. The host passes on a
        /// client's to the other players near it. Payload: the entity's handle, a byte length, then that many bytes its type's
        /// presenter reads (see <see cref="WKTPuppetPresenterBase"/>).
        /// </summary>
        EntityPresentation = 64,
        /// <summary>
        /// Client to host, unreliable: a request for the host's clocks. Payload: the client's real time as a double, which the
        /// host's answer carries back. Host to client, unreliable: the answer. Payload: that real time, then the host's game time
        /// and unscaled time as doubles (see <see cref="WKTSharedClock"/>).
        /// </summary>
        ClockSync = 65,
        /// <summary>
        /// Client to host and host to a client, reliable: a push the sender gave a world entity another machine simulates, such
        /// as a kick tipping a turret over. The host applies it or passes it on to the client simulating the entity. Payload:
        /// <see cref="WKTEntityForce"/>.
        /// </summary>
        EntityForce = 66,
        /// <summary>
        /// Client to host, reliable: whether the client's living player has a Remains on screen, sent when it changes. A Remains
        /// freezes while anyone sees it. Payload: the Remains' handle, then a bool.
        /// </summary>
        RemainsSeen = 67,
        /// <summary>
        /// Client to host, unreliable: how much the client's own lights frighten a Teeth (<c>CL_Lamp.GetHighestEffectorFearRating</c>
        /// at it), a few times a second while above zero, then once at zero. Payload: the Teeth's handle, then the rating as a float.
        /// </summary>
        TeethLightFear = 68,
        /// <summary>
        /// Client to host, reliable: the client enraged its copy of Mother (eating a grub, killing a slug grub), which the host
        /// does instead. Payload: Mother's handle, whether she crushes, then the id of the player it is for (see
        /// <see cref="WKTMotherWrath"/>).
        /// </summary>
        MotherEnrage = 69,
        /// <summary>
        /// Host to a client, reliable: a line Mother says to that client's player during a crush meant for them. Payload:
        /// Mother's handle, then the line (at most <see cref="WKTMotherWrath.MAX_LINE_LENGTH"/> characters).
        /// </summary>
        MotherLine = 70,
        /// <summary>
        /// Client to host and host to a client, reliable: a player pressed a button on a creature another machine simulates,
        /// such as an explosive roach or a grub sister to pet, which runs there. The host passes a client's on to the client
        /// simulating it. Payload: the creature's handle, then the button event's index (see <see cref="WKTEntityEvents"/>).
        /// </summary>
        EntityInteraction = 71,
        /// <summary>
        /// Host to clients, reliable: a world entity gibbed on the host (<c>GameEntity.Gib</c>), usually a body shot apart, so
        /// every client's copy gibs too. Payload: the handle.
        /// </summary>
        EntityGib = 72,
        /// <summary>
        /// Client to host, reliable: the client's player's perk ids, sent when they change, for enemy code asking the player it
        /// goes for about a perk. Payload: see <see cref="WKTPlayerPerks.Write"/>.
        /// </summary>
        PlayerPerks = 73,
        /// <summary>
        /// Client to host and host to clients, reliable: a held enemy shot of the sender's was let go. The host passes on a
        /// client's. Payload: <see cref="WKTShotPose.WriteReleased"/>.
        /// </summary>
        ProjectileReleased = 74,
        /// <summary>
        /// Client to host and host to clients, reliable: an enemy shot of the sender's that stays where it hits came to rest, or
        /// (from the host) one resting in the world for a player who just joined. Payload: <see cref="WKTShotPose.WriteStuck"/>.
        /// </summary>
        ProjectileStuck = 75,
        /// <summary>Client to host and host to clients, reliable: a resting enemy shot of the sender's went away. Payload: the shot id.</summary>
        ProjectileGone = 76,
        /// <summary>
        /// Client to host and host to clients, reliable: what a player holds in each hand, when it changes. The host passes on
        /// a client's and sends everyone's to a player who joins. Payload: <see cref="WKTHeldItemsState"/>.
        /// </summary>
        HeldItems = 77,
        /// <summary>
        /// Client to host and host to clients, reliable: a run event for every player's chat, such as a player choosing a perk.
        /// The host passes on a client's. Payload: the rich text (see <see cref="WKTChat.PostEvent"/>).
        /// </summary>
        ChatEvent = 78,
        /// <summary>
        /// Client to host and host to clients, reliable: a player's flashlight, when it changes (a few times a second at most).
        /// The host passes on a client's and sends everyone's to a player who joins. Payload: <see cref="WKTPlayerLightState"/>.
        /// </summary>
        PlayerLight = 79,
        /// <summary>Client to host, reliable: the client started or finished loading (see <see cref="WKTLoadingSync"/>). Payload: a bool.</summary>
        LoadingState = 80,
        /// <summary>Host to clients, reliable: how many players the run waits for to finish loading; above zero, everyone is frozen. Payload: a byte.</summary>
        LoadingHold = 81,
        /// <summary>Client to host, reliable: the client's roaches changed with shared roaches. Payload: the change, then how much of it the host held back. See <see cref="WKTSharedRoaches"/>.</summary>
        RoachChange = 82,
        /// <summary>Host to clients, reliable: the shared purse's total. Payload: an int.</summary>
        RoachTotal = 83,
        /// <summary>Client to host, reliable: the client wants to buy a priced perk. Payload: request number, cost.</summary>
        RoachSpendRequest = 84,
        /// <summary>Host to a client, reliable: whether the purse holds the cost back for the purchase. Payload: request number, approved, cost.</summary>
        RoachSpendAnswer = 85,
        /// <summary>Client to host, reliable: the client slotted a save disk to revive the dead (see <see cref="WKTDiskRevives"/>). Payload: where the client stands.</summary>
        ReviveRequest = 86,
        /// <summary>Host to clients, reliable: a dead player comes back. Payload: player id, position, yaw.</summary>
        Revive = 87,
        /// <summary>
        /// Host to clients, and client to host for flashflowers, reliable: a level hazard acts, such as a death wall starting or
        /// lightning striking (see <see cref="WKTHazardSync"/>). Payload: the hazard's content key, then the call.
        /// </summary>
        HazardCall = 88,
        /// <summary>
        /// Client to host and host to clients, reliable: where a hand-moved level part is now, a crank door's open amount or a
        /// crane platform's position (see <see cref="WKTMovingParts"/>). Payload: kind, content key, value, speed.
        /// </summary>
        MovingPart = 89,
        /// <summary>
        /// Client to host and host to clients, unreliable: a sound, particle or tracer the sender's held item played, to play
        /// at the same spot (see <see cref="WKTItemEffects"/>). The host passes on a client's.
        /// </summary>
        ItemEffect = 90,
        /// <summary>
        /// Client to host and host to clients, unreliable: where a player's blink eye or translocator aims, while they squeeze it;
        /// an empty prefab when they stop (see <see cref="WKTAimMarkers"/>). Payload: player id, item prefab, position.
        /// </summary>
        AimMarker = 91,
        /// <summary>
        /// Client to host and host to clients, unreliable: where a player's barnacle hook or grapple in one hand holds on, or
        /// that it let go (see <see cref="WKTTethers"/>). Payload: player id, hand, holds, point.
        /// </summary>
        Tether = 92,
        /// <summary>Client to host, reliable: the client used up one of the host's world items, such as in a craft, which the host removes. Payload: the handle.</summary>
        ItemConsume = 93,
        /// <summary>
        /// Client to host and host to clients, reliable: a player arrived before Mother with their record, or the host's verdict
        /// for the group (see <see cref="WKTMotherJudgement"/>). Payload: kind, guilty.
        /// </summary>
        MotherJudgement = 94,
        /// <summary>
        /// Client to host and host to one player, reliable: a step of taking the item out of another player's hand, the
        /// thief's request or the victim's hand-over (see <see cref="WKTItemStealing"/>). Payload: <see cref="WKTItemSteal"/>.
        /// </summary>
        ItemSteal = 95,
        /// <summary>
        /// Client to host and host to clients, reliable: someone started a shortcut there, and everyone near is pulled to a spot
        /// around them. Payload: position.
        /// </summary>
        GatherPull = 96,
        /// <summary>
        /// Client to host, reliable: a chat message (payload: text). Host to clients, reliable: a message the host let through,
        /// sent to everyone including its sender (payload: player id, text). See <see cref="WKTChat"/>.
        /// </summary>
        Chat = 97,
        /// <summary>Host to one client, reliable: the host dropped their chat message as spam. Payload: <see cref="WKTChatVerdict"/>.</summary>
        ChatNotice = 98,
        /// <summary>
        /// Client to host, unreliable: one 20 ms frame of the client's voice (payload: <see cref="WKTVoiceFrame"/>). Host to
        /// clients, unreliable: a player's frame, sent only to those allowed to hear it (payload: speaker id, then
        /// <see cref="WKTVoiceFrame"/>). See <see cref="WKTVoiceManager"/>.
        /// </summary>
        VoiceFrame = 99,
        /// <summary>
        /// Client to host and host to clients, reliable: a step of two players taking hold of each other's hands with Grab
        /// Assists (see <see cref="WKTGrabAssist"/>). Payload: <see cref="WKTHandLink"/>.
        /// </summary>
        HandLink = 100,
        /// <summary>Host to clients, unreliable: where the race orb is, in wire space (see <see cref="WKTRace"/>). Payload: position.</summary>
        RaceOrb = 101,
        /// <summary>Client to host, reliable: the client reached the end of the run during a race. No payload.</summary>
        RaceFinish = 102,
        /// <summary>Host to clients, reliable: the race is over. Payload: the winner's id (255 for nobody), whether they finished.</summary>
        RaceOver = 103,
        /// <summary>
        /// Client to host, reliable: the client handed Mother the item from their hand (see <see cref="WKTMotherTrades"/>).
        /// Payload: Mother's handle, the item's prefab, its data.
        /// </summary>
        MotherTrade = 104,
        /// <summary>Host to one client, reliable: what their trade with Mother earned them. Payload: <see cref="WKTMotherRewardKind"/>, value.</summary>
        MotherReward = 105,
        /// <summary>
        /// Client to host, then host to the other clients, reliable: a player found an item no one had held before (see
        /// <see cref="WKTRunEvents"/>), shown in everyone's chat. Payload: (host to clients only) the finder's id, the item's
        /// name, its outline color as three bytes.
        /// </summary>
        ItemFound = 106,
        /// <summary>
        /// Client to host, then host to the other clients, reliable: which level a player is in (see <see cref="WKTPlayerRooms"/>),
        /// sent when it changes and every few seconds. Payload: (host to clients only) the player's id, the level's content key.
        /// </summary>
        PlayerRoom = 107,
        /// <summary>
        /// Client to host, then host to the other clients, reliable: a player teleported through a ladder vent blocker, which
        /// is removed for everyone (see <see cref="WKTLadderSecret"/>). Payload: the level's content key, the blocker's path
        /// below the level.
        /// </summary>
        VentOpened = 108,
        /// <summary>
        /// Client to host (no payload: the sender fell), then host to clients, reliable: the ladder nightmare attacks a player
        /// (see <see cref="WKTLadderSecret"/>). Payload: (host to clients only) the victim's id.
        /// </summary>
        LadderNightmare = 109,
        /// <summary>
        /// Host to clients, unreliable, every few seconds: each player's round trip to the host, for the lobby (see
        /// <see cref="WKTPings"/>). Payload: count, then each player's id and ping in milliseconds (ushort).
        /// </summary>
        PlayerPings = 110,
        /// <summary>
        /// Client to host, then host to everyone else, reliable: a level button's own state, after a press or (host only) when
        /// level scripting turned it on or off (see <see cref="WKTButtonSync"/>). Payload: <see cref="WKTButtonState"/>.
        /// </summary>
        ButtonState = 111,
        /// <summary>
        /// Client to host and host to clients, reliable: a player's body size and the perks that replace their sounds, such as
        /// Roach Mode's, when they change. The host passes on a client's and sends everyone's to a player who joins (see
        /// <see cref="WKTPlayerBodies"/>). Payload: <see cref="WKTPlayerBodyState"/>.
        /// </summary>
        PlayerBody = 112,
        /// <summary>
        /// Client to host and host to clients, reliable: everything a player carries, when it changes. The host passes on a
        /// client's and sends everyone's to a player who joins, so whoever hosts when a player leaves drops it all where they
        /// were (see <see cref="WKTPlayerInventories"/>). Payload: (host to clients only) the player's id, then
        /// <see cref="WKTInventorySnapshot"/>.
        /// </summary>
        PlayerInventory = 113,
        /// <summary>
        /// Client to host (payload: computer id), then host to everyone else (payload: player id, computer id), reliable: a
        /// player picked a perk at an in-world computer, which a button may wait for (see <see cref="WKTButtonSync"/>).
        /// </summary>
        PerkPicked = 114,
        /// <summary>
        /// Client to host, then host to everyone else, reliable: a player left the Bazaar, and everyone in one leaves with them
        /// (see <see cref="WKTBazaar"/>). No payload.
        /// </summary>
        ChamberLeave = 115,
        /// <summary>
        /// Client to host, reliable: the client paid for a Bazaar item, which the host marks paid and binds to everyone again
        /// (see <see cref="WKTWorldSync.OnLocalPaid"/>). Payload: the handle.
        /// </summary>
        ItemPaid = 116,
        /// <summary>
        /// Client to host, reliable: the client went through a seamless teleporter, and the host moves its own objects in the
        /// teleporter's box along (see <see cref="SeamlessTeleporter_Patch"/>). Payload: level key, teleporter key.
        /// </summary>
        SeamlessTeleport = 117,
        /// <summary>
        /// Client to host, reliable: a session event's locator spawner made enemies on the client, which the host spawns for
        /// everyone (see <see cref="WKTEventSpawns"/>). Payload: level key, locator id, the place's spawn index, count, then
        /// each one's asset id, position and rotation.
        /// </summary>
        EventSpawn = 118,
        /// <summary>
        /// Client to host, reliable: the client's mass buffs, whatever gave them, which the shared goo goes by (see
        /// <see cref="WKTPlayerPerks"/>). Payload: speed, resist and slow sums.
        /// </summary>
        PlayerMassBuffs = 119,
        /// <summary>
        /// Client to host, reliable: something only the client ran started a timer whose world events only the host fires,
        /// and the host starts its own (see <see cref="WKTDoorSync.OnTimerPlay"/>). Payload: level key, timer key, reset.
        /// </summary>
        TimerPlay = 120,
        /// <summary>
        /// Host to clients, reliable: the Steam accounts banned this session, kept by every client so a player who takes over
        /// as host turns them away too. Payload: count, then each Steam id.
        /// </summary>
        BanList = 121,
        /// <summary>
        /// Client to host and host to clients near the drone, reliable: the Buddy drone said an event line, which it plays
        /// straight from an audio clip rather than a named sound (see <see cref="WKTEntitySounds.BeforeBuddyLine"/>). Payload:
        /// entity handle, clip name.
        /// </summary>
        BuddyLine = 122,
        /// <summary>
        /// Host to one client, reliable: the host's Mother started crushing that client, whose camera she now pulls toward her
        /// (see <see cref="WKTMotherWrath.OnGlare"/>). Payload: entity handle.
        /// </summary>
        MotherGlare = 123
    }
}
