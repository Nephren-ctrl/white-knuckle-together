using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// Rewrites references to the local player in game methods, for transpilers. Pure instruction rewriting, so it can be
    /// tested on hand-built instruction lists.
    /// </summary>
    internal static class WKTCallRewriter {
        public static readonly MethodInfo GetPlayer = AccessTools.Method(typeof(ENT_Player), nameof(ENT_Player.GetPlayer));
        public static readonly FieldInfo PlayerObject = AccessTools.Field(typeof(ENT_Player), nameof(ENT_Player.playerObject));
        public static readonly MethodInfo GetTransform = AccessTools.PropertyGetter(typeof(Component), nameof(Component.transform));
        public static readonly MethodInfo GetPosition = AccessTools.PropertyGetter(typeof(Transform), nameof(Transform.position));

        /// <summary>Whether <paramref name="instruction"/> loads the local player: <c>ENT_Player.GetPlayer()</c> or <c>ENT_Player.playerObject</c>.</summary>
        public static bool LoadsLocalPlayer(CodeInstruction instruction) =>
            instruction.Calls(GetPlayer) || instruction.LoadsField(PlayerObject);

        /// <summary>
        /// Replaces every <c>ENT_Player.GetPlayer().transform.position</c> (or the same through <c>playerObject</c>) with
        /// <c>replacement(this)</c>, where <paramref name="replacement"/> is a static method taking the method's own
        /// component and returning a <see cref="Vector3"/>. Only valid in instance methods of components.
        /// </summary>
        public static List<CodeInstruction> RewritePlayerPosition(IEnumerable<CodeInstruction> instructions, MethodInfo replacement, out int count) {
            var list = new List<CodeInstruction>(instructions);
            count = 0;
            for (var i = 0; i + 2 < list.Count; i++) {
                if (!LoadsLocalPlayer(list[i]) || !list[i + 1].Calls(GetTransform) || !list[i + 2].Calls(GetPosition)) continue;
                var first = list[i];
                var loadSelf = new CodeInstruction(OpCodes.Ldarg_0);
                first.MoveLabelsTo(loadSelf);
                first.MoveBlocksTo(loadSelf);
                list[i] = loadSelf;
                list[i + 1] = new CodeInstruction(OpCodes.Call, replacement);
                list.RemoveAt(i + 2);
                count++;
            }
            return list;
        }

        /// <summary>
        /// Replaces every <c>player.transform</c> read from a local that <c>ENT_Player.GetPlayer()</c> (or
        /// <c>playerObject</c>) was stored in with <c>replacement(this)</c>, where <paramref name="replacement"/> is a static
        /// method taking the method's own component and returning a <see cref="Transform"/>. Only valid in instance methods
        /// of components.
        /// </summary>
        public static List<CodeInstruction> RewriteStoredPlayerTransform(IEnumerable<CodeInstruction> instructions, MethodInfo replacement, out int count) {
            var list = new List<CodeInstruction>(instructions);
            count = 0;
            var player = -1;
            for (var i = 0; i + 1 < list.Count; i++) {
                if (LoadsLocalPlayer(list[i]) && TryGetLocal(list[i + 1], true, out var stored)) {
                    player = stored;
                    continue;
                }
                if (player < 0 || !TryGetLocal(list[i], false, out var loaded) || loaded != player || !list[i + 1].Calls(GetTransform)) continue;
                var loadSelf = new CodeInstruction(OpCodes.Ldarg_0);
                list[i].MoveLabelsTo(loadSelf);
                list[i].MoveBlocksTo(loadSelf);
                list[i] = loadSelf;
                list[i + 1] = new CodeInstruction(OpCodes.Call, replacement);
                count++;
            }
            return list;
        }

        /// <summary>The local variable a <c>stloc</c> (<paramref name="store"/>) or <c>ldloc</c> instruction names.</summary>
        private static bool TryGetLocal(CodeInstruction instruction, bool store, out int index) {
            var opcode = instruction.opcode;
            index = -1;
            if (opcode == (store ? OpCodes.Stloc_0 : OpCodes.Ldloc_0)) index = 0;
            else if (opcode == (store ? OpCodes.Stloc_1 : OpCodes.Ldloc_1)) index = 1;
            else if (opcode == (store ? OpCodes.Stloc_2 : OpCodes.Ldloc_2)) index = 2;
            else if (opcode == (store ? OpCodes.Stloc_3 : OpCodes.Ldloc_3)) index = 3;
            else if (opcode == (store ? OpCodes.Stloc_S : OpCodes.Ldloc_S) || opcode == (store ? OpCodes.Stloc : OpCodes.Ldloc)) {
                index = instruction.operand switch {
                    LocalVariableInfo local => local.LocalIndex,
                    byte value => value,
                    short value => value,
                    ushort value => value,
                    int value => value,
                    _ => -1
                };
            }
            return index >= 0;
        }

        /// <summary>
        /// Replaces every call to <paramref name="original"/> with a call to <paramref name="replacement"/>, a static method
        /// taking the same arguments, with the receiver first for an instance method.
        /// </summary>
        public static List<CodeInstruction> RedirectCalls(IEnumerable<CodeInstruction> instructions, MethodInfo original, MethodInfo replacement, out int count) {
            var list = new List<CodeInstruction>(instructions);
            count = 0;
            for (var i = 0; i < list.Count; i++) {
                if (!list[i].Calls(original)) continue;
                list[i].opcode = OpCodes.Call;
                list[i].operand = replacement;
                count++;
            }
            return list;
        }
    }
}
