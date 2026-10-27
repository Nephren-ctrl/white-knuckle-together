using HarmonyLib;
using JetBrains.Annotations;
using UnityEngine;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// A bool level scripting sets on an animator that is switched off right now is kept and set once it plays again (see
    /// <see cref="WKTAnimatorMemory"/>). Only in a multiplayer run; singleplayer only switches levels off far behind the player.
    /// </summary>
    [HarmonyPatch(typeof(UT_SimpleAnimatorController), nameof(UT_SimpleAnimatorController.SetAnimatorBool))]
    internal sealed class AnimatorBoolMemory_Patch {
        [UsedImplicitly]
        private static void Postfix(UT_SimpleAnimatorController __instance, string s) {
            if (WKT.InstanceOrNull?.Network is not { IsMultiplayerRun: true } || string.IsNullOrEmpty(s)) return;
            if (!__instance.TryGetComponent<Animator>(out var animator)) return;
            var parts = s.Split(':');
            if (parts.Length != 2 || !bool.TryParse(parts[1], out var value)) return;
            if (animator.isActiveAndEnabled && !animator.TryGetComponent<WKTAnimatorMemory>(out _)) return;
            WKTAnimatorMemory.Remember(animator, parts[0], value);
        }
    }

    /// <summary>
    /// A trigger set on an animator that is switched off right now, by a world event the host applied, is set once it plays
    /// again (see <see cref="WKTAnimatorMemory"/>). Only while such an event is applied here, so gameplay code is untouched.
    /// </summary>
    [HarmonyPatch(typeof(Animator), nameof(Animator.SetTrigger), typeof(string))]
    internal sealed class AnimatorTriggerMemory_Patch {
        [UsedImplicitly]
        private static void Postfix(Animator __instance, string __0) {
            if (WKTDoorSync.IsApplying && !string.IsNullOrEmpty(__0) && !__instance.isActiveAndEnabled)
                WKTAnimatorMemory.RememberTrigger(__instance, __0);
        }
    }
}
