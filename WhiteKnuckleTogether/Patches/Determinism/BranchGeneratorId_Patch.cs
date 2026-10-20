using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using JetBrains.Annotations;
using UnityEngine;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// A branch a generator builds (a shortcut, the ladder) is named the same on every machine of a multiplayer run, so it
    /// generates and is keyed the same (see <see cref="WKTBranchIds"/>). Singleplayer keeps the frame count.
    /// </summary>
    [WKTCriticalPatch(WKTPatchCategory.RunSync), HarmonyPatch(typeof(UT_BranchGenerator), nameof(UT_BranchGenerator.Generate))]
    internal sealed class BranchGeneratorId_Patch {
        private static readonly MethodInfo s_frameCount = AccessTools.PropertyGetter(typeof(Time), nameof(Time.frameCount));
        private static readonly MethodInfo s_getSuffix = AccessTools.Method(typeof(WKTBranchIds), nameof(WKTBranchIds.GetSuffix), new[] { typeof(UT_BranchGenerator) });

        [UsedImplicitly]
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) {
            var list = new List<CodeInstruction>(instructions);
            for (var i = 0; i < list.Count; i++) {
                if (!list[i].Calls(s_frameCount)) continue;
                // selectedBranch.id += Time.frameCount  ->  selectedBranch.id += WKTBranchIds.GetSuffix(this)
                var load = new CodeInstruction(OpCodes.Ldarg_0).MoveLabelsFrom(list[i]);
                list[i] = new CodeInstruction(OpCodes.Call, s_getSuffix);
                list.Insert(i, load);
                return list;
            }
            throw new InvalidOperationException("Found no frame count to replace in UT_BranchGenerator.Generate.");
        }
    }
}
