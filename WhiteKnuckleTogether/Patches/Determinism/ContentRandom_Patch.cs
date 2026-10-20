using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using JetBrains.Annotations;
using UnityEngine;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// Every method that decides what a level contains rolls from its own component's random stream during a multiplayer
    /// run (see <see cref="WKTContentSeeding"/>), so every player gets the same enemies, items and props. Outside one the
    /// prefix only reads one static and returns.
    /// </summary>
    [WKTCriticalPatch(WKTPatchCategory.RunSync), HarmonyPatch]
    internal sealed class ContentRandom_Patch {
        private static readonly Dictionary<MethodBase, ulong> s_salts = new();
        /// <summary>For each coroutine state machine, the fields that lead to the MonoBehaviour that started it.</summary>
        private static readonly Dictionary<Type, FieldInfo[]> s_ownerChains = new();

        [UsedImplicitly]
        private static IEnumerable<MethodBase> TargetMethods() {
            var targets = WKTContentRandomTargets.Find(typeof(GameEntity).Assembly);
            foreach (var target in targets) {
                s_salts[target] = WKTStableHash.Of(WKTContentRandomTargets.GetId(target));
                if (target.DeclaringType != null && !typeof(MonoBehaviour).IsAssignableFrom(target.DeclaringType) &&
                    WKTContentRandomTargets.TryGetOwnerChain(target.DeclaringType, out var chain))
                    s_ownerChains[target.DeclaringType] = chain;
            }
            return targets;
        }

        [UsedImplicitly]
        private static void Prefix(object __instance, MethodBase __originalMethod, out WKTRandomStreams<UnityEngine.Random.State>.Token __state) {
            __state = default;
            if (!WKTContentSeeding.IsActive || !s_salts.TryGetValue(__originalMethod, out var salt)) return;
            var owner = __instance as MonoBehaviour ?? GetCoroutineOwner(__instance);
            if (owner != null)
                __state = WKTContentSeeding.Enter(owner, salt);
        }

        /// <summary>Runs even when the method throws, so the global random state is always restored.</summary>
        [UsedImplicitly]
        private static void Finalizer(WKTRandomStreams<UnityEngine.Random.State>.Token __state) => WKTContentSeeding.Exit(__state);

        private static MonoBehaviour? GetCoroutineOwner(object stateMachine) {
            if (!s_ownerChains.TryGetValue(stateMachine.GetType(), out var chain)) return null;
            var current = stateMachine;
            foreach (var field in chain) {
                current = field.GetValue(current);
                if (current == null) return null;
            }
            return current as MonoBehaviour;
        }
    }
}
