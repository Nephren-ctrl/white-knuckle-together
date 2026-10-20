using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using JetBrains.Annotations;
using UnityEngine;

namespace WhiteKnuckleTogether {
    /// <summary>A rope that started is synced from now on (see <see cref="WKTRopeSync"/>).</summary>
    [WKTCriticalPatch(WKTPatchCategory.WorldSync), HarmonyPatch(typeof(ENV_Rope), "Start")]
    internal sealed class RopeStart_Patch {
        [UsedImplicitly]
        private static void Postfix(ENV_Rope __instance) => WKTWorldSync.Instance?.Ropes.Register(__instance);
    }

    [WKTCriticalPatch(WKTPatchCategory.WorldSync), HarmonyPatch(typeof(ENV_Rope), "OnDestroy")]
    internal sealed class RopeDestroy_Patch {
        [UsedImplicitly]
        private static void Postfix(ENV_Rope __instance) => WKTWorldSync.Instance?.Ropes.Unregister(__instance);
    }

    /// <summary>A rope another machine simulates shows that machine's state instead of simulating itself.</summary>
    [WKTCriticalPatch(WKTPatchCategory.WorldSync), HarmonyPatch(typeof(ENV_Rope), "FixedUpdate")]
    internal sealed class RopeFixedUpdate_Patch {
        [UsedImplicitly]
        private static bool Prefix(ENV_Rope __instance) {
            var sync = WKTWorldSync.Instance;
            return sync == null || sync.Ropes.BeforeFixedUpdate(__instance);
        }
    }

    /// <summary>The local player's push on a rope another machine simulates goes to that machine.</summary>
    [WKTCriticalPatch(WKTPatchCategory.WorldSync), HarmonyPatch(typeof(ENV_Rope), nameof(ENV_Rope.AddVelocity))]
    internal sealed class RopeAddVelocity_Patch {
        [UsedImplicitly]
        private static bool Prefix(ENV_Rope __instance, Vector3 v) {
            var sync = WKTWorldSync.Instance;
            return sync == null || sync.Ropes.BeforeAddVelocity(__instance, v);
        }
    }

    /// <summary>
    /// The local player taking hold of a rope link. It moves the rope's simulation before the game gives the rope its grab
    /// push, so that push lands where the rope is simulated.
    /// </summary>
    [WKTCriticalPatch(WKTPatchCategory.WorldSync), HarmonyPatch(typeof(CL_Handhold_Rope), nameof(CL_Handhold_Rope.Interact))]
    internal sealed class RopeHandholdInteract_Patch {
        [UsedImplicitly]
        private static void Prefix(CL_Handhold_Rope __instance, Clickable.InteractionInfo? info, out bool __state) {
            __state = false;
            var sync = WKTWorldSync.Instance;
            if (sync == null || info == null || info.player == null || info.player != ENT_Player.playerObject) return;
            sync.Ropes.OnLocalGrab(__instance);
            WKTRopeSync.SetGrabbing(true);
            __state = true;
        }

        [UsedImplicitly]
        private static Exception? Finalizer(Exception? __exception, bool __state) {
            if (__state)
                WKTRopeSync.SetGrabbing(false);
            return __exception;
        }
    }

    /// <summary>
    /// A puppet rope's end is kinematic and does not move by itself, so the game's grab and jump pushes read its velocity as
    /// shown instead.
    /// </summary>
    [WKTCriticalPatch(WKTPatchCategory.WorldSync), HarmonyPatch]
    internal sealed class RopeHandholdVelocity_Patch {
        public static readonly MethodInfo GetLinearVelocity = AccessTools.PropertyGetter(typeof(Rigidbody), nameof(Rigidbody.linearVelocity));
        private static readonly MethodInfo s_getVelocity = AccessTools.Method(typeof(WKTRopeSync), nameof(WKTRopeSync.GetVelocity));

        public static IEnumerable<MethodBase> GetTargets() {
            yield return AccessTools.Method(typeof(CL_Handhold_Rope), nameof(CL_Handhold_Rope.Interact));
            yield return AccessTools.Method(typeof(CL_Handhold_Rope), nameof(CL_Handhold_Rope.StopInteract));
        }

        [UsedImplicitly]
        private static IEnumerable<MethodBase> TargetMethods() => GetTargets();

        [UsedImplicitly]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod) {
            var result = WKTCallRewriter.RedirectCalls(instructions, GetLinearVelocity, s_getVelocity, out var count);
            if (count == 0)
                throw new InvalidOperationException($"Found no rope velocity to redirect in {__originalMethod.DeclaringType?.Name}.{__originalMethod.Name}.");
            return result;
        }
    }

    /// <summary>
    /// The local player's shot leaving a rope in a wall (the rope rebar) is shared with everyone (see
    /// <see cref="WKTPlacedRopeSync"/>): the hit effect it makes is watched for one carrying a rope. Another player's shot
    /// shown here makes none, since the shooter's placement builds it.
    /// </summary>
    [WKTCriticalPatch(WKTPatchCategory.WorldSync), HarmonyPatch(typeof(Projectile), "CreateHitEffect")]
    internal sealed class ProjectileCreateHitEffect_Patch {
        private static readonly MethodInfo s_hook = AccessTools.Method(typeof(WKTPlacedRopeSync), nameof(WKTPlacedRopeSync.SpawnHitEffect));

        [UsedImplicitly]
        private static void Prefix(Projectile __instance, Projectile.HitEffect effect) => WKTPlacedRopeSync.BeginHit(__instance, effect);

        [UsedImplicitly]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) {
            var result = WKTCallRewriter.RedirectCalls(instructions, LocalPrefabSpawn_Patch.InstantiateAtPose, s_hook, out var count);
            if (count == 0)
                throw new InvalidOperationException("Found no hit effect spawn to redirect in Projectile.CreateHitEffect.");
            return result;
        }

        [UsedImplicitly]
        private static Exception? Finalizer(Exception? __exception) {
            WKTPlacedRopeSync.EndHit();
            return __exception;
        }
    }
}
