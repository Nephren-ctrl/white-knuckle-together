using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using JetBrains.Annotations;

namespace WhiteKnuckleTogether {
    /// <summary>A swimmer missing its water looks it up again before it runs (see <see cref="WKTSwimmers"/>).</summary>
    [WKTCriticalPatch(WKTPatchCategory.WorldSync), HarmonyPatch(typeof(DEN_SimpleSwimmer), nameof(DEN_SimpleSwimmer.Update))]
    internal sealed class SwimmerZone_Patch {
        [UsedImplicitly]
        private static void Prefix(DEN_SimpleSwimmer __instance) {
            if (WKTWorldSync.Instance != null)
                WKTSwimmers.EnsureZone(__instance);
        }
    }

    /// <summary>
    /// A swimmer finding no water when it starts kills itself (<c>DEN_SimpleSwimmer.Start</c>). A client's copy may start before
    /// its water has, so only the host's does: its death reaches everyone.
    /// </summary>
    [WKTCriticalPatch(WKTPatchCategory.WorldSync), HarmonyPatch(typeof(DEN_SimpleSwimmer), nameof(DEN_SimpleSwimmer.Start))]
    internal sealed class SwimmerStartKill_Patch {
        private static readonly MethodInfo s_killIfAllowed = AccessTools.Method(typeof(SwimmerStartKill_Patch), nameof(KillIfAllowed));

        [UsedImplicitly]
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) {
            var list = new List<CodeInstruction>(instructions);
            var count = 0;
            foreach (var instruction in list) {
                // this.Kill("", null), whichever declaration the call names.
                if (instruction.operand is not MethodInfo { Name: nameof(GameEntity.Kill) } method || method.IsStatic || method.GetParameters().Length != 2) continue;
                instruction.opcode = OpCodes.Call;
                instruction.operand = s_killIfAllowed;
                count++;
            }
            return count > 0 ? list : throw new InvalidOperationException("Found no kill to guard in DEN_SimpleSwimmer.Start.");
        }

        private static void KillIfAllowed(GameEntity swimmer, string type, Damageable.DamageInfo info) {
            if (WKTSwimmers.MayDieForNoWater())
                swimmer.Kill(type, info);
        }
    }
}
