using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using JetBrains.Annotations;

namespace WhiteKnuckleTogether {
    /// <summary>Mother is enraged by the host only, for the player who set it off (see <see cref="WKTMotherWrath"/>).</summary>
    [WKTCriticalPatch(WKTPatchCategory.WorldSync), HarmonyPatch(typeof(DEN_Mother), nameof(DEN_Mother.Enrage))]
    internal sealed class MotherEnrage_Patch {
        [UsedImplicitly]
        private static bool Prefix(DEN_Mother __instance, bool __0) => WKTWorldSync.Instance?.MotherWrath.BeforeEnrage(__instance, __0) ?? true;
    }

    /// <summary>Mother's crush kills the player who enraged her, on their own machine, and never a client's own player through a puppet.</summary>
    [WKTCriticalPatch(WKTPatchCategory.WorldSync), HarmonyPatch(typeof(DEN_Mother), nameof(DEN_Mother.KillPlayer))]
    internal sealed class MotherKillPlayer_Patch {
        [UsedImplicitly]
        private static bool Prefix(DEN_Mother __instance) => WKTWorldSync.Instance?.MotherWrath.BeforeKillPlayer(__instance) ?? true;
    }

    /// <summary>Mother's lines during a crush meant for a remote player are shown to them.</summary>
    [WKTCriticalPatch(WKTPatchCategory.WorldSync), HarmonyPatch(typeof(UT_PsychicCommunicator), nameof(UT_PsychicCommunicator.PlayString))]
    internal sealed class MotherLine_Patch {
        [UsedImplicitly]
        private static bool Prefix(UT_PsychicCommunicator __instance, string __0) {
            var wrath = WKTWorldSync.Instance?.MotherWrath;
            if (wrath == null) return true;
            var mother = __instance.GetComponentInParent<DEN_Mother>();
            return mother == null || wrath.BeforeLine(mother, __0);
        }
    }

    /// <summary>
    /// Mother only counts hits from the local player (<c>info.sourceEntity != ENT_Player.GetPlayer()</c>), so on the host a
    /// remote player's hit, which comes from their stand-in, is passed to her as the local player's. Whom it is for still
    /// follows the hit's dealer (<see cref="WKTDamageCause"/>).
    /// </summary>
    [WKTCriticalPatch(WKTPatchCategory.WorldSync), HarmonyPatch]
    internal sealed class MotherRemoteHit_Patch {
        [UsedImplicitly]
        private static IEnumerable<MethodBase> TargetMethods() {
            yield return AccessTools.Method(typeof(DEN_Mother), nameof(DEN_Mother.Damage), new[] { typeof(Damageable.DamageInfo) });
            yield return AccessTools.Method(typeof(DEN_Mother), nameof(DEN_Mother.Kill), new[] { typeof(string), typeof(Damageable.DamageInfo) });
        }

        [UsedImplicitly]
        private static void Prefix(object[] __args, out GameEntity? __state) {
            __state = null;
            if (WKTWorldSync.Instance == null) return;
            foreach (var argument in __args) {
                if (argument is not Damageable.DamageInfo { sourceEntity: WKTPlayerProxy proxy } info || ENT_Player.GetPlayer() is not { } local) continue;
                __state = proxy;
                info.sourceEntity = local;
            }
        }

        [UsedImplicitly]
        private static void Finalizer(object[] __args, GameEntity? __state) {
            if (__state == null) return;
            foreach (var argument in __args) {
                if (argument is Damageable.DamageInfo info)
                    info.sourceEntity = __state;
            }
        }
    }
}
