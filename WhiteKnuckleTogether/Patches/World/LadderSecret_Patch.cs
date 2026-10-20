using HarmonyLib;
using JetBrains.Annotations;
using UnityEngine;

namespace WhiteKnuckleTogether {
    /// <summary>A local teleport through a ladder vent blocker opens it for everyone (see <see cref="WKTLadderSecret"/>).</summary>
    [WKTCriticalPatch(WKTPatchCategory.WorldSync), HarmonyPatch]
    internal sealed class LadderVentTeleport_Patch {
        [UsedImplicitly]
        private static System.Collections.Generic.IEnumerable<System.Reflection.MethodBase> TargetMethods() {
            yield return AccessTools.Method(typeof(ENT_Player), nameof(ENT_Player.Teleport), new[] { typeof(Vector3) });
            yield return AccessTools.Method(typeof(ENT_Player), nameof(ENT_Player.Teleport), new[] { typeof(Vector3), typeof(Quaternion), typeof(bool) });
        }

        [UsedImplicitly]
        private static void Prefix(ENT_Player __instance, out Vector3 __state) => __state = __instance.transform.position;

        [UsedImplicitly]
        private static void Postfix(ENT_Player __instance, Vector3 __state) {
            if (__instance == ENT_Player.playerObject)
                WKTWorldSync.Instance?.Ladder.OnLocalTeleport(__state, __instance.transform.position);
        }
    }

    /// <summary>The ladder nightmare's attack is shared: at whoever fell, deadly to anyone in its way (see <see cref="WKTLadderSecret"/>).</summary>
    [WKTCriticalPatch(WKTPatchCategory.WorldSync), HarmonyPatch(typeof(DEN_LadderNightmare), "Update")]
    internal sealed class LadderNightmare_Patch {
        [UsedImplicitly]
        private static bool Prefix(DEN_LadderNightmare __instance) => WKTWorldSync.Instance?.Ladder.BeforeNightmareUpdate(__instance) ?? true;
    }
}
