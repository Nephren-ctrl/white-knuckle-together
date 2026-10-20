namespace WhiteKnuckleTogether {
    public static class WKTLobbyConsts {
        public const int MAX_PLAYERS = 4;
        public const string KEY_GAMEMODE = "gamemode";
        public const string KEY_PLAY_MODE = "playmode";
        public const string KEY_GOO_FOLLOW = "goofollow";
        public const string KEY_ORB_RADIUS = "orbradius";
        public const string KEY_PLAYER_COLLISION = "playercollision";
        public const string KEY_MODIFIER = "modifiers";
        /// <summary>The shortcuts every player in the lobby has unlocked. Clients show the rest as redacted.</summary>
        public const string KEY_SHORTCUTS_AVAILABLE = "shortcutsavailable";
        /// <summary>The trinkets and bindings every player in the lobby has unlocked, by asset name (see <see cref="WKTTrinkets"/>).</summary>
        public const string KEY_TRINKETS_AVAILABLE = "trinketsavailable";
        /// <summary>The available shortcuts the host has ticked.</summary>
        public const string KEY_SHORTCUTS = "shortcuts";
        /// <summary>Member data: the ids of the voice cosmetics a player has active.</summary>
        public const string KEY_VOICES = "voices";
        /// <summary>Member data: "1" when a player turned voice chat off, so they neither hear nor are heard.</summary>
        public const string KEY_VOICE_CHAT_OFF = "vcoff";
        /// <summary>Member data: "1" while a player is ready to start the run, on the trinket screen.</summary>
        public const string KEY_READY = "ready";
        /// <summary>Set by the host while every player is ready and the run is about to start: a new number each time it starts counting.</summary>
        public const string KEY_COUNTDOWN = "countdown";
        /// <summary>"1" while the host has the lobby on the trinket screen, where everyone picks trinkets and readies.</summary>
        public const string KEY_PICKING = "picking";
        /// <summary>Marks a lobby as this mod's, so the lobby browser only lists those. Always <see cref="MOD_ID"/>.</summary>
        public const string KEY_MOD = "mod";
        public const string MOD_ID = "WhiteKnuckleTogether";
        /// <summary>"1" on public lobbies, the only ones the lobby browser lists. Join code lobbies are public to Steam but not listed.</summary>
        public const string KEY_LISTED = "listed";
        /// <summary>
        /// A join code lobby's code, <see cref="JOIN_CODE_LENGTH"/> digits, which the lobby browser searches for. Steam only
        /// searches lobby data, so the code can be read by anyone who lists lobbies through Steam directly.
        /// </summary>
        public const string KEY_JOIN_CODE = "joincode";
        public const int JOIN_CODE_LENGTH = 5;
        /// <summary>Every number of <see cref="JOIN_CODE_LENGTH"/> digits, leading zeros included.</summary>
        public const int JOIN_CODE_COUNT = 100000;
        /// <summary>The host's mod version. Only lobbies of the same mod and game version can be joined.</summary>
        public const string KEY_MOD_VERSION = "modversion";
        /// <summary>The host's game version.</summary>
        public const string KEY_GAME_VERSION = "gameversion";
        /// <summary>The lobby's name, as the host set it. See <see cref="MAX_LOBBY_NAME_LENGTH"/>.</summary>
        public const string KEY_NAME = "name";
        /// <summary>The names of the players in the session, one per line, the host first. Steam only lists members to members.</summary>
        public const string KEY_MEMBERS = "members";
        /// <summary>"1" while the host is in a run, which cannot be joined.</summary>
        public const string KEY_IN_RUN = "inrun";
        /// <summary>The host's Steam relay ping location, from which the lobby browser estimates each lobby's ping.</summary>
        public const string KEY_PING_LOCATION = "pinglocation";
        /// <summary>Seconds from everyone being ready to the run starting.</summary>
        public const float READY_COUNTDOWN = 3f;
        /// <summary>Room for a Steam name (at most 32 characters) and "'s Lobby".</summary>
        public const int MAX_LOBBY_NAME_LENGTH = 40;
        /// <summary>Steam names are at most 32 characters.</summary>
        public const int MAX_MEMBER_NAME_LENGTH = 32;
        public const int MAX_VOICES = 8;
        public const int MAX_VOICE_ID_LENGTH = 96;
    }
}
