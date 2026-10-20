using HarmonyLib;
using JetBrains.Annotations;

namespace WhiteKnuckleTogether {
    /// <summary>A door opened bit by bit here (a crank or lever) opens as far on every machine (see <see cref="WKTMovingParts"/>).</summary>
    [WKTCriticalPatch(WKTPatchCategory.WorldSync), HarmonyPatch(typeof(UT_Door), nameof(UT_Door.AddOpenAmount))]
    internal sealed class CrankDoorAdd_Patch {
        [UsedImplicitly]
        private static void Postfix(UT_Door __instance) {
            if (!WKTMovingParts.IsApplying)
                WKTDoorSync.Instance?.OnLocalDoorMoved(__instance);
        }
    }

    /// <summary>The same for a door set to an open amount.</summary>
    [WKTCriticalPatch(WKTPatchCategory.WorldSync), HarmonyPatch(typeof(UT_Door), nameof(UT_Door.SetOpenAmount))]
    internal sealed class CrankDoorSet_Patch {
        [UsedImplicitly]
        private static void Postfix(UT_Door __instance) {
            if (!WKTMovingParts.IsApplying)
                WKTDoorSync.Instance?.OnLocalDoorMoved(__instance);
        }
    }
}
