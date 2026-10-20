using System;
using System.Collections;
using System.Reflection;
using UnityEngine;
using Object = UnityEngine.Object;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// Finds the projectile prefab an enemy or hand item refers to, by name, so a client can show a shot from its own copy.
    /// Searches the serialized fields of every component under a root, two levels into plain data objects such as attack
    /// lists. Callers cache the result: the search is slow.
    /// </summary>
    internal static class WKTPrefabSearch {
        private const int c_max_depth = 2;

        /// <summary>The projectile prefab named <paramref name="name"/> that some component under <paramref name="root"/> refers to.</summary>
        public static Projectile? FindProjectile(GameObject root, string name) {
            foreach (var component in root.GetComponentsInChildren<MonoBehaviour>(true)) {
                if (component == null) continue;
                var found = FindProjectile(component, name, 0);
                if (found != null) return found;
            }
            return null;
        }

        private static Projectile? FindProjectile(object owner, string name, int depth) {
            foreach (var field in owner.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)) {
                var value = field.GetValue(owner);
                switch (value) {
                    case Projectile projectile when projectile != null && GetPrefabName(projectile.name) == name:
                        return projectile;
                    case GameObject gameObject when gameObject != null && GetPrefabName(gameObject.name) == name && gameObject.TryGetComponent<Projectile>(out var onObject):
                        return onObject;
                    case null:
                    case Object:
                    case string:
                        continue;
                }
                if (depth >= c_max_depth || !IsSearchable(value.GetType())) continue;
                if (value is IList list) {
                    foreach (var element in list) {
                        if (element is Projectile listed && listed != null && GetPrefabName(listed.name) == name) return listed;
                        if (element != null && element is not Object && IsSearchable(element.GetType())) {
                            var nested = FindProjectile(element, name, depth + 1);
                            if (nested != null) return nested;
                        }
                    }
                    continue;
                }
                var found = FindProjectile(value, name, depth + 1);
                if (found != null) return found;
            }
            return null;
        }

        /// <summary>Game data objects worth looking into: not delegates, not framework types.</summary>
        public static bool IsSearchable(Type type) =>
            type.IsClass && !typeof(Delegate).IsAssignableFrom(type) &&
            (type.Namespace == null || !type.Namespace.StartsWith("System", StringComparison.Ordinal) || typeof(IList).IsAssignableFrom(type));

        /// <summary>An instance's name without the "(Clone)" Unity adds, which is the prefab's name.</summary>
        public static string GetPrefabName(string name) {
            const string clone = "(Clone)";
            var end = name.Length;
            while (end >= clone.Length && string.CompareOrdinal(name, end - clone.Length, clone, 0, clone.Length) == 0)
                end -= clone.Length;
            return end == name.Length ? name : name.Substring(0, end).TrimEnd();
        }
    }
}
