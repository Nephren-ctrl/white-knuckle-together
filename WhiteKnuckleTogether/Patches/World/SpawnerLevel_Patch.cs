using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using JetBrains.Annotations;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// <c>UT_SPT_Spawner</c> puts what it spawns in the local player's current level, such as the roaches a Breeder Roach lets
    /// out when it dies. In a synced co-op run the host spawns for every player, so a Breeder dying by a remote player would
    /// leave its roaches in the host's level, to vanish when that level unloads. They go in the level the spawner is in instead.
    /// </summary>
    [WKTCriticalPatch(WKTPatchCategory.WorldSync), HarmonyPatch(typeof(UT_SPT_Spawner), nameof(UT_SPT_Spawner.Spawn), typeof(string))]
    internal sealed class SpawnerLevel_Patch {
        public static readonly MethodInfo GetCurrentLevel = AccessTools.Method(typeof(WorldLoader), nameof(WorldLoader.GetCurrentLevel));
        /// <summary>The spawner spawning now; spawners started by what it spawns nest inside it.</summary>
        private static UT_SPT_Spawner? s_spawner;

        [UsedImplicitly]
        private static void Prefix(UT_SPT_Spawner __instance, out UT_SPT_Spawner? __state) {
            __state = s_spawner;
            s_spawner = __instance;
        }

        [UsedImplicitly]
        private static Exception? Finalizer(Exception? __exception, UT_SPT_Spawner? __state) {
            s_spawner = __state;
            return __exception;
        }

        [UsedImplicitly]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
            WKTCallRewriter.RedirectCalls(instructions, GetCurrentLevel, AccessTools.Method(typeof(SpawnerLevel_Patch), nameof(LevelFor)), out _);

        /// <summary>Replaces <c>loader.GetCurrentLevel()</c>: in a synced co-op run, the level the spawning spawner is in.</summary>
        public static WorldLoader.LevelInfo LevelFor(WorldLoader loader) {
            var spawner = s_spawner;
            if (WKTWorldSync.Instance == null || spawner == null) return loader.GetCurrentLevel();
            var levels = loader.GetCurrentBranch()?.levelTracker;
            return (levels != null ? WKTHostStreaming.FindClosestLevel(levels, spawner.transform.position) : null) ?? loader.GetCurrentLevel();
        }
    }
}
