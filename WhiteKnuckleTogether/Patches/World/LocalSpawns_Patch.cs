using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using JetBrains.Annotations;
using UnityEngine;
using Object = UnityEngine.Object;

namespace WhiteKnuckleTogether {
    /// <summary>Drop objects created while a local spawn scope is open are the local player's to hand to the host.</summary>
    [WKTCriticalPatch(WKTPatchCategory.WorldSync), HarmonyPatch(typeof(Item), nameof(Item.CreateDropObject))]
    internal sealed class ItemCreateDropObject_Patch {
        [UsedImplicitly]
        private static void Postfix(Item_Object __result) => WKTWorldSync.RecordLocalSpawn(__result);
    }

    /// <summary>
    /// A thrown item the local player threw, such as a rebar, sticks where it lands as a new item. On a client the host makes
    /// it a world object everyone sees.
    /// </summary>
    [WKTCriticalPatch(WKTPatchCategory.WorldSync), HarmonyPatch(typeof(Projectile), "ParentHitItem")]
    internal sealed class ProjectileParentHitItem_Patch {
        private static readonly AccessTools.FieldRef<Projectile, GameEntity> s_sourceEntity = AccessTools.FieldRefAccess<Projectile, GameEntity>("sourceEntity");

        [UsedImplicitly]
        private static void Prefix(Projectile __instance, out bool __state) {
            var source = s_sourceEntity(__instance);
            __state = source != null && source == ENT_Player.playerObject && !WKTEnemyShots.IsCopy(__instance) &&
                WKTWorldSync.BeginLocalSpawn();
        }

        [UsedImplicitly]
        private static Exception? Finalizer(Exception? __exception, bool __state) {
            WKTWorldSync.EndLocalSpawn(__state);
            return __exception;
        }
    }

    /// <summary>
    /// Objects the local player's own hands spawn straight from a prefab: a piton falling out of the wall as an item, and
    /// spawner hand items. Each spawn is its own local spawn scope.
    /// </summary>
    [WKTCriticalPatch(WKTPatchCategory.WorldSync), HarmonyPatch]
    internal sealed class LocalPrefabSpawn_Patch {
        public static readonly MethodInfo InstantiateAtPose = FindInstantiateAtPose();
        private static readonly MethodInfo s_hook = AccessTools.Method(typeof(LocalPrefabSpawn_Patch), nameof(Instantiate));

        public static IEnumerable<MethodBase> GetTargets() {
            yield return AccessTools.Method(typeof(CL_Handhold), "FixedUpdate");
            yield return AccessTools.Method(typeof(HandItem_Spawner), nameof(HandItem_Spawner.StopUse));
        }

        [UsedImplicitly]
        private static IEnumerable<MethodBase> TargetMethods() => GetTargets();

        [UsedImplicitly]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod) {
            var result = WKTCallRewriter.RedirectCalls(instructions, InstantiateAtPose, s_hook, out var count);
            if (count == 0)
                throw new InvalidOperationException($"Found no prefab spawn to redirect in {__originalMethod.DeclaringType?.Name}.{__originalMethod.Name}.");
            return result;
        }

        /// <summary>Spawns like <c>Object.Instantiate</c>, inside a local spawn scope.</summary>
        public static GameObject Instantiate(GameObject original, Vector3 position, Quaternion rotation) {
            var began = WKTWorldSync.BeginLocalSpawn();
            try {
                var spawned = Object.Instantiate(original, position, rotation);
                WKTWorldSync.RecordLocalSpawn(spawned);
                return spawned;
            }
            finally {
                WKTWorldSync.EndLocalSpawn(began);
            }
        }

        /// <summary><c>Object.Instantiate&lt;GameObject&gt;(GameObject, Vector3, Quaternion)</c>.</summary>
        private static MethodInfo FindInstantiateAtPose() {
            foreach (var method in typeof(Object).GetMethods(BindingFlags.Public | BindingFlags.Static)) {
                if (method.Name != nameof(Object.Instantiate) || !method.IsGenericMethodDefinition) continue;
                var parameters = method.GetParameters();
                if (parameters.Length == 3 && parameters[0].ParameterType.IsGenericParameter &&
                    parameters[1].ParameterType == typeof(Vector3) && parameters[2].ParameterType == typeof(Quaternion))
                    return method.MakeGenericMethod(typeof(GameObject));
            }
            throw new MissingMethodException(nameof(Object), "Instantiate<T>(T, Vector3, Quaternion)");
        }
    }
}
