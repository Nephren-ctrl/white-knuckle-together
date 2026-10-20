using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using JetBrains.Annotations;
using UnityEngine;

namespace WhiteKnuckleTogether {
    /// <summary>Finds a method on <see cref="Projectile"/> and every override of it.</summary>
    internal static class ProjectileTargets {
        public static List<MethodBase> Find(string name, Type[] parameters) {
            var targets = new List<MethodBase>();
            foreach (var type in WKTGameTypes.All) {
                if (!typeof(Projectile).IsAssignableFrom(type)) continue;
                var method = type.GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
                    null, parameters, null);
                if (method != null && !method.IsAbstract)
                    targets.Add(method);
            }
            return targets;
        }
    }

    /// <summary>When an enemy fires on the machine simulating it, everyone else shows the same shot from their puppet.</summary>
    [WKTCriticalPatch(WKTPatchCategory.WorldSync), HarmonyPatch]
    internal sealed class ProjectileInitialize_Patch {
        public static readonly Type[] Parameters = { typeof(Vector3), typeof(GameEntity), typeof(Item) };

        [UsedImplicitly]
        private static IEnumerable<MethodBase> TargetMethods() => ProjectileTargets.Find(nameof(Projectile.Initialize), Parameters);

        [UsedImplicitly]
        private static void Postfix(Projectile __instance, Vector3 __0, GameEntity __1) {
            if (__1 != null)
                WKTWorldSync.Instance?.EnemyShots.OnInitialize(__instance, __1, __0);
        }
    }

    /// <summary>
    /// A client's copy of a host enemy's shot hits only the client's own player, whose machine decides that hit. Anything else
    /// it hits is the host's shot to decide.
    /// </summary>
    [WKTCriticalPatch(WKTPatchCategory.WorldSync), HarmonyPatch]
    internal sealed class VisualProjectileDamage_Patch {
        public static readonly Type[] Parameters = { typeof(Damageable), typeof(Damageable.DamageInfo) };

        [UsedImplicitly]
        private static IEnumerable<MethodBase> TargetMethods() => ProjectileTargets.Find("OnDamage", Parameters);

        [UsedImplicitly]
        private static bool Prefix(Projectile __instance, Damageable __0, ref bool __result) {
            if (!WKTEnemyShots.IsCopy(__instance)) return true;
            if (__0 is ENT_Player player && player == ENT_Player.playerObject && !player.dead) return true;
            __result = false;
            return false;
        }
    }

    /// <summary>
    /// The host's enemy shots pass through remote players: each player's own copy of the shot decides whether it hits them.
    /// This also keeps shots that knock players off their holds from looking for a player the stand-in does not have. Other
    /// players' shots shown here never hit anything: the shooter's own shot does. A copy of an enemy's shot only hits the
    /// local player, so it does not push or set off anything else a second time.
    /// </summary>
    [WKTCriticalPatch(WKTPatchCategory.WorldSync), HarmonyPatch]
    internal sealed class HostProjectileProxyHit_Patch {
        public static readonly Type[] Parameters = {
            typeof(RaycastHit).MakeByRefType(), typeof(ObjectTagger).MakeByRefType(), typeof(bool).MakeByRefType(), typeof(bool).MakeByRefType()
        };

        [UsedImplicitly]
        private static IEnumerable<MethodBase> TargetMethods() => ProjectileTargets.Find("HitInteractions", Parameters);

        [UsedImplicitly]
        private static bool Prefix(Projectile __instance, ref RaycastHit __0, ref bool __3) {
            var sync = WKTWorldSync.Instance;
            if (sync == null) return true;
            var collider = __0.collider;
            var skipped = WKTPlayerShots.IsCosmetic(__instance) || (sync.IsHost && WKTPlayerProxy.IsProxyCollider(collider)) ||
                          (WKTEnemyShots.IsCopy(__instance) && !IsLocalPlayer(collider));
            if (!skipped) return true;
            __3 = false;
            return false;
        }

        private static bool IsLocalPlayer(Collider? collider) =>
            collider != null && collider.GetComponentInParent<ENT_Player>() is { } player && player == ENT_Player.playerObject;
    }

    /// <summary>
    /// The host's enemy shots fly on through remote players' stand-ins rather than stopping or sticking in them: each player's
    /// own copy of the shot lands on them.
    /// </summary>
    [WKTCriticalPatch(WKTPatchCategory.WorldSync), HarmonyPatch]
    internal sealed class HostProjectileProxyStop_Patch {
        public static readonly Type[] Parameters = { typeof(RaycastHit), typeof(ObjectTagger), typeof(bool), typeof(bool) };

        [UsedImplicitly]
        private static IEnumerable<MethodBase> TargetMethods() => ProjectileTargets.Find("OnCollisionHit", Parameters);

        [UsedImplicitly]
        private static bool Prefix(Projectile __instance, RaycastHit __0) =>
            WKTWorldSync.Instance is not { IsHost: true } || WKTPlayerShots.IsCosmetic(__instance) || !WKTPlayerProxy.IsProxyCollider(__0.collider);
    }

    /// <summary>The local player firing a hand item shows the shot to everyone else.</summary>
    [WKTCriticalPatch(WKTPatchCategory.WorldSync), HarmonyPatch]
    internal sealed class PlayerShot_Patch {
        public static IEnumerable<MethodBase> GetTargets() {
            foreach (var type in WKTGameTypes.All) {
                if (!typeof(HandItem_Shoot).IsAssignableFrom(type)) continue;
                var method = type.GetMethod("CreateProjectile", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (method != null && !method.IsAbstract)
                    yield return method;
            }
        }

        [UsedImplicitly]
        private static IEnumerable<MethodBase> TargetMethods() => GetTargets();

        [UsedImplicitly]
        private static void Postfix(HandItem_Shoot __instance, Projectile __result) => WKTWorldSync.Instance?.PlayerShots.OnLocalShot(__instance, __result);
    }
}
