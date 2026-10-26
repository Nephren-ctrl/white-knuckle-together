using System;
using HarmonyLib;
using JetBrains.Annotations;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// A date-gated spawn chance reads the run's shared clock in a multiplayer run (see <see cref="WKTSharedDate"/>), so every
    /// player gets the same season and the same world. Singleplayer reads the local clock as the game does.
    /// </summary>
    [WKTCriticalPatch(WKTPatchCategory.RunSync), HarmonyPatch(typeof(SSM_DateTimeRange), nameof(SSM_DateTimeRange.Check))]
    internal sealed class DateTimeRange_Patch {
        [UsedImplicitly]
        private static bool Prefix(SSM_DateTimeRange __instance, ref float __result) {
            var network = WKT.InstanceOrNull?.Network;
            if (network == null || !network.IsMultiplayerRun) return true;
            __result = WKTSharedDate.Check(__instance.startTime, __instance.endTime, new DateTime(network.Session.Run.ClockTicks), __instance.trueValue,
                __instance.falseValue);
            return false;
        }
    }
}
