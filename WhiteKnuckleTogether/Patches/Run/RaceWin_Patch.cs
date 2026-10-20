using HarmonyLib;
using JetBrains.Annotations;

// The end of the run during a race. See WKTRace. Not critical: without it, a racer reaching the end only ends their own run.
namespace WhiteKnuckleTogether {
    /// <summary>The game's win at the end of the run: in a race, the first to get there wins it for everyone.</summary>
    [HarmonyPatch(typeof(CL_GameManager), nameof(CL_GameManager.Win))]
    internal sealed class GameManagerWin_Patch {
        [UsedImplicitly]
        private static bool Prefix() => !WKTRace.TryHandleFinish();
    }
}
