using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using JetBrains.Annotations;
using UnityEngine;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// <c>UT_Wiggle</c> bobs things from the time alone, such as the abyss's floating rope bags. It reads the shared clock
    /// (<see cref="WKTSharedClock"/>) instead, so they bob in step on every machine.
    /// </summary>
    [WKTCriticalPatch(WKTPatchCategory.WorldSync), HarmonyPatch(typeof(UT_Wiggle), "Update")]
    internal sealed class WiggleSharedClock_Patch {
        public static readonly MethodInfo GetTime = AccessTools.PropertyGetter(typeof(Time), nameof(Time.time));
        public static readonly MethodInfo GetUnscaledTime = AccessTools.PropertyGetter(typeof(Time), nameof(Time.unscaledTime));

        [UsedImplicitly]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) {
            var list = WKTCallRewriter.RedirectCalls(instructions, GetTime, AccessTools.Method(typeof(WKTSharedClock), nameof(WKTSharedClock.GetTime)), out _);
            return WKTCallRewriter.RedirectCalls(list, GetUnscaledTime, AccessTools.Method(typeof(WKTSharedClock), nameof(WKTSharedClock.GetUnscaledTime)), out _);
        }
    }
}
