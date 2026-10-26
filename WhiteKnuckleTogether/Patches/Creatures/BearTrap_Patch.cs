using HarmonyLib;
using JetBrains.Annotations;
using UnityEngine;

namespace WhiteKnuckleTogether {
    /// <summary>A bear trap is simulated by the player it holds (see <see cref="WKTBearTraps"/>).</summary>
    [WKTCriticalPatch(WKTPatchCategory.WorldSync), HarmonyPatch(typeof(ENV_BearTrap), nameof(ENV_BearTrap.TryGrabEntity))]
    internal sealed class BearTrapGrab_Patch {
        [UsedImplicitly]
        private static bool Prefix(ENV_BearTrap __instance, Transform t) => WKTBearTraps.TryCatch(__instance, t);
    }

    /// <summary>A trap that was taken over by catching this machine's player gives itself back once it lets go.</summary>
    [WKTCriticalPatch(WKTPatchCategory.WorldSync), HarmonyPatch(typeof(ENV_BearTrap), "LateUpdate")]
    internal sealed class BearTrapRelease_Patch {
        [UsedImplicitly]
        private static void Postfix(ENV_BearTrap __instance) => WKTBearTraps.OnLateUpdate(__instance);
    }
}
