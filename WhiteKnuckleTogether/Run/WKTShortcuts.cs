using System;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// What the mod knows about each <see cref="ShortcutId"/>, read from the game's assets (see GAME_INTERNALS.md). A
    /// shortcut is available in a multiplayer run only when every player has its achievement, which the game grants the first
    /// time a player uses it. An available shortcut the host ticks is open with no code, key
    /// or first-time step; anything else is closed for the run. Pure, so it can be tested without Unity.
    /// </summary>
    internal static class WKTShortcuts {
        /// <summary>Shown in place of the name of a shortcut not every player has unlocked.</summary>
        public const string REDACTED = "[REDACTED]";
        /// <summary>The event a shortcut's computer plays once it is unlocked.</summary>
        public const string UNLOCK_EVENT = "unlock";
        public static readonly ShortcutId[] All = (ShortcutId[])Enum.GetValues(typeof(ShortcutId));
        /// <summary>Every shortcut bit.</summary>
        public static readonly byte AllMask = BuildAllMask();

        public static byte GetBit(ShortcutId id) => (byte)(1 << (int)id);

        public static bool Has(byte mask, ShortcutId id) => (mask & GetBit(id)) != 0;

        /// <summary>The name the game gives the shortcut's region.</summary>
        public static string GetName(ShortcutId id) => id switch {
            ShortcutId.TangledSink => "Tangled Sink",
            ShortcutId.ExpulsionChute => "Expulsion Chute",
            ShortcutId.RottenDuct => "Rotten Duct",
            ShortcutId.HadalDiver => "Hadal Diver",
            _ => throw new ArgumentOutOfRangeException(nameof(id), id, null)
        };

        /// <summary>The name to show for a shortcut: its own if every player has unlocked it, <see cref="REDACTED"/> otherwise.</summary>
        public static string GetShownName(ShortcutId id, byte availableMask) => Has(availableMask, id) ? GetName(id) : REDACTED;

        /// <summary>
        /// The achievement the game grants the first time a player uses the shortcut. Each shortcut's "Shortcut Unlocked"
        /// progression unlock asks for exactly this, but the game only lists two of those unlocks, so the achievement is what
        /// tells whether a player has been through it.
        /// </summary>
        public static string GetAchievementId(ShortcutId id) => id switch {
            ShortcutId.TangledSink => "ACH_TANGLEDSINK",
            ShortcutId.ExpulsionChute => "ACH_EXPULSIONCHUTE",
            ShortcutId.RottenDuct => "ACH_ROTTENDUCT",
            ShortcutId.HadalDiver => "ACH_HADALDIVER",
            _ => throw new ArgumentOutOfRangeException(nameof(id), id, null)
        };

        /// <summary>The level on the main route that leads into the shortcut, as the game names its prefab.</summary>
        public static string GetLevelName(ShortcutId id) => id switch {
            ShortcutId.TangledSink => "M1_Intro_01",
            ShortcutId.ExpulsionChute => "Campaign_Interlude_Silo_To_Pipeworks_01",
            ShortcutId.RottenDuct => "M3_Habitation_Shaft_Intro",
            ShortcutId.HadalDiver => "Campaign_Interlude_Habitation_To_Abyss_01",
            _ => throw new ArgumentOutOfRangeException(nameof(id), id, null)
        };

        /// <summary>
        /// The object in the shortcut's level that opens it, relative to the level, or null when a flag check opens it
        /// instead (<see cref="GetFlag"/>):
        /// <list type="bullet">
        /// <item>Tangled Sink: the vent door the intro computer opens once its code is entered.</item>
        /// <item>Rotten Duct: the prison door's lever, unlocked by a key or by having visited Dead Man's Handle.</item>
        /// <item>Hadal Diver: the research pod's computer, whose <see cref="UNLOCK_EVENT"/> makes the call lever usable.</item>
        /// </list>
        /// </summary>
        public static string? GetTargetPath(ShortcutId id) => id switch {
            ShortcutId.TangledSink => "Entities/Props/Door_Vent_01/Door_Habitation_Room_01",
            ShortcutId.RottenDuct => "Entities/Props/Prop_Door_Habitation_01/Prop_Prison_Door_Root",
            ShortcutId.HadalDiver => "Exterior/HadalDiver/Call Platform/Research Pod/Prop_ComputerConsole_Basic/OS_Computer",
            _ => null
        };

        /// <summary>
        /// The flag the game checks to open the shortcut, or null. The Expulsion Chute's button and the Hadal Diver's
        /// computer both wait for a flag check, which the mod answers from the host's choice instead.
        /// </summary>
        public static string? GetFlag(ShortcutId id) => id switch {
            ShortcutId.ExpulsionChute => "chutedoorunlocked",
            ShortcutId.HadalDiver => "hadaldiverunlocked",
            _ => null
        };

        /// <summary>The shortcut whose flag is <paramref name="flagName"/>. False for every other flag.</summary>
        public static bool TryGetByFlag(string? flagName, out ShortcutId id) {
            foreach (var shortcut in All) {
                if (GetFlag(shortcut) != flagName || flagName == null) continue;
                id = shortcut;
                return true;
            }
            id = default;
            return false;
        }

        /// <summary>Whether a level object named <paramref name="levelName"/> is the shortcut's level, clone or not.</summary>
        public static bool IsLevel(string levelName, ShortcutId id) {
            var name = GetLevelName(id);
            if (!levelName.StartsWith(name, StringComparison.Ordinal)) return false;
            return levelName.Length == name.Length || levelName[name.Length] == '(';
        }

        /// <summary>The shortcuts every player has unlocked, from the session's shared progression.</summary>
        public static byte GetAvailableMask(WKTProgressionProfile shared) {
            byte mask = 0;
            foreach (var id in All) {
                if (shared.HasAchievement(GetAchievementId(id)))
                    mask |= GetBit(id);
            }
            return mask;
        }

        /// <summary>
        /// The shortcuts open in a run: ticked, available to every player, and never with Iron Knuckle, which turns
        /// shortcuts off in the game itself.
        /// </summary>
        public static byte GetOpenMask(byte ticked, byte available, bool ironKnuckle) => ironKnuckle ? (byte)0 : (byte)(ticked & available & AllMask);

        /// <summary>
        /// The host's ticks from what it has unticked: every available shortcut is ticked unless the host unticked it, so a
        /// shortcut that becomes available is on by default.
        /// </summary>
        public static byte GetTicked(byte available, byte unticked) => (byte)(available & ~unticked & AllMask);

        /// <summary>The host's unticked set after it ticks or unticks <paramref name="id"/>.</summary>
        public static byte SetUnticked(byte unticked, ShortcutId id, bool ticked) =>
            ticked ? (byte)(unticked & ~GetBit(id)) : (byte)(unticked | GetBit(id));

        private static byte BuildAllMask() {
            byte mask = 0;
            foreach (var id in All)
                mask |= GetBit(id);
            return mask;
        }
    }
}
