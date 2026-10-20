using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using JetBrains.Annotations;
using UnityEngine;
using UnityEngine.Audio;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// Every sound a handhold or one of its modules plays at a point (an icy hold slipping, a breakable shaking, grab and
    /// release clips, damage and poison) goes through <see cref="WKTHandholdSync.PlaySound"/>, which also tells everyone else
    /// when the local player's hold made it. Not critical: without it, only other players' handhold sounds are missing.
    /// </summary>
    [HarmonyPatch]
    internal sealed class HandholdSounds_Patch {
        public static readonly MethodInfo PlaySoundAtPoint = AccessTools.Method(typeof(AudioManager), nameof(AudioManager.PlaySound),
            new[] { typeof(AudioClip), typeof(Vector3), typeof(float), typeof(float), typeof(float), typeof(bool), typeof(float), typeof(AudioMixerGroup), typeof(string) });
        private static readonly MethodInfo s_hook = AccessTools.Method(typeof(WKTHandholdSync), nameof(WKTHandholdSync.PlaySound));

        /// <summary>The instance methods of handholds and handhold modules that play a sound at a point.</summary>
        public static IEnumerable<MethodBase> GetTargets() {
            var callees = new List<MethodBase>();
            foreach (var type in typeof(CL_Handhold).Assembly.GetTypes()) {
                if (!typeof(CL_Handhold).IsAssignableFrom(type) && !typeof(HandholdModule).IsAssignableFrom(type)) continue;
                foreach (var method in type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)) {
                    if (method.IsAbstract || method.ContainsGenericParameters) continue;
                    callees.Clear();
                    if (WKTILCallReader.TryGetCallees(method, callees) && callees.Contains(PlaySoundAtPoint))
                        yield return method;
                }
            }
        }

        [UsedImplicitly]
        private static IEnumerable<MethodBase> TargetMethods() => GetTargets();

        [UsedImplicitly]
        private static void Prefix(object __instance) => WKTHandholdSync.Enter(__instance);

        /// <summary>Each sound call gets the handhold or module itself as one more argument.</summary>
        [UsedImplicitly]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod) {
            var list = new List<CodeInstruction>(instructions);
            var count = 0;
            for (var i = 0; i < list.Count; i++) {
                if (!list[i].Calls(PlaySoundAtPoint)) continue;
                var loadSelf = new CodeInstruction(OpCodes.Ldarg_0);
                list[i].MoveLabelsTo(loadSelf);
                list[i].MoveBlocksTo(loadSelf);
                list[i].opcode = OpCodes.Call;
                list[i].operand = s_hook;
                list.Insert(i, loadSelf);
                i++;
                count++;
            }
            if (count == 0)
                throw new InvalidOperationException($"Found no handhold sound to redirect in {__originalMethod.DeclaringType?.Name}.{__originalMethod.Name}.");
            return list;
        }

        [UsedImplicitly]
        private static Exception? Finalizer(Exception? __exception) {
            WKTHandholdSync.Exit();
            return __exception;
        }
    }

    /// <summary>
    /// The local player taking hold of or letting go of a handhold makes its grab and release sounds theirs, though their
    /// hand is only added after the sounds of the first grab start and is gone before the release sounds play.
    /// </summary>
    [HarmonyPatch(typeof(CL_Handhold), nameof(CL_Handhold.Interact))]
    internal sealed class HandholdLocalGrab_Patch {
        [UsedImplicitly]
        private static void Prefix(Clickable.InteractionInfo? info, out bool __state) => __state = HandholdLocalRelease_Patch.Enter(info?.player);

        [UsedImplicitly]
        private static Exception? Finalizer(Exception? __exception, bool __state) => HandholdLocalRelease_Patch.Exit(__exception, __state);
    }

    [HarmonyPatch(typeof(CL_Handhold), nameof(CL_Handhold.StopInteract))]
    internal sealed class HandholdLocalRelease_Patch {
        [UsedImplicitly]
        private static void Prefix(ENT_Player? p, out bool __state) => __state = Enter(p);

        [UsedImplicitly]
        private static Exception? Finalizer(Exception? __exception, bool __state) => Exit(__exception, __state);

        public static bool Enter(ENT_Player? player) {
            if (player == null || player != ENT_Player.playerObject || WKTWorldSync.Instance == null) return false;
            WKTHandholdSync.EnterLocalHold();
            return true;
        }

        public static Exception? Exit(Exception? exception, bool entered) {
            if (entered)
                WKTHandholdSync.Exit();
            return exception;
        }
    }

    /// <summary>With timed handholds shared, a breakable handhold breaking breaks for everyone, once.</summary>
    [WKTCriticalPatch(WKTPatchCategory.WorldSync), HarmonyPatch(typeof(CL_Handhold_Breakable), nameof(CL_Handhold_Breakable.Break))]
    internal sealed class HandholdBreak_Patch {
        [UsedImplicitly]
        private static bool Prefix(CL_Handhold_Breakable __instance, out bool __state) {
            __state = false;
            var sync = WKTWorldSync.Instance;
            return sync == null || sync.Handholds.BeforeBreak(__instance, out __state);
        }

        [UsedImplicitly]
        private static Exception? Finalizer(Exception? __exception, bool __state) {
            WKTHandholdSync.AfterBreak(__state);
            return __exception;
        }
    }

    /// <summary>With timed handholds shared, a handhold's timer running out runs its events for everyone.</summary>
    [WKTCriticalPatch(WKTPatchCategory.WorldSync), HarmonyPatch(typeof(HandholdModule_Timer), nameof(HandholdModule_Timer.Update))]
    internal sealed class HandholdTimer_Patch {
        [UsedImplicitly]
        private static void Prefix(HandholdModule_Timer __instance, out bool __state) =>
            __state = WKTWorldSync.Instance != null && WKTHandholdSync.IsTimerRunning(__instance);

        [UsedImplicitly]
        private static void Postfix(HandholdModule_Timer __instance, bool __state) {
            if (__state && !WKTHandholdSync.IsTimerRunning(__instance))
                WKTWorldSync.Instance?.Handholds.OnLocalTimerFinished(__instance);
        }
    }
}
