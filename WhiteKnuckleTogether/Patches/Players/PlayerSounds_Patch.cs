using HarmonyLib;
using JetBrains.Annotations;
using UnityEngine;

// Report the local player's body sounds to WKTPlayerSoundRecorder, which only exists during a multiplayer run, so
// singleplayer pays one null check per sound and plays exactly as before. Not critical: without them, remote players
// are only silent.
namespace WhiteKnuckleTogether {
    /// <summary>A set playing one of its own clips: jumps, landings, mantling, a hand slipping off.</summary>
    [HarmonyPatch(typeof(UT_AudioClipHandler.AudioSet), nameof(UT_AudioClipHandler.AudioSet.Play), typeof(float), typeof(AudioSource))]
    internal sealed class AudioSetPlay_Patch {
        [UsedImplicitly]
        private static void Prefix(UT_AudioClipHandler.AudioSet __instance) => WKTPlayerSoundRecorder.Active?.OnSetPlayed(__instance);
    }

    /// <summary>A set playing a clip it is given: a landing on a material with landing sounds of its own.</summary>
    [HarmonyPatch(typeof(UT_AudioClipHandler.AudioSet), nameof(UT_AudioClipHandler.AudioSet.PlaySound))]
    internal sealed class AudioSetPlaySound_Patch {
        [UsedImplicitly]
        private static void Prefix(UT_AudioClipHandler.AudioSet __instance) => WKTPlayerSoundRecorder.Active?.OnSetPlayedClip(__instance);
    }

    /// <summary>A hand taking hold of a handhold, whose grab sound depends on its material: chains sound like chains.</summary>
    [HarmonyPatch(typeof(ENT_Player), "PlayHandholdSound")]
    internal sealed class PlayerHandholdSound_Patch {
        [UsedImplicitly]
        private static void Prefix(ENT_Player __instance, string handholdMaterial) => WKTPlayerSoundRecorder.Active?.OnHandholdSound(__instance, handholdMaterial);
    }

    /// <summary>Footsteps bypass the sets. A step was played when the time to the next one was reset upwards.</summary>
    [HarmonyPatch(typeof(ENT_Player), "FootstepAudio")]
    internal sealed class PlayerFootstepAudio_Patch {
        [UsedImplicitly]
        private static void Prefix(float ___footstepTime, out float __state) => __state = ___footstepTime;

        [UsedImplicitly]
        private static void Postfix(ENT_Player __instance, float ___footstepTime, float __state, ENT_Player.Footsteps? ___curFootstep) {
            if (___footstepTime > __state)
                WKTPlayerSoundRecorder.Active?.OnFootstep(__instance, ___curFootstep);
        }
    }
}
