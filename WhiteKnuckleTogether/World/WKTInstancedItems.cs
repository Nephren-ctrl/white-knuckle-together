using System;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// The campaign's placed progression pickups, which every player gets their own of in a synced co-op run: the flashlights
    /// and flare guns before and in the Teeth's shaft, the flashlight in the Abyss tram, and the handgun, its ammo, flare gun
    /// and flashlight in the computer safe rooms before the Nest. One player taking the only flashlight would leave the others
    /// in the dark. Each machine keeps its own copy (<see cref="WKTLocalObject"/>); a dropped copy is an ordinary shared item.
    /// <para>
    /// Only the items placed in those rooms (or spawned there by a fixed spawner) count, by their level and their path in it.
    /// Ammo enemies carry or drop is never instanced.
    /// </para>
    /// </summary>
    internal static class WKTInstancedItems {
        /// <summary>A level name prefix, and the path below the level of an item or of the spawner that spawns it.</summary>
        private static readonly (string Level, string Path)[] s_placed = {
            ("M3_Habitation_Entryway_01", "Sections/Gateway/M3_Habitation_Entryway/M3_Habitation_Entryway/Habitation_Entryway_Root/Structure_I2_GateControlFacility_Spawn/Structure_I2_GateControlFacility/Item_Flashlight"),
            ("M3_Habitation_Shaft_Intro", "Entities/Item_Flashlight"),
            ("M3_Habitation_Shaft_Intro", "Entities/Items/Item_Flaregun"),
            ("Campaign_Interlude_Chute_To_Habitation", "Entities/Item_Flashlight"),
            ("Campaign_Interlude_Chute_To_Habitation", "Entities/Items/Item_Flaregun"),
            ("Campaign_Interlude_Chute_To_Habitation", "Entities/Items/Item_Flaregun_Ammo"),
            ("Campaign_Interlude_Chute_To_Habitation", "Entities/Items/Item_Flaregun_Ammo.01"),
            ("Campaign_Interlude_Chute_To_Habitation", "Entities/Items/Item_Flaregun_Ammo.02"),
            ("Campaign_Interlude_Duct_To_Abyss_01", "Entities/Items/Item_Flashlight"),
            ("Campaign_Interlude_Duct_To_Abyss_01", "Entities/Items/Item_Flaregun"),
            ("Campaign_Interlude_Habitation_To_Abyss_01", "Exterior/M4_Interlude_Collapse_Anim.01/Tram_Interfacility_Offline_Front_01/Spawn_Item_Flashlight"),
            ("Campaign_Interlude_Habitation_To_Abyss_01", "Interior/Entities (1)/Spawn_Item_Flaregun"),
            ("Campaign_Interlude_Abyss_To_Nest_01_SafeArea", "Entities/Items/Handgun"),
            ("Campaign_Interlude_Abyss_To_Nest_01_SafeArea", "Entities/Items/Spawn_Item_Flaregun"),
            ("Campaign_Interlude_Abyss_To_Nest_01_SafeArea", "Entities/Items/Spawn_Item_Flashlight"),
            ("Campaign_Interlude_Diver_To_Nest_SafeArea_01", "Entities/Handgun"),
            ("Campaign_Interlude_Diver_To_Nest_SafeArea_01", "Entities/Item_Flaregun"),
            ("M3_Habitation_Endless_Start", "Entities/Item_Flashlight"),
            ("M3_Habitation_Endless_Start", "Entities/Items/Item_Flaregun"),
            ("M5_Nest_Endless_Start", "Entities/Handgun"),
            ("M5_Nest_Endless_Start", "Entities/Items/Spawn_Item_Flaregun"),
            ("M5_Nest_Endless_Start", "Entities/Items/Spawn_Item_Flaregun_Ammo"),
            ("M5_Nest_Endless_Transition_Intro", "Entities/Items/Handgun")
        };

        /// <summary>Whether <paramref name="item"/> is one of the placed progression pickups.</summary>
        public static bool IsInstanced(Item_Object item) {
            var level = item.GetComponentInParent<M_Level>(true);
            return level != null && IsListed(level.name, WKTGatherCheck.PathBelow(item.transform, level.transform));
        }

        /// <summary>
        /// Whether the item at <paramref name="path"/> below level <paramref name="levelName"/> is listed: the listed path
        /// itself, or anything below it (what a listed spawner spawns, the items of a listed group).
        /// </summary>
        public static bool IsListed(string levelName, string path) {
            foreach (var (level, placed) in s_placed) {
                if (!levelName.StartsWith(level, StringComparison.Ordinal) || !path.StartsWith(placed, StringComparison.Ordinal)) continue;
                if (path.Length == placed.Length || path[placed.Length] == '/') return true;
            }
            return false;
        }
    }
}
