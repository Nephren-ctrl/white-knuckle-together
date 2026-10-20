using HarmonyLib;
using JetBrains.Annotations;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// The game's own progression gate. Reporting false while locked makes the game skip achievement checks, achievement
    /// setting, and leaderboard eligibility by itself. The sink patches still block everything independently.
    /// </summary>
    [WKTCriticalPatch(WKTPatchCategory.ProgressionGate), HarmonyPatch(typeof(CL_GameManager), nameof(CL_GameManager.AreAchievementsAllowed))]
    internal sealed class GameManagerAreAchievementsAllowed_Patch {
        [UsedImplicitly]
        private static void Postfix(ref bool __result) {
            if (WKTProgressionLock.IsLocked)
                __result = false;
        }
    }
}
