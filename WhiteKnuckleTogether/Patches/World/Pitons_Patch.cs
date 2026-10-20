using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using JetBrains.Annotations;
using UnityEngine;
using Object = UnityEngine.Object;

namespace WhiteKnuckleTogether {
    /// <summary>A piton the local player places is shared with everyone.</summary>
    [WKTCriticalPatch(WKTPatchCategory.WorldSync), HarmonyPatch(typeof(HandItem_Piton), nameof(HandItem_Piton.PitonHit))]
    internal sealed class PitonPlace_Patch {
        private static readonly MethodInfo s_hook = AccessTools.Method(typeof(PitonPlace_Patch), nameof(Instantiate));
        private static GameObject? s_placed;

        [UsedImplicitly]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) {
            var result = WKTCallRewriter.RedirectCalls(instructions, LocalPrefabSpawn_Patch.InstantiateAtPose, s_hook, out var count);
            if (count == 0)
                throw new InvalidOperationException("Found no piton spawn to redirect in HandItem_Piton.PitonHit.");
            return result;
        }

        /// <summary>Spawns like <c>Object.Instantiate</c>, remembering a piton (the object with a handhold) for the postfix.</summary>
        public static GameObject Instantiate(GameObject original, Vector3 position, Quaternion rotation) {
            var spawned = Object.Instantiate(original, position, rotation);
            if (spawned.TryGetComponent<CL_Handhold>(out _))
                s_placed = spawned;
            return spawned;
        }

        [UsedImplicitly]
        private static Exception? Finalizer(Exception? __exception, HandItem_Piton __instance) {
            var placed = s_placed;
            s_placed = null;
            if (__exception == null && placed != null)
                WKTWorldSync.Instance?.Pitons.OnLocalPlaced(__instance, placed);
            return __exception;
        }
    }

    /// <summary>A hammer hit on a shared piton plays out for everyone.</summary>
    [WKTCriticalPatch(WKTPatchCategory.WorldSync), HarmonyPatch(typeof(CL_Handhold), nameof(CL_Handhold.HammerIn))]
    internal sealed class PitonHammer_Patch {
        [UsedImplicitly]
        private static void Postfix(CL_Handhold __instance, float amount) => WKTWorldSync.Instance?.Pitons.OnLocalHammer(__instance, amount);
    }

    /// <summary>A shared piton the local player takes back out of the wall is gone for everyone.</summary>
    [WKTCriticalPatch(WKTPatchCategory.WorldSync), HarmonyPatch(typeof(HandholdModule_GrantItemOnRelease), nameof(HandholdModule_GrantItemOnRelease.GiveItem))]
    internal sealed class PitonTake_Patch {
        private static readonly AccessTools.FieldRef<HandholdModule, CL_Handhold> s_handhold = AccessTools.FieldRefAccess<HandholdModule, CL_Handhold>("handhold");

        [UsedImplicitly]
        private static void Prefix(HandholdModule_GrantItemOnRelease __instance) {
            if (WKTWorldSync.Instance == null || !__instance.destroyOnRelease) return;
            var handhold = s_handhold(__instance);
            if (handhold != null && handhold.TryGetComponent<WKTPlacedPiton>(out var marker))
                marker.Taken = true;
        }
    }
}
