using System;
using System.Collections.Generic;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// Where the game keeps each <see cref="WKTPlayerSoundId"/>: the group and set names in the player's
    /// <see cref="UT_AudioClipHandler"/>. Footsteps are not in a set; the player's <c>footsteps</c> list holds them. A grab's
    /// set depends on the handhold's material (see <see cref="GetGrabSetName"/>).
    /// </summary>
    internal static class WKTPlayerSoundSets {
        /// <summary>Voice cosmetics replace footsteps through this group, with one set per material name.</summary>
        public const string FOOTSTEP_GROUP = "footstep";
        public const string FATIGUE_GROUP = "fatigue";
        /// <summary>The loop that grows louder as a hanging player's grip runs out.</summary>
        public const string FATIGUE_LEVEL_SET = "level";
        public const string CLIMBING_GROUP = "climbing";
        /// <summary>The grab sound of a handhold whose material has none of its own.</summary>
        public const string GRAB_SET = "handhold";
        private const string c_standard_material = "standard";
        private const string c_movement_group = "movement";
        private const string c_perk_group = "perk";
        /// <summary><c>CL_Handhold.GetMaterialType</c>'s names (the material's name in lower case) to their values.</summary>
        private static readonly Dictionary<string, byte> s_handholdMaterials = CreateHandholdMaterials();
        private static readonly string?[] s_groups = new string?[(int)WKTPlayerSound.MAX_ID + 1];
        private static readonly string?[] s_sets = new string?[(int)WKTPlayerSound.MAX_ID + 1];

        static WKTPlayerSoundSets() {
            Add(WKTPlayerSoundId.Jump, c_movement_group, "jump");
            Add(WKTPlayerSoundId.DoubleJump, c_perk_group, "doublejump");
            Add(WKTPlayerSoundId.JumpBoost, c_perk_group, "jumpboost");
            Add(WKTPlayerSoundId.LandSoft, c_movement_group, "land-soft");
            Add(WKTPlayerSoundId.LandMedium, c_movement_group, "land-medium");
            Add(WKTPlayerSoundId.LandHeavy, c_movement_group, "land-heavy");
            // The game plays a material's own landing clip through the heavy landing set, with that set's settings.
            Add(WKTPlayerSoundId.LandMaterial, c_movement_group, "land-heavy");
            Add(WKTPlayerSoundId.Mantle, c_movement_group, "mantle");
            Add(WKTPlayerSoundId.FatigueDrop, FATIGUE_GROUP, "drop");
            Add(WKTPlayerSoundId.Release, CLIMBING_GROUP, "drop");
            Add(WKTPlayerSoundId.JumpBoostPrime, c_perk_group, "jumpboost-prime");
        }

        /// <summary>
        /// The set a grab of a handhold made of <paramref name="material"/> (<c>CL_Handhold.GetMaterialType</c>) plays, as
        /// <c>ENT_Player.PlayHandholdSound</c> picks it: the material's own set if <paramref name="handler"/> has one, else
        /// <see cref="GRAB_SET"/>.
        /// </summary>
        public static string GetGrabSetName(UT_AudioClipHandler handler, string material) {
            if (material == c_standard_material) return GRAB_SET;
            var own = GRAB_SET + "-" + material;
            return handler.HasSound(CLIMBING_GROUP + ":" + own) ? own : GRAB_SET;
        }

        /// <summary>The value of a handhold material named as <c>CL_Handhold.GetMaterialType</c> names it. Unknown names are standard.</summary>
        public static byte GetHandholdMaterial(string? material) {
            s_handholdMaterials.TryGetValue(material ?? string.Empty, out var value);
            return value;
        }

        /// <summary>False for sounds without a fixed set, such as <see cref="WKTPlayerSoundId.Footstep"/>.</summary>
        public static bool TryGetNames(WKTPlayerSoundId id, out string group, out string set) {
            group = set = string.Empty;
            if (!WKTPlayerSound.IsDefined(id) || s_groups[(int)id] == null) return false;
            group = s_groups[(int)id]!;
            set = s_sets[(int)id]!;
            return true;
        }

        /// <summary>
        /// Fills <paramref name="sets"/>, indexed by <see cref="WKTPlayerSoundId"/>, with <paramref name="handler"/>'s sets.
        /// Sounds without a set, or that the handler lacks, are null.
        /// </summary>
        public static void Resolve(UT_AudioClipHandler? handler, UT_AudioClipHandler.AudioSet?[] sets) {
            for (var id = 0; id < sets.Length; id++) {
                sets[id] = null;
                if (handler == null || !TryGetNames((WKTPlayerSoundId)id, out var group, out var set)) continue;
                sets[id] = handler.GetGroup(group)?.GetSet(set);
            }
        }

        private static Dictionary<string, byte> CreateHandholdMaterials() {
            var materials = new Dictionary<string, byte>();
            foreach (ObjectMaterial.MaterialType material in Enum.GetValues(typeof(ObjectMaterial.MaterialType))) {
                if ((int)material >= 0 && (int)material <= byte.MaxValue)
                    materials[material.ToString().ToLowerInvariant()] = (byte)material;
            }
            return materials;
        }

        private static void Add(WKTPlayerSoundId id, string group, string set) {
            s_groups[(int)id] = group;
            s_sets[(int)id] = set;
        }
    }
}
