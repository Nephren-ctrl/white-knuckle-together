using System;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// Makes level contents roll the same on every machine of a multiplayer run. Each generated level's components are keyed
    /// from the run seed, the level's branch and its place in that branch, and the methods that decide contents (see
    /// <see cref="WKTContentRandomTargets"/>) each roll from their component's own stream. Only active during a multiplayer
    /// run; singleplayer never begins it.
    /// </summary>
    internal static class WKTContentSeeding {
        private const string c_run_salt = "WKT.ContentSeeding";
        /// <summary>How far up an unkeyed component looks for a keyed ancestor.</summary>
        private const int c_max_ancestor_depth = 16;
        private static readonly List<MonoBehaviour> s_behaviours = new(1024);
        /// <summary>Components with no keyed ancestor, such as the player and managers. Checked once, then skipped cheaply.</summary>
        private static readonly HashSet<int> s_unkeyable = new();
        /// <summary>The components keyed for each level, by the level's GameObject instance id, to forget them on unload.</summary>
        private static readonly Dictionary<int, int[]> s_levelComponents = new();
        /// <summary>Each keyed level's key, by the level's GameObject instance id.</summary>
        private static readonly Dictionary<int, ulong> s_levelKeys = new();
        private static readonly Dictionary<ulong, M_Level> s_levelsByKey = new();
        /// <summary>How many generations each branch has started this run, by branch id.</summary>
        private static readonly Dictionary<string, int> s_generationCounts = new();
        private static WKTRandomStreams<Random.State>? s_streams;
        private static ulong s_runKey;

        public static bool IsActive => s_streams != null;

        /// <summary>A level was keyed: its contents can now be told apart between machines. Passes the level and its key.</summary>
        public static event Action<M_Level, ulong>? LevelKeyed;
        /// <summary>A keyed level is about to be destroyed.</summary>
        public static event Action<M_Level>? LevelForgetting;

        /// <summary>Starts keying for a run with <paramref name="runSeed"/>. Does nothing if already keying that run.</summary>
        public static void EnsureBegun(int runSeed) {
            var runKey = WKTStableHash.Mix(WKTStableHash.Of(c_run_salt), (uint)runSeed);
            if (s_streams != null && s_runKey == runKey) return;
            End();
            s_streams = new WKTRandomStreams<Random.State>(UnityRandomStateAccess.Instance, CreateState);
            s_runKey = runKey;
            WKT.Instance.Logger.LogInfo($"Content seeding started for seed {runSeed}.");
        }

        public static void End() {
            if (s_streams == null) return;
            if (s_streams.IsInScope) {
                WKT.Instance.Logger.LogError("Content seeding ended while a stream was entered. Its state is lost.");
                return;
            }
            s_streams.Clear();
            s_streams = null;
            WKTBranchIds.Clear();
            s_levelComponents.Clear();
            s_levelKeys.Clear();
            s_levelsByKey.Clear();
            s_generationCounts.Clear();
            s_unkeyable.Clear();
        }

        /// <summary>
        /// Swaps in <paramref name="component"/>'s stream for the method <paramref name="methodSalt"/>. A component that was
        /// never keyed is keyed now: from the keyed method creating it, so spawned objects roll the same too, or else from
        /// its nearest keyed ancestor and its path below it, for objects spawned inactive and enabled later. Returns an
        /// inactive token, changing nothing, for every other component and outside multiplayer runs.
        /// </summary>
        public static WKTRandomStreams<Random.State>.Token Enter(MonoBehaviour component, ulong methodSalt) {
            var streams = s_streams;
            if (streams == null) return default;
            var id = component.GetInstanceID();
            if (!streams.TryGetKey(id, out var key)) {
                if (streams.IsInScope)
                    KeyHierarchy(component.transform, streams.NextSpawnKey(), streams, null);
                else if (s_unkeyable.Contains(id) || !TryKeyFromAncestor(component, streams)) {
                    s_unkeyable.Add(id);
                    return default;
                }
                if (!streams.TryGetKey(id, out key)) return default;
            }
            return streams.Enter(key, methodSalt);
        }

        /// <summary>
        /// Keys <paramref name="component"/>'s hierarchy from the nearest ancestor with a keyed component, mixed with the
        /// names on the way down to it and each one's place among the siblings of the same name, which match on every machine
        /// that built the same hierarchy. Runtime spawns and other children coming and going there leave them alone.
        /// </summary>
        private static bool TryKeyFromAncestor(MonoBehaviour component, WKTRandomStreams<Random.State> streams) {
            var path = WKTStableHash.Of(null);
            var current = component.transform;
            for (var depth = 0; depth < c_max_ancestor_depth; depth++) {
                path = WKTStableHash.Mix(WKTStableHash.Mix(path, WKTStableHash.Of(current.name)), (uint)GetSameNameIndex(current));
                var parent = current.parent;
                if (parent == null) return false;
                if (TryGetAnyKey(parent, streams, out var ancestorKey)) {
                    KeyHierarchy(component.transform, WKTStableHash.Mix(ancestorKey, path), streams, null);
                    return true;
                }
                current = parent;
            }
            return false;
        }

        /// <summary>How many earlier siblings of <paramref name="transform"/> have its name.</summary>
        private static int GetSameNameIndex(Transform transform) {
            var parent = transform.parent;
            if (parent == null) return 0;
            var name = transform.name;
            var index = 0;
            for (var i = 0; i < parent.childCount; i++) {
                var sibling = parent.GetChild(i);
                if (sibling == transform) break;
                if (sibling.name == name)
                    index++;
            }
            return index;
        }

        private static bool TryGetAnyKey(Transform transform, WKTRandomStreams<Random.State> streams, out ulong key) {
            transform.GetComponents(s_behaviours);
            try {
                foreach (var behaviour in s_behaviours) {
                    if (behaviour != null && streams.TryGetKey(behaviour.GetInstanceID(), out key)) return true;
                }
            }
            finally {
                s_behaviours.Clear();
            }
            key = 0;
            return false;
        }

        /// <summary>
        /// Counts a generation of <paramref name="branchId"/> and gives the <see cref="WorldLoader.seed"/> it should start
        /// from, which it then moves forward by <paramref name="seedOffset"/> itself. False for the very first generation of
        /// the run, which uses the run's preset seed as the game does.
        /// </summary>
        public static bool TryGetGenerationSeed(string? branchId, int seedOffset, out int seed) {
            seed = 0;
            if (s_streams == null) return false;
            var key = branchId ?? string.Empty;
            s_generationCounts.TryGetValue(key, out var count);
            s_generationCounts[key] = count + 1;
            if (s_generationCounts.Count == 1 && count == 0) return false;
            seed = GetGenerationSeed(s_runKey, key, count) - seedOffset;
            return true;
        }

        /// <summary>A world seed in the range the game uses, from the run, a branch and that branch's generation count.</summary>
        public static int GetGenerationSeed(ulong runKey, string branchId, int generation) {
            var key = WKTStableHash.Mix(WKTStableHash.Mix(runKey, WKTStableHash.Of(branchId)), (uint)generation);
            return 1 + (int)(key % (WKTRunStart.MAX_SEED - 2));
        }

        /// <summary>Swaps in the run-wide stream for <paramref name="salt"/>, for rolls made by code that belongs to no level.</summary>
        public static WKTRandomStreams<Random.State>.Token EnterRunStream(ulong salt) => s_streams?.Enter(s_runKey, salt) ?? default;

        /// <summary>
        /// Swaps in the stream of the keyed level <paramref name="level"/> for <paramref name="salt"/>, for rolls that belong to
        /// a place rather than to a component, so every player rolls the same there. Inactive for an unkeyed level.
        /// </summary>
        public static WKTRandomStreams<Random.State>.Token EnterLevelStream(M_Level? level, ulong salt) =>
            s_streams != null && level != null && TryGetLevelKey(level, out var levelKey) ? s_streams.Enter(levelKey, salt) : default;

        public static void Exit(in WKTRandomStreams<Random.State>.Token token) => s_streams?.Exit(token);

        /// <summary>Keys every level of <paramref name="branch"/> that is not keyed yet. Call right after each generation step.</summary>
        public static void KeyNewLevels(WorldLoader.BranchInfo branch) {
            var streams = s_streams;
            if (streams == null || branch.levelTracker == null) return;
            // New levels are appended, so scanning back from the end stops at the first one already keyed.
            for (var i = branch.levelTracker.Count - 1; i >= 0; i--) {
                var info = branch.levelTracker[i];
                var level = info?.GetLevel();
                if (info == null || level == null) continue;
                if (s_levelComponents.ContainsKey(level.gameObject.GetInstanceID())) break;
                KeyLevel(branch, info, level, streams);
            }
        }

        /// <summary>The key of a component, if it was keyed. Every machine that built the same component gives it the same key.</summary>
        public static bool TryGetKey(Component component, out ulong key) {
            key = 0;
            return s_streams != null && s_streams.TryGetKey(component.GetInstanceID(), out key);
        }

        /// <summary>The key of a keyed level, the same on every machine.</summary>
        public static bool TryGetLevelKey(M_Level level, out ulong key) => s_levelKeys.TryGetValue(level.gameObject.GetInstanceID(), out key);

        /// <summary>Adds every keyed level this machine has, with its key, to <paramref name="levels"/>.</summary>
        public static void GetLevels(List<KeyValuePair<ulong, M_Level>> levels) {
            foreach (var pair in s_levelsByKey) {
                if (pair.Value != null)
                    levels.Add(pair);
            }
        }

        /// <summary>The level with a key, if this machine has it.</summary>
        public static bool TryGetLevel(ulong key, out M_Level? level) {
            if (s_levelsByKey.TryGetValue(key, out level) && level != null) return true;
            level = null;
            return false;
        }

        /// <summary>
        /// The component of type <typeparamref name="T"/> keyed <paramref name="key"/> in the level keyed <paramref name="levelKey"/>,
        /// if this machine has it: a level object another machine named. Walks the level, so only for rare messages.
        /// </summary>
        public static bool TryFind<T>(ulong levelKey, ulong key, List<T> scratch, out T? found) where T : Component {
            found = null;
            if (!TryGetLevel(levelKey, out var level)) return false;
            level!.GetComponentsInChildren(true, scratch);
            foreach (var component in scratch) {
                if (component != null && TryGetKey(component, out var componentKey) && componentKey == key) {
                    found = component;
                    break;
                }
            }
            scratch.Clear();
            return found != null;
        }

        /// <summary>Forgets the keys of a level about to be destroyed.</summary>
        public static void ForgetLevel(M_Level? level) {
            var streams = s_streams;
            if (streams == null || level == null) return;
            if (s_levelKeys.ContainsKey(level.gameObject.GetInstanceID()))
                LevelForgetting?.Invoke(level);
            if (s_levelKeys.Remove(level.gameObject.GetInstanceID(), out var levelKey))
                s_levelsByKey.Remove(levelKey);
            if (!s_levelComponents.Remove(level.gameObject.GetInstanceID(), out var ids)) return;
            foreach (var id in ids)
                streams.RemoveKey(id);
        }

        private static void KeyLevel(WorldLoader.BranchInfo branch, WorldLoader.LevelInfo info, M_Level level, WKTRandomStreams<Random.State> streams) {
            var index = WKTLevelAccess.GetGenerationIndex(branch, info);
            if (index < 0) {
                WKT.Instance.Logger.LogError($"Cannot key level {level.name}: it is missing from branch {branch.id}. Its contents may differ between players.");
                // Still recorded, so it is not retried every step.
                s_levelComponents[level.gameObject.GetInstanceID()] = Array.Empty<int>();
                return;
            }
            var levelKey = WKTStableHash.Mix(WKTStableHash.Mix(s_runKey, WKTStableHash.Of(branch.id)), (uint)index);
            var ids = new List<int>(256);
            var fingerprint = KeyHierarchy(level.transform, levelKey, streams, ids);
            s_levelComponents[level.gameObject.GetInstanceID()] = ids.ToArray();
            s_levelKeys[level.gameObject.GetInstanceID()] = levelKey;
            s_levelsByKey[levelKey] = level;
            // Compare these lines between players' logs: a different fingerprint means the levels themselves differ.
            WKT.Instance.Logger.LogInfo($"Keyed level {level.name} ({branch.id} #{index}): {ids.Count} components, fingerprint {fingerprint:X16}.");
            LevelKeyed?.Invoke(level, levelKey);
        }

        /// <summary>
        /// Keys every not yet keyed MonoBehaviour under <paramref name="root"/> in hierarchy order, which is the same on every
        /// machine. Returns a fingerprint of the component types, to spot levels that differ between machines.
        /// </summary>
        private static ulong KeyHierarchy(Transform root, ulong rootKey, WKTRandomStreams<Random.State> streams, List<int>? keyedIds) {
            root.GetComponentsInChildren(true, s_behaviours);
            var fingerprint = rootKey;
            for (var i = 0; i < s_behaviours.Count; i++) {
                var behaviour = s_behaviours[i];
                if (behaviour == null) continue;
                var id = behaviour.GetInstanceID();
                if (streams.TryGetKey(id, out _)) continue;
                streams.SetKey(id, WKTStableHash.Mix(rootKey, (uint)i));
                keyedIds?.Add(id);
                fingerprint = WKTStableHash.Mix(fingerprint, WKTStableHash.Of(behaviour.GetType().Name));
            }
            s_behaviours.Clear();
            return fingerprint;
        }

        /// <summary>The generator state Unity starts from for <paramref name="seed"/>, leaving the global state as it was.</summary>
        private static Random.State CreateState(int seed) {
            var outer = Random.state;
            Random.InitState(seed);
            var state = Random.state;
            Random.state = outer;
            return state;
        }
    }
}
