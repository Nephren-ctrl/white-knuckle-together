using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// Which enemies' hits on players are decided on the player's own machine, and where the ones using the game's basic attack
    /// module keep it. Looked up once per type. See <see cref="WKTEnemyHits"/> for how each kind is replayed.
    /// </summary>
    internal static class WKTAttackModules {
        private const BindingFlags c_fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        /// <summary>Enemies whose attack lands by touching the player, through their own collision handler.</summary>
        public static readonly Type[] ContactAttackTypes = { typeof(DEN_Drone), typeof(DEN_Bloodbug), typeof(DEN_BasicFlier) };
        private static readonly Dictionary<Type, FieldInfo?> s_moduleFields = new();

        /// <summary>The basic attack module an enemy keeps in a field, such as <c>DEN_BasicDenizen.attackModule</c>, if it has one.</summary>
        public static bool TryGet(Component? owner, out DenizenAttackModule_Basic? module) {
            module = null;
            if (owner == null) return false;
            var field = GetField(owner.GetType());
            module = field?.GetValue(owner) as DenizenAttackModule_Basic;
            return module != null;
        }

        /// <summary>The field holding an enemy type's attack module, most derived first.</summary>
        public static FieldInfo? GetField(Type type) {
            if (s_moduleFields.TryGetValue(type, out var cached)) return cached;
            FieldInfo? found = null;
            for (var current = type; current != null && current != typeof(MonoBehaviour) && found == null; current = current.BaseType) {
                foreach (var field in current.GetFields(c_fields)) {
                    if (!typeof(DenizenAttackModule).IsAssignableFrom(field.FieldType)) continue;
                    found = field;
                    break;
                }
            }
            s_moduleFields[type] = found;
            return found;
        }

        public static bool IsContactAttacker(Type type) => Array.IndexOf(ContactAttackTypes, type) >= 0;

        /// <summary>
        /// Whether damage from <paramref name="attacker"/> to a player is decided on that player's machine. Turrets shoot from
        /// their attack module's events, never through a hit check a client could replay, so the host decides their shots.
        /// </summary>
        public static bool IsClientDecided(Component? attacker) =>
            attacker is not DEN_Turret &&
            (attacker is DEN_Hunter || HasReplicatedTongues(attacker) || (attacker != null && IsContactAttacker(attacker.GetType())) || TryGet(attacker, out _));

        /// <summary>Whether a barnacle's tongues are the basic kind, which clients replay from the host (see <see cref="WKTTongueSync"/>).</summary>
        public static bool HasReplicatedTongues(Component? entity) =>
            entity is DEN_Barnacle barnacle && barnacle.tongueInterface is Barnacle_Tongue_Interface_Module_Basic;
    }
}
