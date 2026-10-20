using System;
using UnityEngine;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// Creatures that move in ways the usual relevance and pose streaming miss. Some are seen from far beyond the relevance
    /// distance across open spaces, such as the abyss's Shrales on their long paths; their states go to every player. Others
    /// are moved by their own components from the time alone, such as the abyss's floating rope bags (<c>UT_Wiggle</c> on the
    /// creature itself); with the shared clock (<see cref="WKTSharedClock"/>) they move the same everywhere, so their puppets
    /// keep their own pose.
    /// </summary>
    internal static class WKTAmbientMotion {
        /// <summary>Creature types players see from far away: Shrales, and the striders towering over the waste.</summary>
        public static readonly Type[] FarVisibleTypes = { typeof(DEN_PathSteerer), typeof(DEN_Strider) };

        public static bool IsFarVisible(Type type) {
            foreach (var farVisible in FarVisibleTypes) {
                if (farVisible.IsAssignableFrom(type)) return true;
            }
            return false;
        }

        /// <summary>Whether a world object's own components move it from the clock alone.</summary>
        public static bool IsSelfDriven(GameObject ownerObject) => ownerObject.TryGetComponent<UT_Wiggle>(out var wiggle) && wiggle.enabled;
    }
}
