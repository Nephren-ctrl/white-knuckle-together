using System;
using System.Collections.Generic;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// Where each enemy type keeps its target, for <see cref="WKTEngagement"/>, from the game's own code. Looked up once per
    /// type. Kept apart from the field accessors so it can be tested without the game's runtime.
    /// </summary>
    internal static class WKTEngagementKinds {
        /// <summary>
        /// Turrets stay the host's: one simulated by a client could only see that client's player and shoot no one else.
        /// Barnacle zombies too: their attack is the tongues of the barnacle riding them, which the host always simulates.
        /// </summary>
        private static readonly Type[] s_excluded = { typeof(DEN_Turret), typeof(DEN_Barnacle_Zombie) };
        private static readonly Dictionary<Type, WKTEngagementKind> s_kinds = new();

        public static WKTEngagementKind Of(Type type) {
            if (s_kinds.TryGetValue(type, out var kind)) return kind;
            kind = Classify(type);
            s_kinds[type] = kind;
            return kind;
        }

        private static WKTEngagementKind Classify(Type type) {
            foreach (var excluded in s_excluded) {
                if (excluded.IsAssignableFrom(type)) return WKTEngagementKind.None;
            }
            if (typeof(DEN_BasicDenizen).IsAssignableFrom(type) || typeof(DEN_BasicNavigator).IsAssignableFrom(type) ||
                typeof(DEN_BasicFlier).IsAssignableFrom(type)) return WKTEngagementKind.TargetComponent;
            if (typeof(DEN_Bloodbug).IsAssignableFrom(type)) return WKTEngagementKind.Bloodbug;
            return typeof(DEN_Drone).IsAssignableFrom(type) ? WKTEngagementKind.Drone : WKTEngagementKind.None;
        }
    }

    /// <summary>How an enemy type keeps its target.</summary>
    internal enum WKTEngagementKind : byte {
        /// <summary>Not handed to the player it is engaged with: the host always simulates it.</summary>
        None,
        /// <summary><c>targetComponent</c>, an <c>AITargetComponent</c>, and <c>AIGameEntity.target</c>.</summary>
        TargetComponent,
        /// <summary>The bloodbug's private <c>bloodbugTarget</c> record.</summary>
        Bloodbug,
        /// <summary>The drone's private <c>droneTarget</c> record.</summary>
        Drone
    }
}
