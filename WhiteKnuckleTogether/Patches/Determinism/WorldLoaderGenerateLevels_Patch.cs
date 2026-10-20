using System.Collections;
using HarmonyLib;
using JetBrains.Annotations;
using UnityEngine;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// World generation seeds Unity's global random once, then keeps drawing from it across frames while level assets
    /// load. Load times differ per machine, so any other random call in between would give each player a different world
    /// from the same seed. In a multiplayer run, generation gets its own random state, and every level is keyed for
    /// <see cref="WKTContentSeeding"/> in the same frame it is created, before any of its contents roll.
    /// <para>
    /// Each generation also seeds from <see cref="WorldLoader.seed"/>, a single field every generation moves forward, so
    /// branches generated in a different order on each machine would differ. Every generation after the first therefore
    /// gets its own copy of the field, seeded from its branch and how many times that branch generated before.
    /// </para>
    /// Decided once per coroutine, and singleplayer gets the original coroutine back untouched.
    /// </summary>
    [WKTCriticalPatch(WKTPatchCategory.RunSync), HarmonyPatch(typeof(WorldLoader), "GenerateLevels")]
    internal sealed class WorldLoaderGenerateLevels_Patch {
        [UsedImplicitly]
        private static void Postfix(ref IEnumerator __result, WorldLoader __instance, WorldLoader.BranchInfo branch, WorldLoader.GenerationParameters? genParams) {
            var network = WKT.InstanceOrNull?.Network;
            if (network == null || !network.IsMultiplayerRun) return;
            WKTContentSeeding.EnsureBegun(network.Session.Run.Seed);
            IEnumerator generation = new WKTIsolatedRandomEnumerator<Random.State>(__result, UnityRandomStateAccess.Instance);
            if (WKTContentSeeding.TryGetGenerationSeed(branch.id, genParams?.seedOffset ?? 0, out var seed)) {
                var loader = __instance;
                generation = new WKTScopedValueEnumerator<int>(generation, () => loader.seed, value => loader.seed = value, seed);
            }
            __result = new WKTObservedEnumerator(generation, () => WKTContentSeeding.KeyNewLevels(branch));
        }
    }
}
