using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// Finds the animator an entity drives itself, such as <c>DEN_Barnacle.animator</c>, rather than the first animator among
    /// its children, which can belong to a part that only decorates it. Looked up once per type.
    /// </summary>
    internal static class WKTEntityAnimators {
        private const BindingFlags c_flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        private static readonly Dictionary<Type, FieldInfo?> s_fields = new();

        /// <summary>The entity type's own animator field: the first <see cref="Animator"/> field named "animator", most derived first.</summary>
        public static FieldInfo? GetField(Type type) {
            if (s_fields.TryGetValue(type, out var cached)) return cached;
            FieldInfo? found = null;
            for (var current = type; current != null && current != typeof(MonoBehaviour) && found == null; current = current.BaseType) {
                foreach (var field in current.GetFields(c_flags)) {
                    if (field.FieldType != typeof(Animator) || !string.Equals(field.Name, "animator", StringComparison.OrdinalIgnoreCase)) continue;
                    found = field;
                    break;
                }
            }
            s_fields[type] = found;
            return found;
        }

        /// <summary>The animator <paramref name="owner"/> drives: its own field when set, else the first one among its children.</summary>
        public static Animator? Find(Component owner) {
            var field = GetField(owner.GetType());
            if (field != null && field.GetValue(owner) is Animator own && own != null) return own;
            return owner.GetComponentInChildren<Animator>();
        }
    }
}
