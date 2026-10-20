using System.Collections.Generic;
using HarmonyLib;
using JetBrains.Annotations;
using UnityEngine;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// Artifact altars (<c>ENV_ArtifactDevice</c>) with "own artifact picks" on in a synced co-op run: every machine deals its
    /// own pair, from the altar's seed with the player's id mixed in, and keeps them to itself (<see cref="WKTLocalObject"/>),
    /// so each player picks their own artifact. Off, the host's pair is everyone's (see the host's pickup handling). They are
    /// marked as they are made: their props register on being enabled, inside the altar's Start, and registered alike on
    /// every machine, one player's pickup took everyone's.
    /// </summary>
    [HarmonyPatch(typeof(ENV_ArtifactDevice), "Start")]
    internal sealed class OwnArtifactPicks_Patch {
        private static readonly AccessTools.FieldRef<ENV_ArtifactDevice, List<Item_Object>> s_spawned =
            AccessTools.FieldRefAccess<ENV_ArtifactDevice, List<Item_Object>>("spawnedArtifacts");

        private static bool IsActive => WKT.InstanceOrNull?.Network is { IsMultiplayerRun: true } network && network.Session.IsSyncedWorld &&
                                        network.Session.InstancedArtifacts && network.LocalPlayer != null;

        /// <summary>After the content seeding's own prefix, so the pair is dealt from this player's numbers.</summary>
        [UsedImplicitly, HarmonyPriority(Priority.Last)]
        private static void Prefix(out Random.State? __state) {
            __state = null;
            if (!IsActive) return;
            __state = Random.state;
            WKTWorldSync.LocalSpawnDepth++;
            Random.InitState(WKTPerkSeeds.ForPlayer(Random.Range(int.MinValue, int.MaxValue), WKT.Instance.Network.LocalPlayer!.Id));
        }

        [UsedImplicitly, HarmonyPriority(Priority.First)]
        private static void Postfix(ENV_ArtifactDevice __instance, Random.State? __state) {
            if (__state == null) return;
            // Before the content seeding's own postfix, which puts back the state from before the level's seeding.
            Random.state = __state.Value;
            foreach (var artifact in s_spawned(__instance)) {
                if (artifact != null && !artifact.TryGetComponent<WKTLocalObject>(out _))
                    artifact.gameObject.AddComponent<WKTLocalObject>();
            }
        }

        /// <summary>Even if Start throws, later spawns are shared again.</summary>
        [UsedImplicitly]
        private static void Finalizer(Random.State? __state) {
            if (__state != null)
                WKTWorldSync.LocalSpawnDepth--;
        }
    }
}
