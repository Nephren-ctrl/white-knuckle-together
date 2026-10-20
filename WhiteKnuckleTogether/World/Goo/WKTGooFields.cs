using HarmonyLib;

namespace WhiteKnuckleTogether {
    /// <summary>The death goo's private state. Kept apart, since field references cannot be built in the test host.</summary>
    internal static class WKTGooFields {
        public static readonly AccessTools.FieldRef<DEN_DeathFloor, bool> Active = AccessTools.FieldRefAccess<DEN_DeathFloor, bool>("active");
        public static readonly AccessTools.FieldRef<DEN_DeathFloor, bool> HasStarted = AccessTools.FieldRefAccess<DEN_DeathFloor, bool>("hasStarted");
        public static readonly AccessTools.FieldRef<DEN_DeathFloor, bool> IsRecentering = AccessTools.FieldRefAccess<DEN_DeathFloor, bool>("isRecentering");
    }
}
