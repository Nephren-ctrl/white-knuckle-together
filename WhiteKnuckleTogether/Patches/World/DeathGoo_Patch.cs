using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using JetBrains.Annotations;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// The goo speeds up with its distance to the player. Where that distance sets its speed (the rubberband factor and the
    /// boost far below), it is measured to the players picked in the lobby instead (<see cref="WKTGooRubberband"/>). Sounds,
    /// screen shake and the kill check still use the local player's distance.
    /// </summary>
    [WKTCriticalPatch(WKTPatchCategory.WorldSync), HarmonyPatch(typeof(DEN_DeathFloor), nameof(DEN_DeathFloor.Update))]
    internal sealed class DeathGooRubberband_Patch {
        /// <summary>The constants the distance is used with where it sets the speed: the rubberband factor and the boost threshold.</summary>
        public static readonly float[] SpeedConstants = { 0.07f, 300f };
        public static readonly FieldInfo PlayerDistance = AccessTools.Field(typeof(DEN_DeathFloor), "playerDistance");
        public static readonly MethodInfo Replacement = AccessTools.Method(typeof(WKTGooRubberband), nameof(WKTGooRubberband.Distance),
            new[] { typeof(float), typeof(DEN_DeathFloor) });

        /// <summary>
        /// After each read of <c>playerDistance</c> that is directly used with one of <see cref="SpeedConstants"/>, passes it
        /// through <see cref="Replacement"/> with the goo itself. Returns the number of reads rewritten in <paramref name="count"/>.
        /// </summary>
        public static List<CodeInstruction> Rewrite(IEnumerable<CodeInstruction> instructions, out int count) {
            var list = new List<CodeInstruction>(instructions);
            count = 0;
            for (var i = 0; i + 1 < list.Count; i++) {
                if (!list[i].LoadsField(PlayerDistance) || !IsSpeedConstant(list[i + 1])) continue;
                list.Insert(i + 1, new CodeInstruction(OpCodes.Ldarg_0));
                list.Insert(i + 2, new CodeInstruction(OpCodes.Call, Replacement));
                i += 2;
                count++;
            }
            return list;
        }

        private static bool IsSpeedConstant(CodeInstruction instruction) =>
            instruction.opcode == OpCodes.Ldc_R4 && instruction.operand is float value && Array.IndexOf(SpeedConstants, value) >= 0;

        [UsedImplicitly]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) {
            var list = Rewrite(instructions, out var count);
            return count == SpeedConstants.Length
                ? list
                : throw new InvalidOperationException($"Expected {SpeedConstants.Length} goo speed distance reads, found {count}. The game changed.");
        }
    }

    /// <summary>
    /// The goo starts rising, and starts speeding up, once the players picked in the lobby have climbed far enough, not the
    /// host alone (see <see cref="WKTGooRubberband"/>).
    /// </summary>
    [WKTCriticalPatch(WKTPatchCategory.WorldSync), HarmonyPatch(typeof(DEN_DeathFloor), nameof(DEN_DeathFloor.Update))]
    internal sealed class DeathGooTravel_Patch {
        /// <summary>The goo checks the travel distance against its start height and its ease height.</summary>
        public const int EXPECTED_READS = 2;
        public static readonly MethodInfo TravelDistance = AccessTools.Method(typeof(CL_GameManager), nameof(CL_GameManager.GetPlayerTravelDistance));
        public static readonly MethodInfo Replacement = AccessTools.Method(typeof(WKTGooRubberband), nameof(WKTGooRubberband.TravelDistance),
            new[] { typeof(float), typeof(DEN_DeathFloor) });

        /// <summary>After each call to <see cref="TravelDistance"/>, passes its result through <see cref="Replacement"/> with the goo.</summary>
        public static List<CodeInstruction> Rewrite(IEnumerable<CodeInstruction> instructions, out int count) {
            var list = new List<CodeInstruction>(instructions);
            count = 0;
            for (var i = 0; i < list.Count; i++) {
                if (!list[i].Calls(TravelDistance)) continue;
                list.Insert(i + 1, new CodeInstruction(OpCodes.Ldarg_0));
                list.Insert(i + 2, new CodeInstruction(OpCodes.Call, Replacement));
                i += 2;
                count++;
            }
            return list;
        }

        [UsedImplicitly]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) {
            var list = Rewrite(instructions, out var count);
            return count == EXPECTED_READS
                ? list
                : throw new InvalidOperationException($"Expected {EXPECTED_READS} goo travel distance reads, found {count}. The game changed.");
        }
    }

    /// <summary>
    /// A client's goo sits where the host's is (<see cref="WKTGooSync"/>): placed there before its tick, so the game's own
    /// kill check, sounds and corruption height use the shared height, and again after it and after coroutines, undoing
    /// its own movement.
    /// </summary>
    [WKTCriticalPatch(WKTPatchCategory.WorldSync), HarmonyPatch]
    internal sealed class DeathGooClient_Patch {
        public static IEnumerable<MethodBase> GetTargets() {
            yield return AccessTools.Method(typeof(DEN_DeathFloor), nameof(DEN_DeathFloor.Update));
            yield return AccessTools.Method(typeof(DEN_DeathFloor), "LateUpdate");
        }

        [UsedImplicitly]
        private static IEnumerable<MethodBase> TargetMethods() => GetTargets();

        [UsedImplicitly]
        private static void Prefix(DEN_DeathFloor __instance) => WKTGooSync.ApplyOnClient(__instance);

        [UsedImplicitly]
        private static void Postfix(DEN_DeathFloor __instance) => WKTGooSync.ApplyOnClient(__instance);
    }
}
