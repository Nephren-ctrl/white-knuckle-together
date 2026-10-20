using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using JetBrains.Annotations;

namespace WhiteKnuckleTogether {
    /// <summary>The host's Vent Thing goes for, grabs and lets go of any player (see <see cref="WKTVentThingGrabs"/>).</summary>
    [WKTCriticalPatch(WKTPatchCategory.WorldSync), HarmonyPatch(typeof(DEN_VentThing), "LateUpdate")]
    internal sealed class VentThingTarget_Patch {
        [UsedImplicitly]
        private static void Prefix(DEN_VentThing __instance) {
            if (!WKTWorldSync.IsPuppet(__instance))
                WKTVentThingGrabs.Retarget(__instance);
        }

        [UsedImplicitly]
        private static void Postfix(DEN_VentThing __instance) {
            if (!WKTWorldSync.IsPuppet(__instance))
                WKTVentThingGrabs.ShowHandForOthers(__instance);
        }
    }

    /// <summary>
    /// In <c>PullTarget</c> and <c>DropTarget</c>, <c>targetEntity == ENT_Player.playerObject</c> also holds for the engaged
    /// player's stand-in, and the grab's vignettes only change the held player's own screen.
    /// </summary>
    [WKTCriticalPatch(WKTPatchCategory.WorldSync), HarmonyPatch]
    internal sealed class VentThingGrab_Patch {
        private static readonly FieldInfo s_targetEntity = AccessTools.Field(typeof(DEN_VentThing), "targetEntity");
        private static readonly MethodInfo s_equality = AccessTools.Method(typeof(UnityEngine.Object), "op_Equality");
        private static readonly MethodInfo s_setVignetteTarget = AccessTools.Method(typeof(CL_UIManager), nameof(CL_UIManager.SetVignetteTarget));
        private static readonly MethodInfo s_isEngagedPlayer = AccessTools.Method(typeof(WKTVentThingGrabs), nameof(WKTVentThingGrabs.IsEngagedPlayer));
        private static readonly MethodInfo s_grabVignette = AccessTools.Method(typeof(WKTVentThingGrabs), nameof(WKTVentThingGrabs.SetVignetteTarget));

        [UsedImplicitly]
        private static IEnumerable<MethodBase> TargetMethods() {
            yield return AccessTools.Method(typeof(DEN_VentThing), nameof(DEN_VentThing.PullTarget));
            yield return AccessTools.Method(typeof(DEN_VentThing), nameof(DEN_VentThing.DropTarget));
        }

        [UsedImplicitly]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod) {
            var list = new List<CodeInstruction>(instructions);
            var compares = 0;
            for (var i = 0; i < list.Count; i++) {
                if (i + 2 < list.Count && list[i].LoadsField(s_targetEntity) && list[i + 1].LoadsField(WKTCallRewriter.PlayerObject) && list[i + 2].Calls(s_equality)) {
                    // targetEntity, self, then the check.
                    list[i + 1] = new CodeInstruction(OpCodes.Ldarg_0).MoveLabelsFrom(list[i + 1]);
                    list[i + 2] = new CodeInstruction(OpCodes.Call, s_isEngagedPlayer).MoveLabelsFrom(list[i + 2]);
                    compares++;
                    continue;
                }
                if (!list[i].Calls(s_setVignetteTarget)) continue;
                list.Insert(i, new CodeInstruction(OpCodes.Ldarg_0));
                list[i + 1] = new CodeInstruction(OpCodes.Call, s_grabVignette).MoveLabelsFrom(list[i + 1]);
                i++;
            }
            if (compares == 0)
                throw new System.InvalidOperationException($"Found no player check to widen in DEN_VentThing.{__originalMethod.Name}.");
            return list;
        }
    }
}
