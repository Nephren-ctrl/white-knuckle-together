using System.Collections.Generic;
using System.Reflection;
using DarkMachine.AI;
using DarkMachine.Collections;
using HarmonyLib;
using JetBrains.Annotations;
using UnityEngine;

namespace WhiteKnuckleTogether {
    /// <summary>The host's Remains freezes while any living player sees it (see <see cref="WKTViewerCreatures.IsSeenByAnyone"/>).</summary>
    [WKTCriticalPatch(WKTPatchCategory.WorldSync), HarmonyPatch(typeof(DEN_Remains_Active), nameof(DEN_Remains_Active.Update))]
    internal sealed class RemainsSeen_Patch {
        public static readonly MethodInfo IsVisible = AccessTools.PropertyGetter(typeof(Renderer), nameof(Renderer.isVisible));

        [UsedImplicitly]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
            GasbagExplodeScan_Patch.Require(WKTCallRewriter.RedirectCalls(instructions, IsVisible,
                AccessTools.Method(typeof(WKTViewerCreatures), nameof(WKTViewerCreatures.IsSeenByAnyone)), out var count), count, "DEN_Remains_Active.Update");
    }

    /// <summary>A Remains the host holds behind a remote player's head for their neck snap does nothing else meanwhile.</summary>
    [WKTCriticalPatch(WKTPatchCategory.WorldSync), HarmonyPatch]
    internal sealed class RemainsHeld_Patch {
        public static IEnumerable<MethodBase> GetTargets() {
            yield return AccessTools.Method(typeof(DEN_Remains_Active), nameof(DEN_Remains_Active.Update));
            yield return AccessTools.Method(typeof(DEN_Remains_Active), nameof(DEN_Remains_Active.TickUpdate));
        }

        [UsedImplicitly]
        private static IEnumerable<MethodBase> TargetMethods() => GetTargets();

        [UsedImplicitly]
        private static bool Prefix(DEN_Remains_Active __instance) => !WKTViewerCreatures.IsHeldForKill(__instance);
    }

    /// <summary>
    /// The Hunter's and Face's overlays follow each player's own distance from them, not the AI's target's
    /// (see <see cref="WKTViewerCreatures"/>).
    /// </summary>
    [WKTCriticalPatch(WKTPatchCategory.WorldSync), HarmonyPatch]
    internal sealed class OverlayFromAI_Patch {
        public static readonly MethodInfo HunterSetScreenEffect = AccessTools.Method(typeof(DEN_Hunter), "SetScreenEffect");
        public static readonly MethodInfo FaceSetScreenEffect = AccessTools.Method(typeof(DEN_Face), "SetScreenEffect");

        public static IEnumerable<MethodBase> GetTargets() {
            yield return AccessTools.Method(typeof(DEN_Hunter), "Movement");
            yield return AccessTools.Method(typeof(DEN_Face), "TargetCollisionCheck");
        }

        [UsedImplicitly]
        private static IEnumerable<MethodBase> TargetMethods() => GetTargets();

        [UsedImplicitly]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod) {
            var hunter = __originalMethod.DeclaringType == typeof(DEN_Hunter);
            var replacement = AccessTools.Method(typeof(WKTViewerCreatures),
                hunter ? nameof(WKTViewerCreatures.SetHunterScreenEffectFromAI) : nameof(WKTViewerCreatures.SetFaceScreenEffectFromAI));
            var result = WKTCallRewriter.RedirectCalls(instructions, hunter ? HunterSetScreenEffect : FaceSetScreenEffect, replacement, out var count);
            return GasbagExplodeScan_Patch.Require(result, count, $"{__originalMethod.DeclaringType?.Name}.{__originalMethod.Name}");
        }
    }

    /// <summary>A Tracker's unease builds on each player near it, on their own machine (see <see cref="WKTViewerCreatures"/>).</summary>
    [WKTCriticalPatch(WKTPatchCategory.WorldSync), HarmonyPatch(typeof(DEN_Tracker), nameof(DEN_Tracker.Update))]
    internal sealed class TrackerUnease_Patch {
        public static readonly MethodInfo Enqueue = AccessTools.Method(typeof(PriorityQueue<FXManager.FXZoneStore, byte>), "Enqueue");

        [UsedImplicitly]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
            GasbagExplodeScan_Patch.Require(WKTCallRewriter.RedirectCalls(instructions, Enqueue,
                AccessTools.Method(typeof(WKTViewerCreatures), nameof(WKTViewerCreatures.EnqueueTrackerEffectFromAI)), out var count), count, "DEN_Tracker.Update");
    }

    /// <summary>A Teeth fears every player's lights, not just the host's (see <see cref="WKTViewerCreatures.GetFearRating"/>).</summary>
    [WKTCriticalPatch(WKTPatchCategory.WorldSync), HarmonyPatch(typeof(AIC_Teeth_Chase), nameof(AIC_Teeth_Chase.Execute))]
    internal sealed class TeethLightFear_Patch {
        public static readonly MethodInfo GetHighestEffectorFearRating = AccessTools.Method(typeof(CL_Lamp), nameof(CL_Lamp.GetHighestEffectorFearRating));

        [UsedImplicitly]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
            GasbagExplodeScan_Patch.Require(WKTCallRewriter.RedirectCalls(instructions, GetHighestEffectorFearRating,
                AccessTools.Method(typeof(WKTViewerCreatures), nameof(WKTViewerCreatures.GetFearRating)), out var count), count, "AIC_Teeth_Chase.Execute");
    }

    /// <summary>A Teeth's bite on a remote player reaches their machine, which decides whether it kills (see <see cref="WKTTeethKills.Bite"/>).</summary>
    [WKTCriticalPatch(WKTPatchCategory.WorldSync), HarmonyPatch(typeof(AIC_Teeth_Chase), nameof(AIC_Teeth_Chase.Execute))]
    internal sealed class TeethBite_Patch {
        public static readonly MethodInfo Damage = AccessTools.Method(typeof(GameEntity), nameof(GameEntity.Damage), new[] { typeof(Damageable.DamageInfo) });

        [UsedImplicitly]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
            GasbagExplodeScan_Patch.Require(WKTCallRewriter.RedirectCalls(instructions, Damage, AccessTools.Method(typeof(WKTTeethKills), nameof(WKTTeethKills.Bite)),
                out var count), count, "AIC_Teeth_Chase.Execute (bite)");
    }
}
