using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using JetBrains.Annotations;
using UnityEngine;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// A Hunter's wall slams: the machine simulating it counts them for its puppets (see <see cref="WKTHunterPresenter"/>). A
    /// puppet does not slam by itself; its sound and event arrive from the simulating machine.
    /// </summary>
    [WKTCriticalPatch(WKTPatchCategory.WorldSync), HarmonyPatch(typeof(DEN_Hunter), "OnCollisionEnter")]
    internal sealed class HunterSlam_Patch {
        private static readonly int s_world = LayerMask.NameToLayer("World");
        private static readonly int s_default = LayerMask.NameToLayer("Default");

        [UsedImplicitly]
        private static bool Prefix(DEN_Hunter __instance) => !WKTWorldSync.IsPuppet(__instance);

        [UsedImplicitly]
        private static void Postfix(DEN_Hunter __instance, Collision __0) {
            var presentation = WKTWorldSync.Instance?.Presentation;
            if (presentation == null || WKTWorldSync.IsPuppet(__instance)) return;
            var layer = __0.gameObject.layer;
            var speed = __0.relativeVelocity.magnitude;
            // DEN_Hunter.OnCollisionEnter's own test for a slam.
            if ((layer == s_world || layer == s_default) && speed > __instance.slamMinimumVelocity)
                presentation.Get<WKTHunterPresenter>(__instance)?.OnSlam(speed);
        }
    }

    /// <summary>A Rat King's collisions, the same way (see <see cref="WKTRatKingPresenter"/>).</summary>
    [WKTCriticalPatch(WKTPatchCategory.WorldSync), HarmonyPatch(typeof(DEN_RatKing), "OnCollisionEnter")]
    internal sealed class RatKingSlam_Patch {
        [UsedImplicitly]
        private static bool Prefix(DEN_RatKing __instance) => !WKTWorldSync.IsPuppet(__instance);

        [UsedImplicitly]
        private static void Postfix(DEN_RatKing __instance, Collision __0) {
            var presentation = WKTWorldSync.Instance?.Presentation;
            if (presentation == null || WKTWorldSync.IsPuppet(__instance)) return;
            presentation.Get<WKTRatKingPresenter>(__instance)?.OnCollision(__0.impulse.magnitude);
        }
    }

    /// <summary>A turret's shots: the machine simulating it counts them for its puppets (see <see cref="WKTTurretPresenter"/>).</summary>
    [WKTCriticalPatch(WKTPatchCategory.WorldSync), HarmonyPatch(typeof(DEN_Turret), "PlayTracer")]
    internal sealed class TurretTracer_Patch {
        [UsedImplicitly]
        private static void Postfix(DEN_Turret __instance, Vector3 __1, bool __3) {
            var presentation = WKTWorldSync.Instance?.Presentation;
            if (presentation == null || WKTWorldSync.IsPuppet(__instance)) return;
            presentation.Get<WKTTurretPresenter>(__instance)?.OnTracer(__1, __3);
        }
    }

    /// <summary>
    /// A puppet turret is switched on and off only by the host's turret, whose state, sounds and events it shows (see
    /// <see cref="WKTTurretPresenter"/>). The level's buttons and timers switch the host's.
    /// </summary>
    [WKTCriticalPatch(WKTPatchCategory.WorldSync), HarmonyPatch]
    internal sealed class PuppetTurretActivation_Patch {
        public static IEnumerable<MethodBase> GetTargets() {
            yield return AccessTools.Method(typeof(DEN_Turret), nameof(DEN_Turret.Activate));
            yield return AccessTools.Method(typeof(DEN_Turret), nameof(DEN_Turret.Deactivate));
            yield return AccessTools.Method(typeof(DEN_Turret), nameof(DEN_Turret.ToggleActivated));
        }

        [UsedImplicitly]
        private static IEnumerable<MethodBase> TargetMethods() => GetTargets();

        [UsedImplicitly]
        private static bool Prefix(DEN_Turret __instance) => !WKTWorldSync.IsPuppet(__instance);
    }

    /// <summary>
    /// On the machine simulating the Teeth, its distance is to the nearest player (see <see cref="NearestPlayerDistance_Patch"/>),
    /// which its Animation also used for the hands over this machine's screen. Unless the Teeth holds still for a kill, they
    /// go by the local player's own distance instead (see <see cref="WKTTeethPresenter.ShowLocalEffects"/>).
    /// </summary>
    [HarmonyPatch(typeof(DEN_Teeth), "Animation")]
    internal sealed class TeethScreenHands_Patch {
        private static readonly AccessTools.FieldRef<DEN_Teeth, bool> s_locked = AccessTools.FieldRefAccess<DEN_Teeth, bool>("locked");

        [UsedImplicitly]
        private static void Postfix(DEN_Teeth __instance) {
            if (!WKTPlayerPositions.IsCoveringAllPlayers || s_locked(__instance)) return;
            WKTTeethPresenter.ShowLocalEffects(__instance);
        }
    }
}
