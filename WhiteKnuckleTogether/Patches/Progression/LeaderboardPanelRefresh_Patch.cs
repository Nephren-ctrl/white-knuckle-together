using HarmonyLib;
using JetBrains.Annotations;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// UI_GamemodeScreen refreshes the current panel's leaderboard unconditionally. A disabled leaderboard never ran
    /// Awake, so its score objects do not exist and Refresh would throw, aborting the gamemode screen setup.
    /// </summary>
    [HarmonyPatch(typeof(Leaderboard_Panel), nameof(Leaderboard_Panel.Refresh))]
    public sealed class LeaderboardPanelRefresh_Patch {
        [UsedImplicitly]
        public static bool Prefix(Leaderboard_Panel __instance) => __instance.GetComponent<WKTDisabledLeaderboard>() == null;
    }
}
