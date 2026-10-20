using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Random = UnityEngine.Random;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// Finds the game methods whose random rolls decide what a level contains: the <c>Awake</c>, <c>OnEnable</c> and
    /// <c>Start</c> of every MonoBehaviour, every method named <c>Spawn...</c>, and the coroutines those start, keeping only
    /// the ones that reach <see cref="Random"/> within a few calls. <see cref="GameEntity"/>'s <c>OnEnable</c> is always
    /// included, so every entity created by a keyed spawner is keyed too. Pure reflection, so the set is testable.
    /// </summary>
    internal static class WKTContentRandomTargets {
        public const string COROUTINE_OWNER_FIELD = "<>4__this";
        /// <summary>How many calls deep to look for a random roll. Spawn chances roll three calls below Start.</summary>
        private const int c_max_call_depth = 5;
        /// <summary>How many closures a coroutine may sit inside.</summary>
        private const int c_max_closure_depth = 2;
        private const BindingFlags c_declared = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public |
            BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        private static readonly string[] s_lifecycleNames = { "Awake", "OnEnable", "Start" };
        private const string c_spawn_prefix = "Spawn";

        /// <summary>Every target in <paramref name="gameAssembly"/>, in a stable order.</summary>
        public static List<MethodBase> Find(Assembly gameAssembly) {
            var scan = new RandomUseScan(gameAssembly);
            var targets = new List<MethodBase>();
            foreach (var type in WKTGameTypes.Load(gameAssembly)) {
                if (!IsPatchableBehaviour(type)) continue;
                foreach (var method in type.GetMethods(c_declared)) {
                    if (IsContentMethodName(method.Name) && HasBody(method) && scan.UsesRandom(method))
                        targets.Add(method);
                }
                AddCoroutines(type, type, 0, scan, targets);
            }
            var entityOnEnable = typeof(GameEntity).GetMethod("OnEnable", c_declared);
            if (entityOnEnable != null && !targets.Contains(entityOnEnable))
                targets.Add(entityOnEnable);
            targets.Sort((a, b) => string.CompareOrdinal(GetId(a), GetId(b)));
            return targets;
        }

        /// <summary>
        /// The <c>&lt;&gt;4__this</c> fields leading from a coroutine's state machine to the MonoBehaviour that started it. A
        /// coroutine declared as a local function that captures variables is reached through its closure, so it takes two.
        /// False when the coroutine does not capture the MonoBehaviour at all.
        /// </summary>
        public static bool TryGetOwnerChain(Type stateMachine, out FieldInfo[] chain) {
            chain = Array.Empty<FieldInfo>();
            var fields = new List<FieldInfo>(2);
            for (var current = stateMachine; fields.Count <= c_max_closure_depth;) {
                var field = current.GetField(COROUTINE_OWNER_FIELD, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field == null) return false;
                fields.Add(field);
                if (typeof(MonoBehaviour).IsAssignableFrom(field.FieldType)) {
                    chain = fields.ToArray();
                    return true;
                }
                current = field.FieldType;
            }
            return false;
        }

        /// <summary>A name for <paramref name="method"/> that is the same on every machine running the same game version.</summary>
        public static string GetId(MethodBase method) {
            var parameters = method.GetParameters();
            var parameterTypes = new string[parameters.Length];
            for (var i = 0; i < parameters.Length; i++)
                parameterTypes[i] = parameters[i].ParameterType.Name;
            return $"{method.DeclaringType!.FullName}.{method.Name}({string.Join(",", parameterTypes)})";
        }

        /// <summary>
        /// Whether <paramref name="type"/> is a compiler-generated coroutine, and the name of the method that starts it. For
        /// a coroutine declared as a local function (<c>&lt;&lt;Spawn&gt;g__Wait|3_0&gt;d</c>) that is the enclosing method.
        /// </summary>
        public static bool TryGetCoroutineMethodName(Type type, out string methodName) {
            methodName = string.Empty;
            var name = type.Name;
            if (!typeof(IEnumerator).IsAssignableFrom(type)) return false;
            var start = 0;
            while (start < name.Length && name[start] == '<')
                start++;
            if (start == 0) return false;
            var end = name.IndexOf('>', start);
            if (end <= start) return false;
            methodName = name.Substring(start, end - start);
            return true;
        }

        /// <summary>Adds the coroutines of <paramref name="owner"/> found in <paramref name="container"/> and the closures nested in it.</summary>
        private static void AddCoroutines(Type owner, Type container, int depth, RandomUseScan scan, List<MethodBase> targets) {
            foreach (var nested in container.GetNestedTypes(BindingFlags.NonPublic | BindingFlags.Public)) {
                if (depth < c_max_closure_depth)
                    AddCoroutines(owner, nested, depth + 1, scan, targets);
                if (!TryGetCoroutineMethodName(nested, out var kickoffName) || !IsContentMethodName(kickoffName)) continue;
                if (!TryGetOwnerChain(nested, out var chain) || !chain[chain.Length - 1].FieldType.IsAssignableFrom(owner)) continue;
                var moveNext = nested.GetMethod(nameof(IEnumerator.MoveNext), BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (moveNext != null && scan.UsesRandom(moveNext))
                    targets.Add(moveNext);
            }
        }

        private static bool IsContentMethodName(string name) =>
            Array.IndexOf(s_lifecycleNames, name) >= 0 || name.StartsWith(c_spawn_prefix, StringComparison.Ordinal);

        private static bool IsPatchableBehaviour(Type type) =>
            type.IsClass && !type.ContainsGenericParameters && typeof(MonoBehaviour).IsAssignableFrom(type);

        private static bool HasBody(MethodInfo method) => !method.IsAbstract && !method.IsStatic && method.GetMethodBody() != null;

        /// <summary>
        /// Whether a method draws from <see cref="Random"/>, directly or through calls into the game's own code. Results
        /// are remembered with the depth they were searched to, so the answer never depends on scan order.
        /// </summary>
        private sealed class RandomUseScan {
            private readonly Assembly m_gameAssembly;
            private readonly HashSet<MethodBase> m_usesRandom = new();
            /// <summary>Methods known not to reach Random within the stored number of calls.</summary>
            private readonly Dictionary<MethodBase, int> m_cleanToDepth = new();
            private readonly HashSet<MethodBase> m_inProgress = new();

            public RandomUseScan(Assembly gameAssembly) => m_gameAssembly = gameAssembly;

            public bool UsesRandom(MethodBase method) => UsesRandom(method, c_max_call_depth, out _);

            /// <param name="method">The method to scan.</param>
            /// <param name="remainingDepth">How many more calls deep to follow.</param>
            /// <param name="hitCycle">Whether the answer was cut short by recursion, so a false must not be remembered.</param>
            private bool UsesRandom(MethodBase method, int remainingDepth, out bool hitCycle) {
                hitCycle = false;
                if (m_usesRandom.Contains(method)) return true;
                if (m_cleanToDepth.TryGetValue(method, out var cleanDepth) && cleanDepth >= remainingDepth) return false;
                if (!m_inProgress.Add(method)) {
                    hitCycle = true;
                    return false;
                }
                var result = ScanBody(method, remainingDepth, ref hitCycle);
                m_inProgress.Remove(method);
                if (result)
                    m_usesRandom.Add(method);
                else if (!hitCycle)
                    m_cleanToDepth[method] = remainingDepth;
                return result;
            }

            private bool ScanBody(MethodBase method, int remainingDepth, ref bool hitCycle) {
                var callees = new List<MethodBase>();
                if (!WKTILCallReader.TryGetCallees(method, callees)) return false;
                foreach (var callee in callees) {
                    var declaringType = callee.DeclaringType;
                    if (declaringType == typeof(Random)) return true;
                    if (remainingDepth == 0 || declaringType == null || declaringType.Assembly != m_gameAssembly || callee.IsAbstract) continue;
                    if (UsesRandom(callee, remainingDepth - 1, out var calleeHitCycle)) return true;
                    hitCycle |= calleeHitCycle;
                }
                return false;
            }
        }
    }
}
