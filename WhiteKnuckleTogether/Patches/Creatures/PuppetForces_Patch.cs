using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using JetBrains.Annotations;
using UnityEngine;

namespace WhiteKnuckleTogether {
    /// <summary>A push on a puppet creature goes to the machine simulating it (see <see cref="WKTEntityForces"/>).</summary>
    [WKTCriticalPatch(WKTPatchCategory.WorldSync), HarmonyPatch]
    internal sealed class PuppetForce_Patch {
        public static List<MethodBase> GetTargets() => WorldSyncTargets.Find(new[] { nameof(GameEntity.AddForce) }, new[] { typeof(Vector3), typeof(string) });

        [UsedImplicitly]
        private static IEnumerable<MethodBase> TargetMethods() => GetTargets();

        [UsedImplicitly]
        private static bool Prefix(GameEntity __instance, Vector3 __0) =>
            !WKTSharedEffectScope.SkipsEntities(WKTWorldSync.Instance?.IsHost ?? true) && WKTEntityForces.BeforeForce(__instance, __0, false, Vector3.zero);
    }

    /// <summary>The same for a push at a point.</summary>
    [WKTCriticalPatch(WKTPatchCategory.WorldSync), HarmonyPatch]
    internal sealed class PuppetForceAtPosition_Patch {
        public static List<MethodBase> GetTargets() =>
            WorldSyncTargets.Find(new[] { nameof(GameEntity.AddForceAtPosition) }, new[] { typeof(Vector3), typeof(Vector3), typeof(string) });

        [UsedImplicitly]
        private static IEnumerable<MethodBase> TargetMethods() => GetTargets();

        [UsedImplicitly]
        private static bool Prefix(GameEntity __instance, Vector3 __0, Vector3 __1) =>
            !WKTSharedEffectScope.SkipsEntities(WKTWorldSync.Instance?.IsHost ?? true) && WKTEntityForces.BeforeForce(__instance, __0, true, __1);
    }
}
