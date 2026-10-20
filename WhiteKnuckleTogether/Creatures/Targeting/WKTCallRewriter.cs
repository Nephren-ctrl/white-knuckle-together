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
