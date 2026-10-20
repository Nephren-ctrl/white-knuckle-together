using HarmonyLib;
using UnityEngine;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// Level scripting that changes the route's levels runs on every machine of a synced run, wherever that machine's own
    /// player is. The game's versions act on the local player's current level or branch, which is only the trigger's on the
    /// machine of whoever set it off, so they act on the trigger's own instead:
    /// <list type="bullet">
    /// <item>Resuming generation (<c>M_Level.ContinueGeneration</c>) un-stops the trigger's level, not this machine's
    /// current one, so a player who reaches it later still generates past it.</item>
    /// <item>Deleting the other branches (<c>UT_WorldLoaderController.DeleteOtherBranches</c>) keeps the trigger's branch.
    /// Whoever was on another one was brought to whoever set it off first (see <see cref="WKTDoorSync"/>).</item>
    /// <item>Moving into a level's branch (<c>M_Level.MovePlayerIntoLevelBranch</c>) only switches branch where the local
    /// player is in that level: elsewhere it would switch off the levels around them.</item>
    /// </list>
    /// Inert outside a synced run (<see cref="WKTPlayerPositions.IsSharedWorld"/>).
    /// </summary>
    internal static class WKTLevelScripting {
        private static readonly AccessTools.FieldRef<M_Level, bool> s_isPausingGeneration = AccessTools.FieldRefAccess<M_Level, bool>("isPausingGeneration");

        /// <summary>Resumes generation at <paramref name="level"/>'s own place on the route. False when the game's own version should run.</summary>
        public static bool TryContinueGeneration(M_Level level) {
            if (!WKTPlayerPositions.IsSharedWorld || WorldLoader.instance is not { } loader) return false;
            var info = loader.GetBranchOfLevel(level)?.GetLevelInfoFromLevel(level);
            if (info == null) return false;
            // What the game's ContinueGeneration clears on the level itself, before resuming at the current level.
            level.lastLevel = false;
            s_isPausingGeneration(level) = false;
            info.stopLevel = false;
            return true;
        }

        /// <summary>Makes the branch of <paramref name="controller"/>'s level the current one, so deleting the others keeps it.</summary>
        public static void KeepOwnBranch(UT_WorldLoaderController controller) {
            if (!WKTPlayerPositions.IsSharedWorld || WorldLoader.instance is not { } loader) return;
            var level = controller.GetComponentInParent<M_Level>(true);
            if (level == null) return;
            var branch = loader.GetBranchOfLevel(level);
            if (branch == null || branch == loader.GetCurrentBranch()) return;
            WKT.Instance.Logger.LogInfo($"Deleting the other branches from {level.name}: keeping its branch {branch.id}, not this player's.");
            loader.ChangeToLevelBranch(level);
        }

        /// <summary>Whether moving into <paramref name="level"/>'s branch may go ahead here: where the local player is in it.</summary>
        public static bool AllowMoveIntoBranch(M_Level level) {
            if (!WKTPlayerPositions.IsSharedWorld || ENT_Player.playerObject is not { dead: false } local) return true;
            return level.GetLocalBounds().Contains(level.transform.worldToLocalMatrix.MultiplyPoint(local.transform.position));
        }

        /// <summary>The id of the branch <paramref name="owner"/>'s level is on, the same on every machine, or null.</summary>
        public static string? GetBranchId(Component owner) {
            var level = owner.GetComponentInParent<M_Level>(true);
            return level != null && WorldLoader.instance is { } loader ? loader.GetBranchOfLevel(level)?.id : null;
        }
    }
}
