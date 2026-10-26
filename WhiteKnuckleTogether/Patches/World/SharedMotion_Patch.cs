using HarmonyLib;
using JetBrains.Annotations;

namespace WhiteKnuckleTogether {
    /// <summary>Looping path movers take their place in the loop from the shared clock (see <see cref="WKTSharedMotion"/>).</summary>
    [WKTCriticalPatch(WKTPatchCategory.WorldSync), HarmonyPatch(typeof(UT_PathMover), "Awake")]
    internal sealed class PathMoverPhase_Patch {
        [UsedImplicitly]
        private static void Postfix(UT_PathMover __instance) => WKTSharedMotion.AfterMoverAwake(__instance);
    }

    /// <summary>A Delta Labs security field switches off once it reached the top of its room (see <see cref="WKTSharedMotion"/>).</summary>
    [WKTCriticalPatch(WKTPatchCategory.WorldSync), HarmonyPatch(typeof(UT_PathMover), "ArriveAtNode")]
    internal sealed class SecurityFieldEnd_Patch {
        [UsedImplicitly]
        private static void Prefix(UT_PathMover __instance, out bool __state) => __state = WKTSharedMotion.IsFieldReachingEnd(__instance);

        [UsedImplicitly]
        private static void Postfix(UT_PathMover __instance, bool __state) {
            if (__state)
                WKTSharedMotion.OnFieldReachedEnd(__instance);
        }
    }

    /// <summary>Spinners at full speed take their angle from the shared clock.</summary>
    [WKTCriticalPatch(WKTPatchCategory.WorldSync), HarmonyPatch(typeof(UT_Spin), "Update")]
    internal sealed class SpinPhase_Patch {
        [UsedImplicitly]
        private static bool Prefix(UT_Spin __instance) => !WKTSharedMotion.TrySpin(__instance);
    }

    /// <summary>A spinner a button starts or stops speeds up and slows down on its own from then on (see <see cref="WKTSharedMotion"/>).</summary>
    [WKTCriticalPatch(WKTPatchCategory.WorldSync), HarmonyPatch(typeof(UT_Spin), nameof(UT_Spin.SetSpinning))]
    internal sealed class SpinToggle_Patch {
        [UsedImplicitly]
        private static void Prefix(UT_Spin __instance, bool b) {
            if (__instance.spinning != b)
                WKTSharedMotion.OnSpinToggled(__instance);
        }
    }

    /// <summary>Gears take their angle from the shared clock.</summary>
    [WKTCriticalPatch(WKTPatchCategory.WorldSync), HarmonyPatch(typeof(UT_Gear), "Update")]
    internal sealed class GearPhase_Patch {
        [UsedImplicitly]
        private static bool Prefix(UT_Gear __instance) => !WKTSharedMotion.TryTurnGear(__instance);
    }
}
