using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using JetBrains.Annotations;
using UnityEngine;

namespace WhiteKnuckleTogether {
    /// <summary>Finds the GameEntity methods the world sync patches, across every entity type.</summary>
    internal static class WorldSyncTargets {
        private const BindingFlags c_declared = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        /// <summary>
        /// Every declaration of a method named one of <paramref name="names"/> on <see cref="GameEntity"/> and its subclasses,
        /// except the player's, whose parameters are <paramref name="parameterTypes"/> when given.
        /// </summary>
        public static List<MethodBase> Find(string[] names, Type[]? parameterTypes = null) {
            var targets = new List<MethodBase>();
            foreach (var type in WKTGameTypes.All) {
                if (!typeof(GameEntity).IsAssignableFrom(type) || typeof(ENT_Player).IsAssignableFrom(type) || type.ContainsGenericParameters) continue;
                foreach (var method in type.GetMethods(c_declared)) {
                    if (method.IsAbstract || Array.IndexOf(names, method.Name) < 0) continue;
                    if (parameterTypes != null && !HasParameters(method, parameterTypes)) continue;
                    targets.Add(method);
                }
            }
            targets.Sort((a, b) => string.CompareOrdinal($"{a.DeclaringType!.FullName}.{a.Name}", $"{b.DeclaringType!.FullName}.{b.Name}"));
            return targets;
        }

        private static bool HasParameters(MethodInfo method, Type[] types) {
            var parameters = method.GetParameters();
            if (parameters.Length != types.Length) return false;
            for (var i = 0; i < types.Length; i++) {
                if (parameters[i].ParameterType != types[i]) return false;
            }
            return true;
        }
    }

    /// <summary>Every live GameEntity and every started item is a world object of the synced co-op run.</summary>
    [WKTCriticalPatch(WKTPatchCategory.WorldSync), HarmonyPatch]
    internal sealed class WorldSyncRegistration_Patch {
        public static IEnumerable<MethodBase> GetTargets() {
            yield return AccessTools.Method(typeof(GameEntity), "OnEnable");
            yield return AccessTools.Method(typeof(Item_Object), nameof(Item_Object.Start));
        }

        [UsedImplicitly]
        private static IEnumerable<MethodBase> TargetMethods() => GetTargets();

        [UsedImplicitly]
        private static void Postfix(Component __instance) => WKTWorldSync.Instance?.Register(__instance);
    }

    /// <summary>
    /// Entities whose own <c>OnEnable</c> hides <see cref="GameEntity.OnEnable"/> instead of overriding it, so the base one,
    /// and with it <see cref="WorldSyncRegistration_Patch"/>, never runs for them: the Teeth. Without this every machine ran a
    /// Teeth of its own, hunting its own player. Registered before their own <c>OnEnable</c>, which takes the Teeth out of its
    /// level, so it is keyed by its place in the level like on every other machine.
    /// </summary>
    [WKTCriticalPatch(WKTPatchCategory.WorldSync), HarmonyPatch]
    internal sealed class HiddenOnEnableRegistration_Patch {
        private const BindingFlags c_declared = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        /// <summary>Kept apart on every machine on purpose: each player has their own engraved door (see <see cref="PuppetTick_Patch"/>).</summary>
        private static readonly Type[] s_local = { typeof(DEN_EngravedDoor) };

        public static List<MethodBase> GetTargets() {
            var targets = new List<MethodBase>();
            foreach (var type in WKTGameTypes.All) {
                if (type == typeof(GameEntity) || !typeof(GameEntity).IsAssignableFrom(type) || typeof(ENT_Player).IsAssignableFrom(type) ||
                    type.ContainsGenericParameters || Array.IndexOf(s_local, type) >= 0) continue;
                var method = type.GetMethod("OnEnable", c_declared, null, Type.EmptyTypes, null);
                // An override reaches the base through its chain of overrides, or not at all if it skips base.OnEnable on
                // purpose; only a method that hides it is a separate one.
                if (method != null && !method.IsAbstract && method.GetBaseDefinition() == method && !method.IsVirtual)
                    targets.Add(method);
            }
            targets.Sort((a, b) => string.CompareOrdinal(a.DeclaringType!.FullName, b.DeclaringType!.FullName));
            return targets;
        }

        [UsedImplicitly]
        private static IEnumerable<MethodBase> TargetMethods() => GetTargets();

        [UsedImplicitly]
        private static void Prefix(Component __instance) => WKTWorldSync.Instance?.Register(__instance);
    }

    /// <summary>
    /// Puppets do not think, move or tick on their own: the machine simulating them drives them. The ticks in
    /// <see cref="AllowedOnPuppets"/> still run, since they only animate what the host decided and check hits against the
    /// local player.
    /// </summary>
    [WKTCriticalPatch(WKTPatchCategory.WorldSync), HarmonyPatch]
    internal sealed class PuppetTick_Patch {
        public static readonly string[] Names = { "Update", "FixedUpdate", "LateUpdate", "TickUpdate" };
        /// <summary>
        /// A barnacle's tick moves its tongues, whose own AI is replaced on puppets (see <see cref="BarnacleTongueAI_Patch"/>). Only
        /// barnacles with basic tongues, which clients replay from the host.
        /// </summary>
        public static readonly MethodBase BarnacleTick = AccessTools.Method(typeof(DEN_Barnacle), "LateUpdate");
        /// <summary>
        /// Ticks every machine runs for its own player: the Teeth's hands and the Hunter's and Face's overlays on the screen
        /// follow the local camera (see <see cref="WKTViewerCreatures"/>), and the engraved door and pseudopod act on each player
        /// from their own machine, as in singleplayer.
        /// </summary>
        public static readonly MethodBase[] AllowedOnPuppets = {
            BarnacleTick, AccessTools.Method(typeof(DEN_Teeth), "LateUpdate"), AccessTools.Method(typeof(DEN_Hunter), "LateUpdate"),
            AccessTools.Method(typeof(DEN_Face), "LateUpdate"), AccessTools.Method(typeof(DEN_EngravedDoor), "Update"), AccessTools.Method(typeof(DEN_Pseudopod), "Update")
        };

        [UsedImplicitly]
        private static IEnumerable<MethodBase> TargetMethods() => WorldSyncTargets.Find(Names);

        [UsedImplicitly]
        private static bool Prefix(GameEntity __instance, MethodBase __originalMethod) =>
            !WKTWorldSync.IsPuppet(__instance) ||
            (Array.IndexOf(AllowedOnPuppets, __originalMethod) >= 0 && (__originalMethod != BarnacleTick || WKTAttackModules.HasReplicatedTongues(__instance)));
    }

    /// <summary>A hit on an object another machine simulates goes to that machine instead, which applies it.</summary>
    [WKTCriticalPatch(WKTPatchCategory.WorldSync), HarmonyPatch]
    internal sealed class PuppetDamage_Patch {
        public static readonly Type[] Parameters = { typeof(Damageable.DamageInfo) };

        [UsedImplicitly]
        private static IEnumerable<MethodBase> TargetMethods() => WorldSyncTargets.Find(new[] { nameof(GameEntity.Damage) }, Parameters);

        [UsedImplicitly]
        private static bool Prefix(GameEntity __instance, Damageable.DamageInfo __0, ref bool __result, out byte __state) {
            __state = WKTDamageCause.Current;
            // A puppet checking its hits against the local player hits nothing else, nor does a client's copy of an effect the
            // host runs too. The player's own Damage calls this base one, which must still count.
            if ((WKTPuppetHitScope.IsOpen || WKTSharedEffectScope.SkipsEntities(WKTWorldSync.Instance?.IsHost ?? true)) && __instance is not ENT_Player) {
                __result = false;
                return false;
            }
            if (!WKTWorldSync.TryForwardDamage(__instance, __0)) {
                // Applied here: what it sets off is for the player who dealt it.
                if (WKTWorldSync.Instance != null) {
                    WKTDamageCause.Enter(WKTWorldSync.CauseOf(__0));
                    WKTWorldSync.OnDamagedHere(__instance, __0);
                }
                return true;
            }
            __result = false;
            return false;
        }

        [UsedImplicitly]
        private static void Finalizer(byte __state) => WKTDamageCause.Exit(__state);
    }

    /// <summary>An entity gibbing on the host gibs on every client; a client's own gib of a host object is never replaced.</summary>
    [WKTCriticalPatch(WKTPatchCategory.WorldSync), HarmonyPatch]
    internal sealed class EntityGib_Patch {
        [UsedImplicitly]
        private static IEnumerable<MethodBase> TargetMethods() => WorldSyncTargets.Find(new[] { nameof(GameEntity.Gib) }, Type.EmptyTypes);

        [UsedImplicitly]
        private static void Prefix(GameEntity __instance) {
            if (!WKTWorldSync.AllowHostKill)
                WKTWorldSync.Instance?.OnLocalGib(__instance);
        }
    }

    /// <summary>
    /// A puppet only dies when the host says so. The host tells everyone when its objects die, and a client simulating an
    /// object claims its kill from the host.
    /// </summary>
    [WKTCriticalPatch(WKTPatchCategory.WorldSync), HarmonyPatch]
    internal sealed class EntityKill_Patch {
        public static readonly Type[] Parameters = { typeof(string), typeof(Damageable.DamageInfo) };

        [UsedImplicitly]
        private static IEnumerable<MethodBase> TargetMethods() => WorldSyncTargets.Find(new[] { nameof(GameEntity.Kill) }, Parameters);

        [UsedImplicitly]
        private static bool Prefix(GameEntity __instance) =>
            !WKTPuppetHitScope.IsOpen && (WKTWorldSync.AllowHostKill || !WKTWorldSync.IsPuppet(__instance));

        [UsedImplicitly]
        private static void Postfix(GameEntity __instance, string __0, Damageable.DamageInfo __1) => WKTWorldSync.Instance?.OnLocalKill(__instance, __0, __1);
    }

    /// <summary>
    /// Spawners that run while players are near spawn only on the host in a synced co-op run; clients get its enemies. Their
    /// own copies would only be hidden, so not spawning them saves the work.
    /// </summary>
    [WKTCriticalPatch(WKTPatchCategory.WorldSync), HarmonyPatch]
    internal sealed class ClientSpawnerBlock_Patch {
        public static IEnumerable<MethodBase> GetTargets() {
            yield return AccessTools.Method(typeof(ENV_DenizenWaveZone), "Update");
            yield return AccessTools.Method(typeof(UT_TriggerSpawner), nameof(UT_TriggerSpawner.Spawn));
        }

        [UsedImplicitly]
        private static IEnumerable<MethodBase> TargetMethods() => GetTargets();

        [UsedImplicitly]
        private static bool Prefix() => !IsSyncedClient();

        public static bool IsSyncedClient() {
            var sync = WKTWorldSync.Instance;
            return sync != null && !sync.IsHost;
        }
    }

    /// <summary>
    /// A client's perks must not swap the host's objects for others on its machine alone; in a synced co-op run only the
    /// host's world decides what exists.
    /// </summary>
    [WKTCriticalPatch(WKTPatchCategory.WorldSync), HarmonyPatch(typeof(CL_GameManager.ReplaceInfo), nameof(CL_GameManager.ReplaceInfo.ShouldReplace))]
    internal sealed class ClientReplacementBlock_Patch {
        [UsedImplicitly]
        private static bool Prefix(ref bool __result) {
            if (!ClientSpawnerBlock_Patch.IsSyncedClient()) return true;
            __result = false;
            return false;
        }
    }
}
