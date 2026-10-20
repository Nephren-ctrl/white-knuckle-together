using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using JetBrains.Annotations;
using UnityEngine;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// Proximity checks that read the local player's position measure from the nearest player instead, so props wake up
    /// and wave zones work around every player the host simulates for. Each rewritten read checks one static outside such
    /// a run and then reads the local player as before.
    /// </summary>
    [WKTCriticalPatch(WKTPatchCategory.HostSimulation), HarmonyPatch]
    internal sealed class NearestPlayerPosition_Patch {
        private static readonly MethodInfo s_nearestTo = AccessTools.Method(typeof(WKTPlayerPositions), nameof(WKTPlayerPositions.NearestTo));

        public static IEnumerable<MethodBase> GetTargets() {
            yield return AccessTools.Method(typeof(CL_Prop), nameof(CL_Prop.TickUpdate));
            yield return AccessTools.Method(typeof(ENV_DenizenWaveZone), nameof(ENV_DenizenWaveZone.SpawnRoutine));
        }

        [UsedImplicitly]
        private static IEnumerable<MethodBase> TargetMethods() => GetTargets();

        /// <summary>
        /// Runs before <see cref="EnemyTargets_Patch"/>, which also rewrites <c>ENT_Player.GetPlayer()</c> in shared targets
        /// such as <see cref="ENV_DenizenWaveZone.SpawnRoutine"/>. Without a fixed priority Harmony orders the two by patch
        /// registration, which follows assembly type order and differs between builds.
        /// </summary>
        [UsedImplicitly, HarmonyPriority(Priority.First)]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod) {
            var result = WKTCallRewriter.RewritePlayerPosition(instructions, s_nearestTo, out var count);
            if (count == 0)
                throw new InvalidOperationException($"Found no player position to redirect in {__originalMethod.DeclaringType?.Name}.{__originalMethod.Name}.");
            return result;
        }
    }

    /// <summary>Spawners and triggers that use <c>CL_GameManager.PlayerDistance</c> measure to the nearest player instead.</summary>
    [WKTCriticalPatch(WKTPatchCategory.HostSimulation), HarmonyPatch]
    internal sealed class NearestPlayerDistance_Patch {
        public static readonly MethodInfo PlayerDistance = AccessTools.Method(typeof(CL_GameManager), nameof(CL_GameManager.PlayerDistance));
        private static readonly MethodInfo s_nearestDistance = AccessTools.Method(typeof(WKTPlayerPositions), nameof(WKTPlayerPositions.NearestDistance));
        /// <summary>World content that triggers on a player coming close. Effects and audio keep measuring the local player.</summary>
        private static readonly System.Type[] s_types = { typeof(UT_TriggerSpawner), typeof(UT_PlayerTriggerRadius), typeof(DEN_Teeth) };

        /// <summary>Every method of <see cref="s_types"/> that calls <see cref="PlayerDistance"/>.</summary>
        public static List<MethodBase> GetTargets() {
            var targets = new List<MethodBase>();
            var callees = new List<MethodBase>();
            foreach (var type in s_types) {
                foreach (var method in type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)) {
                    callees.Clear();
                    if (WKTILCallReader.TryGetCallees(method, callees) && callees.Contains(PlayerDistance))
                        targets.Add(method);
                }
            }
            return targets;
        }

        [UsedImplicitly]
        private static IEnumerable<MethodBase> TargetMethods() => GetTargets();

        [UsedImplicitly]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
            WKTCallRewriter.RedirectCalls(instructions, PlayerDistance, s_nearestDistance, out _);
    }

    /// <summary>A wave zone is occupied while any player is inside it.</summary>
    [WKTCriticalPatch(WKTPatchCategory.HostSimulation), HarmonyPatch(typeof(ENV_DenizenWaveZone), nameof(ENV_DenizenWaveZone.IsPlayerInWaveZone))]
    internal sealed class WaveZoneOccupied_Patch {
        [UsedImplicitly]
        private static void Postfix(ENV_DenizenWaveZone __instance, ref bool __result) {
            if (__result || !WKTPlayerPositions.IsCoveringAllPlayers) return;
            var bounds = __instance.waveZoneBounds;
            for (var i = 0; i < WKTPlayerPositions.RemoteCount; i++) {
                if (WKTPlayerPositions.IsRemoteDead(i)) continue;
                if (!bounds.Contains(__instance.transform.InverseTransformPoint(WKTPlayerPositions.GetRemotePosition(i)))) continue;
                __result = true;
                return;
            }
        }
    }

    /// <summary>A wave zone measures how far away something is from the nearest player.</summary>
    [WKTCriticalPatch(WKTPatchCategory.HostSimulation), HarmonyPatch(typeof(ENV_DenizenWaveZone), nameof(ENV_DenizenWaveZone.GetManhattenDistanceFromPlayer))]
    internal sealed class WaveZoneDistance_Patch {
        [UsedImplicitly]
        private static void Postfix(Vector3 position, ref float __result) {
            if (!WKTPlayerPositions.IsCoveringAllPlayers) return;
            for (var i = 0; i < WKTPlayerPositions.RemoteCount; i++) {
                if (WKTPlayerPositions.IsRemoteDead(i)) continue;
                var offset = position - WKTPlayerPositions.GetRemotePosition(i);
                var distance = Mathf.Abs(offset.x) + Mathf.Abs(offset.y) + Mathf.Abs(offset.z);
                if (distance < __result)
                    __result = distance;
            }
        }
    }
}
