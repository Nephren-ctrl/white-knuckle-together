using System.Collections.Generic;
using HarmonyLib;
using JetBrains.Annotations;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// In a multiplayer lobby, only trinkets every player has unlocked can be picked; the rest show locked. During a run the
    /// shared progression already decides (see <see cref="ProgressionUnlockCheckUnlock_Patch"/>).
    /// </summary>
    [WKTCriticalPatch(WKTPatchCategory.ProgressionOverride), HarmonyPatch(typeof(Trinket), nameof(Trinket.IsUnlocked))]
    internal sealed class TrinketIsUnlocked_Patch {
        [UsedImplicitly]
        private static bool Prefix(Trinket __instance, ref bool __result) {
            var network = WKT.InstanceOrNull?.Network;
            if (network == null || network.Mode == NetworkMode.None || network.IsMultiplayerRun) return true;
            __result = network.IsLobbyTrinketAvailable(__instance);
            return false;
        }
    }

    /// <summary>
    /// A multiplayer run starts each player with their own picks, cut down to what is available and affordable (see
    /// <see cref="WKTTrinkets.GetRunTrinkets"/>), without touching the saved picks.
    /// </summary>
    [WKTCriticalPatch(WKTPatchCategory.ProgressionOverride), HarmonyPatch(typeof(M_Gamemode), nameof(M_Gamemode.StartFreshGamemode))]
    internal sealed class StartFreshGamemodeTrinkets_Patch {
        /// <summary>The picks to hand out while the run's trinkets are being given, or null.</summary>
        public static List<string>? RunTrinkets { get; private set; }

        [UsedImplicitly]
        private static void Prefix(M_Gamemode __instance) {
            var network = WKT.InstanceOrNull?.Network;
            if (network == null || !network.IsMultiplayerRun) return;
            WKTRunEvents.IsGrantingStartItems = true;
            RunTrinkets = WKTTrinkets.GetRunTrinkets(__instance);
            WKT.Instance.Logger.LogInfo($"Starting with trinkets [{string.Join(", ", RunTrinkets)}].");
        }

        [UsedImplicitly]
        private static void Finalizer() {
            if (!WKTRunEvents.IsGrantingStartItems) return;
            WKTRunEvents.IsGrantingStartItems = false;
            WKTRunEvents.MarkStartingItems();
            RunTrinkets = null;
            WKTTrinkets.ClearRunPicks();
        }
    }

    /// <summary>While a multiplayer run hands out its trinkets, the save's picks read as the run's (see above).</summary>
    [WKTCriticalPatch(WKTPatchCategory.ProgressionOverride), HarmonyPatch(typeof(StatManager.SaveData), nameof(StatManager.SaveData.GetGamemodeTrinkets))]
    internal sealed class GetGamemodeTrinkets_Patch {
        [UsedImplicitly]
        private static bool Prefix(ref List<string> __result) {
            var run = StartFreshGamemodeTrinkets_Patch.RunTrinkets;
            if (run == null) return true;
            __result = new List<string>(run);
            return false;
        }
    }
}
