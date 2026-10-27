using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using JetBrains.Annotations;
using UnityEngine;

namespace WhiteKnuckleTogether {
    /// <summary>An entity's <c>PlaySound(string)</c> plays on the machine simulating it and is sent to the others (see <see cref="WKTEntitySounds"/>).</summary>
    [WKTCriticalPatch(WKTPatchCategory.WorldSync), HarmonyPatch(typeof(UT_AudioClipHandler), nameof(UT_AudioClipHandler.PlaySound), typeof(string))]
    internal sealed class EntitySoundPlay_Patch {
        [UsedImplicitly]
        private static bool Prefix(UT_AudioClipHandler __instance, string __0) =>
            WKTEntitySounds.BeforeSound(__instance, WKTEntitySoundKind.Play, __0, 0f, Vector3.zero);
    }

    /// <summary>The same for <c>PlaySound(string, float)</c>.</summary>
    [WKTCriticalPatch(WKTPatchCategory.WorldSync), HarmonyPatch(typeof(UT_AudioClipHandler), nameof(UT_AudioClipHandler.PlaySound), typeof(string), typeof(float))]
    internal sealed class EntitySoundPlayVolume_Patch {
        [UsedImplicitly]
        private static bool Prefix(UT_AudioClipHandler __instance, string __0, float __1) =>
            WKTEntitySounds.BeforeSound(__instance, WKTEntitySoundKind.PlayVolume, __0, __1, Vector3.zero);
    }

    /// <summary>The same for <c>PlaySound(string, Vector3)</c>.</summary>
    [WKTCriticalPatch(WKTPatchCategory.WorldSync), HarmonyPatch(typeof(UT_AudioClipHandler), nameof(UT_AudioClipHandler.PlaySound), typeof(string), typeof(Vector3))]
    internal sealed class EntitySoundPlayAt_Patch {
        [UsedImplicitly]
        private static bool Prefix(UT_AudioClipHandler __instance, string __0, Vector3 __1) =>
            WKTEntitySounds.BeforeSound(__instance, WKTEntitySoundKind.PlayAt, __0, 0f, __1);
    }

    /// <summary>The same for <c>StopSound(string)</c>.</summary>
    [WKTCriticalPatch(WKTPatchCategory.WorldSync), HarmonyPatch(typeof(UT_AudioClipHandler), nameof(UT_AudioClipHandler.StopSound), typeof(string))]
    internal sealed class EntitySoundStop_Patch {
        [UsedImplicitly]
        private static bool Prefix(UT_AudioClipHandler __instance, string __0) =>
            WKTEntitySounds.BeforeSound(__instance, WKTEntitySoundKind.Stop, __0, 0f, Vector3.zero);
    }

    /// <summary>The Buddy drone's event lines, played straight from a clip, are sent the same way.</summary>
    [WKTCriticalPatch(WKTPatchCategory.WorldSync), HarmonyPatch(typeof(DEN_Buddy), nameof(DEN_Buddy.PlaySound))]
    internal sealed class BuddyLine_Patch {
        [UsedImplicitly]
        private static bool Prefix(DEN_Buddy __instance, AudioClip __0) => WKTEntitySounds.BeforeBuddyLine(__instance, __0);
    }

    /// <summary>
    /// Ticks that run on puppets as well as on the simulating machine (<see cref="PuppetTick_Patch.AllowedOnPuppets"/>) play
    /// their sounds, events and pushes on every machine by themselves, so none of them are sent.
    /// </summary>
    [WKTCriticalPatch(WKTPatchCategory.WorldSync), HarmonyPatch]
    internal sealed class PuppetTickLocalReplay_Patch {
        [UsedImplicitly]
        private static IEnumerable<MethodBase> TargetMethods() => PuppetTick_Patch.AllowedOnPuppets;

        [UsedImplicitly]
        private static void Prefix() => WKTLocalReplayScope.Enter();

        [UsedImplicitly]
        private static Exception? Finalizer(Exception? __exception) {
            WKTLocalReplayScope.Exit();
            return __exception;
        }
    }
}
