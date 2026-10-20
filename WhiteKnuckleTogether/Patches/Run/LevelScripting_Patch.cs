using HarmonyLib;
using JetBrains.Annotations;

namespace WhiteKnuckleTogether {
    /// <summary>Resumes generation at the trigger's own level (see <see cref="WKTLevelScripting"/>).</summary>
    [WKTCriticalPatch(WKTPatchCategory.WorldSync), HarmonyPatch(typeof(M_Level), nameof(M_Level.ContinueGeneration))]
    internal sealed class LevelContinueGeneration_Patch {
        [UsedImplicitly]
        private static bool Prefix(M_Level __instance) => !WKTLevelScripting.TryContinueGeneration(__instance);
    }

    /// <summary>Keeps the trigger's own branch when the others are deleted (see <see cref="WKTLevelScripting"/>).</summary>
    [WKTCriticalPatch(WKTPatchCategory.WorldSync), HarmonyPatch(typeof(UT_WorldLoaderController), nameof(UT_WorldLoaderController.DeleteOtherBranches))]
    internal sealed class LoaderDeleteOtherBranches_Patch {
        [UsedImplicitly]
        private static void Prefix(UT_WorldLoaderController __instance) => WKTLevelScripting.KeepOwnBranch(__instance);
    }

    /// <summary>Moves into a level's branch only where the local player is in it (see <see cref="WKTLevelScripting"/>).</summary>
    [WKTCriticalPatch(WKTPatchCategory.WorldSync), HarmonyPatch(typeof(M_Level), nameof(M_Level.MovePlayerIntoLevelBranch))]
    internal sealed class LevelMovePlayerIntoBranch_Patch {
        [UsedImplicitly]
        private static bool Prefix(M_Level __instance) => WKTLevelScripting.AllowMoveIntoBranch(__instance);
    }
}
