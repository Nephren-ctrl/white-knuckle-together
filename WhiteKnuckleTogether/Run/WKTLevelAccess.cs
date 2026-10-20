using System;
using HarmonyLib;
using UnityEngine;

namespace WhiteKnuckleTogether {
    /// <summary>Reads level bookkeeping the game keeps internal.</summary>
    internal static class WKTLevelAccess {
        /// <summary>How far above a level's entrance its <c>lowBounds</c> is set when the level is placed.</summary>
        private const float c_entrance_margin = 1f;
        private static readonly AccessTools.FieldRef<WorldLoader.LevelInfo, WorldLoader.BranchInfo.LevelId> s_levelId =
            AccessTools.FieldRefAccess<WorldLoader.LevelInfo, WorldLoader.BranchInfo.LevelId>("levelID");
        private static float[] s_entrances = new float[32];

        /// <summary>The id the generator gave the level. Null for a level that was never registered.</summary>
        public static WorldLoader.BranchInfo.LevelId? GetLevelId(WorldLoader.LevelInfo info) => s_levelId(info);

        /// <summary>
        /// The level's position among every level its branch has generated, including ones since unloaded. The same on every
        /// machine, unlike its index in the branch's tracker, which shifts as old levels unload. -1 if unknown.
        /// </summary>
        public static int GetGenerationIndex(WorldLoader.BranchInfo branch, WorldLoader.LevelInfo info) {
            var id = GetLevelId(info);
            var ids = branch.GetLevelIds();
            return id == null || ids == null ? -1 : ids.IndexOf(id);
        }

        /// <summary>
        /// The room of the current branch's route that <paramref name="position"/> is in: the generation index of the highest
        /// level whose entrance is at or below it. It only depends on the levels' entrance heights, so it is the same wherever
        /// the local player is and stays the same as old levels unload. -1 if unknown, such as below the first level.
        /// </summary>
        public static int GetRouteRoom(Vector3 position) {
            var loader = WorldLoader.instance;
            if (loader == null) return -1;
            var branch = loader.GetCurrentBranch();
            var tracker = branch?.levelTracker;
            if (tracker == null || tracker.Count == 0) return -1;
            if (s_entrances.Length < tracker.Count)
                s_entrances = new float[Mathf.NextPowerOfTwo(tracker.Count)];
            for (var i = 0; i < tracker.Count; i++) {
                var info = tracker[i];
                s_entrances[i] = info != null ? info.lowBounds + c_entrance_margin : float.PositiveInfinity;
            }
            var index = FindRoom(s_entrances.AsSpan(0, tracker.Count), position.y);
            return index < 0 ? -1 : GetGenerationIndex(branch!, tracker[index]);
        }

        /// <summary>The room of the current branch's route that <paramref name="level"/> itself is: its generation index. -1 if unknown.</summary>
        public static int GetLevelRoom(M_Level? level) {
            var branch = WorldLoader.instance != null ? WorldLoader.instance.GetCurrentBranch() : null;
            if (branch == null || level == null) return -1;
            var info = branch.GetLevelInfoFromLevel(level);
            return info == null ? -1 : GetGenerationIndex(branch, info);
        }

        /// <summary>The highest of <paramref name="entrances"/> at or below <paramref name="height"/>, or -1.</summary>
        public static int FindRoom(ReadOnlySpan<float> entrances, float height) {
            var best = -1;
            for (var i = 0; i < entrances.Length; i++) {
                if (entrances[i] <= height && (best < 0 || entrances[i] >= entrances[best]))
                    best = i;
            }
            return best;
        }
    }
}
