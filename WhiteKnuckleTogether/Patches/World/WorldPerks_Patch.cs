using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using JetBrains.Annotations;

// Perks that act on the world act on it for everyone in a shared world. See WKTWorldPerks. Not critical: without them, each
// player's world perks only act on their own machine, as before.
namespace WhiteKnuckleTogether {
    /// <summary>
    /// Spawn tables weighted by a perk count it if any player has it, so every machine fills levels the same way from the
    /// shared seed (Funniest Timeline's roaches, Piton Enthusiast's pitons and the like).
    /// </summary>
    [HarmonyPatch(typeof(SSM_PerkChecker), nameof(SSM_PerkChecker.Check))]
    internal sealed class PerkChecker_Patch {
        [UsedImplicitly]
        private static bool Prefix(SSM_PerkChecker __instance, ref float __result) {
            if (WKTWorldSync.Instance == null || !WKTPlayerPerks.HasAny(__instance.perkID)) return true;
            __result = __instance.trueValue;
            return false;
        }
    }

    /// <summary>A client's item spawners and hunters run on the host instead, which spawns them for everyone.</summary>
    [HarmonyPatch]
    internal sealed class ClientWorldPerkBlock_Patch {
        [UsedImplicitly]
        private static IEnumerable<MethodBase> TargetMethods() {
            yield return AccessTools.Method(typeof(PerkModule_ObjectSpawner), nameof(PerkModule_ObjectSpawner.Update));
            yield return AccessTools.Method(typeof(PerkModule_Hunted), nameof(PerkModule_Hunted.Update));
        }

        [UsedImplicitly]
        private static bool Prefix() => !ClientSpawnerBlock_Patch.IsSyncedClient();
    }
}
