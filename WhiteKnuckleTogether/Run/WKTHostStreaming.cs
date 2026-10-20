using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// The game only keeps the local player's level and its neighbours loaded, unloads levels behind it, and generates
    /// more only when it reaches the last one. The host of a synced co-op run simulates the world for everyone, so it does
    /// the same around every remote player too (only while <see cref="WKTPlayerPositions.IsCoveringAllPlayers"/>). Every
    /// machine of a synced run unloads only below the lowest living player, so players may spread out: whoever falls back
    /// or takes over as host still finds the levels below them.
    /// </summary>
    internal static class WKTHostStreaming {
        /// <summary>How many levels the game keeps below the lowest player when it unloads old ones.</summary>
        public const int UNLOAD_KEEP_BELOW = 5;
        private static readonly AccessTools.FieldRef<WorldLoader, bool> s_loading = AccessTools.FieldRefAccess<WorldLoader, bool>("loading");
        private static readonly System.Func<WorldLoader, WorldLoader.BranchInfo, WorldLoader.GenerationParameters, IEnumerator> s_generateLevels =
            AccessTools.MethodDelegate<System.Func<WorldLoader, WorldLoader.BranchInfo, WorldLoader.GenerationParameters, IEnumerator>>(
                AccessTools.Method(typeof(WorldLoader), "GenerateLevels"));
        /// <summary>Levels kept loaded for remote players this frame: each one's level and its neighbours.</summary>
        private static readonly HashSet<WorldLoader.LevelInfo> s_keep = new();
        /// <summary>The level each remote player is in this frame.</summary>
        private static readonly List<WorldLoader.LevelInfo> s_remoteLevels = new(4);

        /// <summary>Works out which levels the remote players need. Call before the game's own load and unload pass.</summary>
        public static void BeforeLoadUnload(WorldLoader loader) {
            s_keep.Clear();
            s_remoteLevels.Clear();
            if (!WKTPlayerPositions.IsCoveringAllPlayers) return;
            var branch = loader.GetCurrentBranch();
            if (branch?.levelTracker == null) return;
            for (var i = 0; i < WKTPlayerPositions.RemoteCount; i++) {
                var level = FindReportedLevel(loader, branch, WKTPlayerPositions.GetRemoteId(i)) ??
                    FindClosestLevel(branch.levelTracker, WKTPlayerPositions.GetRemotePosition(i));
                if (level == null) continue;
                s_remoteLevels.Add(level);
                s_keep.Add(level);
                if (level.connectedLevels == null) continue;
                foreach (var connected in level.connectedLevels) {
                    if (connected != null)
                        s_keep.Add(connected);
                }
            }
        }

        /// <summary>
        /// The level a remote player reported being in (see <see cref="WKTPlayerRooms"/>), when it is on the host's current
        /// branch. A level on another branch (a shortcut or side branch the host is not on) is kept loaded right here, since
        /// the game only ever loads and unloads levels of the current branch. Null to fall back to the nearest level.
        /// </summary>
        private static WorldLoader.LevelInfo? FindReportedLevel(WorldLoader loader, WorldLoader.BranchInfo current, byte playerId) {
            var info = GetReportedLevel(loader, playerId, out var branch, out var level);
            if (info == null) return null;
            if (branch == current) return info;
            KeepActive(info, level!);
            return null;
        }

        /// <summary>The level a remote player reported being in, on whichever branch it is, or null when unknown here.</summary>
        private static WorldLoader.LevelInfo? GetReportedLevel(WorldLoader loader, byte playerId, out WorldLoader.BranchInfo? branch, out M_Level? level) {
            branch = null;
            level = null;
            var room = WKTPlayerRooms.Get(playerId);
            if (room == WKTPlayerRooms.UNKNOWN || !WKTContentSeeding.TryGetLevel(room, out level) || level == null) return null;
            branch = loader.GetBranchOfLevel(level);
            return branch?.GetLevelInfoFromLevel(level);
        }

        private static void KeepActive(WorldLoader.LevelInfo info, M_Level level) {
            var wasLoaded = info.loaded;
            if (!level.gameObject.activeSelf)
                level.gameObject.SetActive(true);
            info.loaded = true;
            if (wasLoaded) return;
            level.OnLevelActivate();
            WorldLoader.onLevelActivate?.Invoke(info);
        }

        /// <summary>
        /// Keeps <paramref name="info"/> loaded if a remote player needs it and the game would not already. True if handled,
        /// in which case the game's own check for it is skipped.
        /// </summary>
        public static bool TryKeepLoaded(WorldLoader.LevelInfo info, WorldLoader.LevelInfo? current) {
            if (s_keep.Count == 0 || info == current || !s_keep.Contains(info)) return false;
            // Neighbours of the local player's level are the game's to handle.
            if (current?.connectedLevels != null && current.connectedLevels.Contains(info)) return false;
            var level = info.GetLevel();
            if (level == null) return false;
            info.adjacent = true;
            KeepActive(info, level);
            return true;
        }

        /// <summary>Generates further when a remote player reached the last level, as the game does for the local player.</summary>
        public static void AfterLoadUnload(WorldLoader loader) {
            if (s_remoteLevels.Count == 0 || s_loading(loader)) return;
            var branch = loader.GetCurrentBranch();
            var tracker = branch?.levelTracker;
            if (tracker == null || tracker.Count == 0) return;
            var last = tracker[tracker.Count - 1];
            var lastLevel = last.GetLevel();
            if (!s_remoteLevels.Contains(last) || last.stopLevel || lastLevel == null || lastLevel.IsPausingGeneration()) return;
            var parameters = new WorldLoader.GenerationParameters { lastRegion = lastLevel.region, seedOffset = 1 };
            WKT.Instance.Logger.LogInfo($"A remote player reached the last level {lastLevel.name}. Continuing generation.");
            loader.UnloadPreviousLevels(UNLOAD_KEEP_BELOW);
            loader.StartCoroutine(s_generateLevels(loader, branch!, parameters));
        }

        /// <summary>
        /// Replaces the game's unloading of old levels in a synced run: unloads below the lowest living player rather than the
        /// local one, on every machine. The local player's level is the one they are at, not the branch's current level, which
        /// level scripting set off by a remote player on the host can move. False when the game's own version should run.
        /// </summary>
        public static bool TryUnloadPreviousLevels(WorldLoader loader, int offset) {
            if (!WKTPlayerPositions.IsSharedWorld) return false;
            var branch = loader.GetCurrentBranch();
            var tracker = branch?.levelTracker;
            if (tracker == null) return false;
            Span<int> indices = stackalloc int[WKTLobbyConsts.MAX_PLAYERS + 1];
            var count = 0;
            if (ENT_Player.playerObject is { dead: false } local)
                indices[count++] = IndexOf(tracker, FindClosestLevel(tracker, local.transform.position));
            for (var i = 0; i < WKTPlayerPositions.RemoteCount && count < indices.Length; i++) {
                if (WKTPlayerPositions.IsRemoteDead(i)) continue;
                var reported = GetReportedLevel(loader, WKTPlayerPositions.GetRemoteId(i), out var reportedBranch, out _);
                var level = reportedBranch == branch ? reported : FindClosestLevel(tracker, WKTPlayerPositions.GetRemotePosition(i));
                indices[count++] = IndexOf(tracker, level);
            }
            // Nobody alive here to go by (everyone dead or spectating): the game's own current level.
            if (count == 0)
                indices[count++] = tracker.IndexOf(branch!.currentLevel);
            var end = GetUnloadCount(indices.Slice(0, count), offset);
            if (end <= 0) return true;
            for (var i = 0; i < end; i++) {
                tracker[i].DestroyLevelObject();
                tracker[i].GetLevelAssetReference().UnloadLevelAsset();
            }
            tracker.RemoveRange(0, end);
            return true;
        }

        /// <summary>
        /// How many levels from the front of the route to unload: those more than <paramref name="offset"/> below the lowest
        /// of <paramref name="playerIndices"/> (each player's level index, -1 when not on the route). One player off the route
        /// unloads nothing, as their level cannot be placed. Pure.
        /// </summary>
        public static int GetUnloadCount(ReadOnlySpan<int> playerIndices, int offset) {
            var lowest = int.MaxValue;
            foreach (var index in playerIndices) {
                if (index < 0) return 0;
                lowest = Math.Min(lowest, index);
            }
            return lowest == int.MaxValue ? 0 : Math.Max(0, lowest - offset);
        }

        public static void Reset() {
            s_keep.Clear();
            s_remoteLevels.Clear();
        }

        /// <summary>Whether the loader is generating levels.</summary>
        public static bool IsLoading(WorldLoader loader) => s_loading(loader);

        private static int IndexOf(List<WorldLoader.LevelInfo> tracker, WorldLoader.LevelInfo? level) => level == null ? -1 : tracker.IndexOf(level);

        /// <summary>Like <c>WorldLoader.GetClosestLevelToPosition</c>, without allocating: the level containing or nearest to a point.</summary>
        public static WorldLoader.LevelInfo? FindClosestLevel(List<WorldLoader.LevelInfo> levels, Vector3 position) {
            WorldLoader.LevelInfo? closest = null;
            var closestSqr = float.PositiveInfinity;
            foreach (var info in levels) {
                var level = info?.GetLevel();
                if (level == null) continue;
                var local = level.transform.worldToLocalMatrix.MultiplyPoint(position);
                var bounds = level.GetLocalBounds();
                if (bounds.Contains(local)) return info;
                var sqr = bounds.SqrDistance(local);
                if (sqr >= closestSqr) continue;
                closestSqr = sqr;
                closest = info;
            }
            return closest;
        }
    }
}
