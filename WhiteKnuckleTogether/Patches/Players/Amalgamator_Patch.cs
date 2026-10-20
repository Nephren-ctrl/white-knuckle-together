using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using JetBrains.Annotations;
using UnityEngine;
using Object = UnityEngine.Object;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// The amalgamator (<c>HandItem_Crafter</c>) in a synced co-op run. What it pulls toward the player is theirs to simulate
    /// while it does, as if grabbed, so everyone sees it fly; once it lets go, or crafts, the objects go back. What it crafts
    /// is a local spawn, shared with everyone like a spawner item's.
    /// </summary>
    [HarmonyPatch(typeof(HandItem_Crafter), nameof(HandItem_Crafter.Update))]
    internal sealed class AmalgamatorPull_Patch {
        private static readonly AccessTools.FieldRef<HandItem_Crafter, List<GameEntity>> s_pulled = AccessTools.FieldRefAccess<HandItem_Crafter, List<GameEntity>>("pickedupEntities");
        private static readonly HashSet<WKTNetIdentity> s_held = new();
        private static readonly List<WKTNetIdentity> s_scratch = new();

        [UsedImplicitly]
        private static void Postfix(HandItem_Crafter __instance) {
            var sync = WKTWorldSync.Instance;
            if (sync == null) return;
            var pulled = s_pulled(__instance);
            foreach (var entity in pulled) {
                if (entity == null || !entity.TryGetComponent<WKTNetIdentity>(out var identity) || identity.Handle == 0 || !s_held.Add(identity)) continue;
                sync.Authority.OnLocalGrab(identity, 0, Vector3.zero);
            }
            if (s_held.Count == 0) return;
            s_scratch.Clear();
            foreach (var identity in s_held) {
                if (identity == null || identity.Entity == null || !pulled.Contains(identity.Entity))
                    s_scratch.Add(identity!);
            }
            foreach (var identity in s_scratch) {
                s_held.Remove(identity);
                if (identity != null && identity.Handle != 0)
                    sync.Authority.OnLocalRelease(identity);
            }
            s_scratch.Clear();
        }
    }

    /// <summary>What the amalgamator crafts is shared like any other local spawn.</summary>
    [WKTCriticalPatch(WKTPatchCategory.WorldSync), HarmonyPatch(typeof(HandItem_Crafter), "TryCraft")]
    internal sealed class AmalgamatorCraft_Patch {
        public static readonly MethodInfo InstantiateEntity = FindInstantiate();
        private static readonly MethodInfo s_hook = AccessTools.Method(typeof(AmalgamatorCraft_Patch), nameof(Instantiate));

        /// <summary>Set while the amalgamator crafts, when the items it uses up are the host's to remove.</summary>
        public static bool IsCrafting { get; private set; }

        [UsedImplicitly]
        private static void Prefix() => IsCrafting = WKTWorldSync.Instance != null;

        [UsedImplicitly]
        private static void Finalizer() => IsCrafting = false;

        [UsedImplicitly]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) {
            var result = WKTCallRewriter.RedirectCalls(instructions, InstantiateEntity, s_hook, out var count);
            return count > 0 ? result : throw new InvalidOperationException("Found no crafted spawn to redirect in HandItem_Crafter.TryCraft.");
        }

        /// <summary>Spawns like <c>Object.Instantiate</c>, inside a local spawn scope.</summary>
        public static GameEntity Instantiate(GameEntity original, Vector3 position, Quaternion rotation, Transform parent) {
            var began = WKTWorldSync.BeginLocalSpawn();
            try {
                var spawned = Object.Instantiate(original, position, rotation, parent);
                WKTWorldSync.RecordLocalSpawn(spawned.gameObject);
                return spawned;
            }
            finally {
                WKTWorldSync.EndLocalSpawn(began);
            }
        }

        /// <summary><c>Object.Instantiate&lt;GameEntity&gt;(GameEntity, Vector3, Quaternion, Transform)</c>.</summary>
        private static MethodInfo FindInstantiate() {
            foreach (var method in typeof(Object).GetMethods(BindingFlags.Public | BindingFlags.Static)) {
                if (method.Name != nameof(Object.Instantiate) || !method.IsGenericMethodDefinition) continue;
                var parameters = method.GetParameters();
                if (parameters.Length == 4 && parameters[0].ParameterType.IsGenericParameter && parameters[1].ParameterType == typeof(Vector3) &&
                    parameters[2].ParameterType == typeof(Quaternion) && parameters[3].ParameterType == typeof(Transform))
                    return method.MakeGenericMethod(typeof(GameEntity));
            }
            throw new MissingMethodException(nameof(Object), "Instantiate<T>(T, Vector3, Quaternion, Transform)");
        }
    }
}
