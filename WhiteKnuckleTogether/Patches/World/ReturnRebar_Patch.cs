using HarmonyLib;
using JetBrains.Annotations;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// A shared return rebar's recall prompt and return only run where the rebar is the local player's to recall or flies to
    /// them. Copies flying to someone else steer toward that player instead.
    /// </summary>
    [WKTCriticalPatch(WKTPatchCategory.WorldSync), HarmonyPatch(typeof(Projectile_ReturnRebar), "ReturnUpdate")]
    internal sealed class ReturnRebarUpdate_Patch {
        [UsedImplicitly]
        private static bool Prefix(Projectile_ReturnRebar __instance) {
            var sync = WKTWorldSync.Instance;
            if (sync == null || !__instance.TryGetComponent<WKTReturnRebarCopy>(out var copy)) return true;
            return sync.ReturnRebars.ShouldRunReturnUpdate(copy);
        }
    }

    /// <summary>
    /// A living thrower recalling their own rebar recalls it for everyone. Recalling a dead or absent player's rebar asks the
    /// host first, and nothing else starts a shared rebar's return outside the machine deciding it.
    /// </summary>
    [WKTCriticalPatch(WKTPatchCategory.WorldSync), HarmonyPatch(typeof(Projectile_ReturnRebar), "StartReturn")]
    internal sealed class ReturnRebarStartReturn_Patch {
        [UsedImplicitly]
        private static bool Prefix(Projectile_ReturnRebar __instance, out bool __state) {
            __state = false;
            var sync = WKTWorldSync.Instance;
            return sync == null || sync.ReturnRebars.OnStartReturn(__instance, out __state);
        }

        [UsedImplicitly]
        private static void Postfix(Projectile_ReturnRebar __instance, bool __state) {
            if (__state)
                WKTWorldSync.Instance?.ReturnRebars.OnLocalRecall(__instance);
        }
    }

    /// <summary>A shared return rebar sticking in a wall on the machine deciding it sticks there for everyone.</summary>
    [WKTCriticalPatch(WKTPatchCategory.WorldSync), HarmonyPatch(typeof(Projectile_ReturnRebar), "CreateHitEffect")]
    internal sealed class ReturnRebarStuck_Patch {
        [UsedImplicitly]
        private static void Prefix(bool ___hasHitSurface, out bool __state) => __state = ___hasHitSurface;

        [UsedImplicitly]
        private static void Postfix(Projectile_ReturnRebar __instance, bool ___hasHitSurface, bool __state) {
            if (!__state && ___hasHitSurface)
                WKTWorldSync.Instance?.ReturnRebars.OnLocalStuck(__instance);
        }
    }
}
