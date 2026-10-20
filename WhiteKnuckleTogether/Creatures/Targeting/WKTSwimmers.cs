using HarmonyLib;
using UnityEngine;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// Swimmers (<c>DEN_SimpleSwimmer</c>, such as the Myzont) stay in the water they started in (<c>startingSwimZone</c>). In
    /// a synced co-op run a client's copy can start before that water exists there, so the zone is looked up again whenever
    /// it is missing, and only the host may kill one for having none. A swimmer that cannot reach players out of the water
    /// is only handed to players in it.
    /// </summary>
    internal static class WKTSwimmers {
        private static readonly AccessTools.FieldRef<DEN_SimpleSwimmer, UT_SwimZone?> s_zone = AccessTools.FieldRefAccess<DEN_SimpleSwimmer, UT_SwimZone?>("startingSwimZone");

        /// <summary>Looks the swimmer's water up again if it has none yet.</summary>
        public static void EnsureZone(DEN_SimpleSwimmer swimmer) {
            if (s_zone(swimmer) == null)
                s_zone(swimmer) = UT_SwimZone.GetTargetSwimZone(swimmer.transform.position);
        }

        /// <summary>Whether a swimmer without water of its own dies here, as the game's <c>Start</c> has it: never on a client.</summary>
        public static bool MayDieForNoWater() => WKTWorldSync.Instance is not { IsHost: false };

        /// <summary>Whether <paramref name="entity"/> may be handed to the player at <paramref name="position"/>.</summary>
        public static bool MayEngage(GameEntity? entity, Vector3 position) {
            if (entity is not DEN_SimpleSwimmer { canTargetOutsideOfWater: false } swimmer) return true;
            var zone = s_zone(swimmer);
            return zone == null || zone.IsPositionInSwimZone(position);
        }
    }
}
