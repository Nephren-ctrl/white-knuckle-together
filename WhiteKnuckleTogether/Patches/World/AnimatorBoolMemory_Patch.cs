using HarmonyLib;
using JetBrains.Annotations;
using UnityEngine;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// A bool level scripting sets on an animator that is switched off right now is kept and set once it plays again (see
    /// <see cref="WKTAnimatorBoolMemory"/>). Only in a multiplayer run; singleplayer only switches levels off far behind the player.
    /// </summary>
    [HarmonyPatch(typeof(UT_SimpleAnimatorController), nameof(UT_SimpleAnimatorController.SetAnimatorBool))]
    internal sealed class AnimatorBoolMemory_Patch {
        [UsedImplicitly]
        private static void Postfix(UT_SimpleAnimatorController __instance, string s) {
            if (WKT.InstanceOrNull?.Network is not { IsMultiplayerRun: true } || string.IsNullOrEmpty(s)) return;
            if (!__instance.TryGetComponent<Animator>(out var animator)) return;
            var parts = s.Split(':');
            if (parts.Length != 2 || !bool.TryParse(parts[1], out var value)) return;
            if (animator.isActiveAndEnabled && !animator.TryGetComponent<WKTAnimatorBoolMemory>(out _)) return;
            WKTAnimatorBoolMemory.Remember(animator, parts[0], value);
        }
    }
}
