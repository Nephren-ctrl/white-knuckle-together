using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using JetBrains.Annotations;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// Only the host's Rho altars take items (see <see cref="WKTRhoAltars"/>). <c>CheckTransform</c>, the altar's trigger zone
    /// listener, only calls this.
    /// </summary>
    [WKTCriticalPatch(WKTPatchCategory.WorldSync), HarmonyPatch(typeof(ENV_RhoAltar), nameof(ENV_RhoAltar.CheckItemObject))]
    internal sealed class RhoAltarCheck_Patch {
        [UsedImplicitly]
        private static bool Prefix(ENV_RhoAltar __instance, Item_Object __0, out int __state) {
            __state = -1;
            return WKTWorldSync.Instance?.RhoAltars.BeforeCheck(__instance, __0, out __state) ?? true;
        }

        [UsedImplicitly]
        private static void Postfix(ENV_RhoAltar __instance, int __state) => WKTWorldSync.Instance?.RhoAltars.AfterCheck(__instance, __state);
    }

    /// <summary>Only the host's Rho altars trade, for their payee. <c>Update</c> only calls this, and only after a take.</summary>
    [WKTCriticalPatch(WKTPatchCategory.WorldSync), HarmonyPatch(typeof(ENV_RhoAltar), nameof(ENV_RhoAltar.Trade))]
    internal sealed class RhoAltarTrade_Patch {
        [UsedImplicitly]
        private static bool Prefix(ENV_RhoAltar __instance) => WKTWorldSync.Instance?.RhoAltars.BeforeTrade(__instance) ?? true;

        [UsedImplicitly]
        private static void Finalizer() => WKTWorldSync.Instance?.RhoAltars.AfterTrade();
    }

    /// <summary>
    /// A Rho altar's delayed take (its <c>WaitToTake</c> coroutine, a local function of <c>CheckItemObject</c>) runs for the
    /// player who dropped the item. It is nested in the compiler's closure classes, which hold the altar and the item.
    /// </summary>
    [WKTCriticalPatch(WKTPatchCategory.WorldSync), HarmonyPatch]
    internal sealed class RhoAltarTake_Patch {
        private static FieldInfo[] s_altarPath = Array.Empty<FieldInfo>();
        private static FieldInfo[] s_itemPath = Array.Empty<FieldInfo>();

        public static MethodBase GetTarget() {
            var moveNext = FindMoveNext(typeof(ENV_RhoAltar));
            return moveNext ?? throw new InvalidOperationException("ENV_RhoAltar's WaitToTake coroutine was not found.");
        }

        /// <summary>The fields leading from the coroutine to a value of <paramref name="type"/>, through the closure classes.</summary>
        public static FieldInfo[] FindPath(Type coroutine, Type type) {
            var path = new List<FieldInfo>();
            return TryFindPath(coroutine, type, path, 0) ? path.ToArray() : throw new InvalidOperationException($"ENV_RhoAltar's WaitToTake coroutine does not hold its {type.Name}.");
        }

        [UsedImplicitly]
        private static MethodBase TargetMethod() {
            var target = GetTarget();
            s_altarPath = FindPath(target.DeclaringType!, typeof(ENV_RhoAltar));
            s_itemPath = FindPath(target.DeclaringType!, typeof(Item_Object));
            return target;
        }

        [UsedImplicitly]
        private static bool Prefix(object __instance, ref bool __result) {
            var altars = WKTWorldSync.Instance?.RhoAltars;
            if (altars == null || Follow(__instance, s_altarPath) is not ENV_RhoAltar altar || altars.BeforeTake(altar, Follow(__instance, s_itemPath) as Item_Object)) return true;
            __result = false;
            return false;
        }

        [UsedImplicitly]
        private static void Finalizer() => WKTWorldSync.Instance?.RhoAltars.AfterTake();

        private static MethodInfo? FindMoveNext(Type type) {
            foreach (var nested in type.GetNestedTypes(BindingFlags.NonPublic)) {
                if (nested.Name.Contains("WaitToTake") && AccessTools.Method(nested, "MoveNext") is { } moveNext) return moveNext;
                if (FindMoveNext(nested) is { } deeper) return deeper;
            }
            return null;
        }

        private static bool TryFindPath(Type owner, Type type, List<FieldInfo> path, int depth) {
            if (depth > 3) return false;
            foreach (var field in owner.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)) {
                path.Add(field);
                if (field.FieldType == type) return true;
                // Only the compiler's own closure classes are followed, nested in the altar.
                if (field.FieldType.DeclaringType == typeof(ENV_RhoAltar) && field.FieldType.Name.StartsWith("<", StringComparison.Ordinal) &&
                    TryFindPath(field.FieldType, type, path, depth + 1)) return true;
                path.RemoveAt(path.Count - 1);
            }
            return false;
        }

        private static object? Follow(object? value, FieldInfo[] path) {
            foreach (var field in path) {
                if (value == null) return null;
                value = field.GetValue(value);
            }
            return value;
        }
    }
}
