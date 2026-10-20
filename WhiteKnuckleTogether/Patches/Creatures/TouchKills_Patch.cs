using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using JetBrains.Annotations;
using UnityEngine;
using Object = UnityEngine.Object;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// A gasbag pops for a remote player on that player's own screen (see <see cref="WKTTouchKills"/>), so the host's gasbag
    /// does not pop for their proxy. It still sees and chases them: only its explode scan skips proxies.
    /// </summary>
    [WKTCriticalPatch(WKTPatchCategory.WorldSync), HarmonyPatch(typeof(DEN_Gasbag), nameof(DEN_Gasbag.ExplodeScan))]
    internal sealed class GasbagExplodeScan_Patch {
        public static readonly MethodInfo OverlapSphere = AccessTools.Method(typeof(Physics), nameof(Physics.OverlapSphere), new[] { typeof(Vector3), typeof(float) });

        [UsedImplicitly]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
            Require(WKTCallRewriter.RedirectCalls(instructions, OverlapSphere,
                AccessTools.Method(typeof(WKTPlayerProxy), nameof(WKTPlayerProxy.OverlapSphereWithoutProxies)), out var count), count, "DEN_Gasbag.ExplodeScan");

        public static List<CodeInstruction> Require(List<CodeInstruction> result, int count, string where) =>
            count > 0 ? result : throw new InvalidOperationException($"Found nothing to redirect in {where}.");
    }

    /// <summary>
    /// A roach or critter is squished by a remote player on that player's own screen (see <see cref="WKTTouchKills"/>), so the
    /// host's never counts a proxy as close enough to squish it. It still flees them.
    /// </summary>
    [WKTCriticalPatch(WKTPatchCategory.WorldSync), HarmonyPatch]
    internal sealed class SquishReach_Patch {
        public static readonly MethodInfo ClosestPoint = AccessTools.Method(typeof(Collider), nameof(Collider.ClosestPoint), new[] { typeof(Vector3) });

        public static IEnumerable<MethodBase> GetTargets() {
            yield return AccessTools.Method(typeof(DEN_Roach), "AI");
            yield return AccessTools.Method(typeof(DEN_Critter), "AI");
        }

        [UsedImplicitly]
        private static IEnumerable<MethodBase> TargetMethods() => GetTargets();

        [UsedImplicitly]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod) =>
            GasbagExplodeScan_Patch.Require(WKTCallRewriter.RedirectCalls(instructions, ClosestPoint,
                AccessTools.Method(typeof(WKTPlayerProxy), nameof(WKTPlayerProxy.ClosestPointUnlessProxy)), out var count), count,
                $"{__originalMethod.DeclaringType?.Name}.{__originalMethod.Name}");
    }

    /// <summary>A squished critter's removal shows its death effect to everyone (see <see cref="WKTTouchKills.DestroySquished"/>).</summary>
    [WKTCriticalPatch(WKTPatchCategory.WorldSync), HarmonyPatch(typeof(DEN_Critter), "AI")]
    internal sealed class CritterSquish_Patch {
        public static readonly MethodInfo Destroy = AccessTools.Method(typeof(Object), nameof(Object.Destroy), new[] { typeof(Object) });

        [UsedImplicitly]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
            GasbagExplodeScan_Patch.Require(WKTCallRewriter.RedirectCalls(instructions, Destroy,
                AccessTools.Method(typeof(WKTTouchKills), nameof(WKTTouchKills.DestroySquished)), out var count), count, "DEN_Critter.AI");
    }
}
