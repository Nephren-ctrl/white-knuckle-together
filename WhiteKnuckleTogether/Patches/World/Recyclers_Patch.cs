using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using JetBrains.Annotations;

namespace WhiteKnuckleTogether {
    /// <summary>Only the host's recyclers run (see <see cref="WKTRecyclers"/>).</summary>
    [WKTCriticalPatch(WKTPatchCategory.WorldSync), HarmonyPatch(typeof(ENV_Recycler), "Update")]
    internal sealed class RecyclerUpdate_Patch {
        [UsedImplicitly]
        private static bool Prefix(ENV_Recycler __instance) => WKTWorldSync.Instance?.Recyclers.BeforeUpdate(__instance) ?? true;

        [UsedImplicitly]
        private static void Finalizer() => WKTWorldSync.Instance?.Recyclers.AfterUpdate();
    }

    /// <summary>
    /// The item a recycler takes is noted, so its payout reaches whoever dropped it. An item a client's amalgamator uses up
    /// is removed by the host.
    /// </summary>
    [WKTCriticalPatch(WKTPatchCategory.WorldSync), HarmonyPatch(typeof(Item), nameof(Item.Destroy), typeof(bool))]
    internal sealed class RecycledItem_Patch {
        [UsedImplicitly]
        private static void Prefix(Item __instance) {
            var sync = WKTWorldSync.Instance;
            if (sync == null) return;
            sync.Recyclers.OnItemDestroyed(__instance);
            if (AmalgamatorCraft_Patch.IsCrafting)
                sync.OnLocalConsume(__instance);
        }
    }

    /// <summary>
    /// A recycler's delayed payout (its <c>WaitToAddRoaches</c> coroutine) goes to the player whose item it took instead of
    /// always the local player.
    /// </summary>
    [WKTCriticalPatch(WKTPatchCategory.WorldSync), HarmonyPatch]
    internal sealed class RecyclerPayout_Patch {
        private static readonly MethodInfo s_addRoaches = AccessTools.Method(typeof(CL_GameManager), nameof(CL_GameManager.AddRoaches), new[] { typeof(int), typeof(bool) });
        private static readonly MethodInfo s_pay = AccessTools.Method(typeof(RecyclerPayout_Patch), nameof(Pay));

        public static MethodBase GetTarget() {
            foreach (var nested in typeof(ENV_Recycler).GetNestedTypes(BindingFlags.NonPublic)) {
                if (!nested.Name.Contains("WaitToAddRoaches")) continue;
                var moveNext = AccessTools.Method(nested, "MoveNext");
                if (moveNext != null) return moveNext;
            }
            throw new InvalidOperationException("ENV_Recycler's WaitToAddRoaches coroutine was not found.");
        }

        [UsedImplicitly]
        private static MethodBase TargetMethod() => GetTarget();

        [UsedImplicitly]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase original) {
            var owner = FindOwnerField(original.DeclaringType!);
            var list = new List<CodeInstruction>(instructions);
            var count = 0;
            for (var i = 0; i < list.Count; i++) {
                if (!list[i].Calls(s_addRoaches)) continue;
                // AddRoaches(amount, global) becomes Pay(amount, global, recycler): the recycler is pushed where the call was.
                var call = list[i];
                var self = new CodeInstruction(OpCodes.Ldarg_0);
                call.MoveLabelsTo(self);
                call.MoveBlocksTo(self);
                list.Insert(i, self);
                list.Insert(i + 1, new CodeInstruction(OpCodes.Ldfld, owner));
                list[i + 2] = new CodeInstruction(OpCodes.Call, s_pay);
                i += 2;
                count++;
            }
            return count > 0 ? list : throw new InvalidOperationException("Found no payout to redirect in ENV_Recycler's WaitToAddRoaches.");
        }

        private static FieldInfo FindOwnerField(Type coroutine) {
            foreach (var field in coroutine.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)) {
                if (field.FieldType == typeof(ENV_Recycler)) return field;
            }
            throw new InvalidOperationException("ENV_Recycler's WaitToAddRoaches coroutine does not hold its recycler.");
        }

        private static void Pay(int amount, bool global, ENV_Recycler recycler) {
            var recyclers = WKTWorldSync.Instance?.Recyclers;
            if (recyclers == null)
                CL_GameManager.AddRoaches(amount, global);
            else
                recyclers.Pay(recycler, amount, global);
        }
    }
}
