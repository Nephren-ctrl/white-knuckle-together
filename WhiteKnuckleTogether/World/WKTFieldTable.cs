using System;
using System.Collections.Generic;
using System.Reflection;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// Every value of one type an object holds in its fields, single or in lists, in field order: the same on every machine
    /// that built the object from the same prefab, so an index into it names one value across machines. Unset values keep
    /// their places. Each type's fields are found once.
    /// </summary>
    internal static class WKTFieldTable {
        private const BindingFlags c_fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        private static readonly Dictionary<(Type Owner, Type Value), FieldInfo[]> s_fields = new();
        private static readonly List<Type> s_chain = new();
        private static readonly List<FieldInfo> s_found = new();

        /// <summary>Adds <paramref name="source"/>'s values of type <typeparamref name="T"/> to <paramref name="values"/>.</summary>
        public static void Collect<T>(object? source, List<T?> values) where T : class {
            if (source == null) return;
            foreach (var field in GetFields(source.GetType(), typeof(T))) {
                var value = field.GetValue(source);
                if (field.FieldType == typeof(T))
                    values.Add(value as T);
                else if (value is List<T?> list)
                    values.AddRange(list);
            }
        }

        private static FieldInfo[] GetFields(Type owner, Type value) {
            if (s_fields.TryGetValue((owner, value), out var cached)) return cached;
            var list = typeof(List<>).MakeGenericType(value);
            // Base types first, each in declaration order, which the metadata keeps.
            s_chain.Clear();
            for (var type = owner; type != null && type != typeof(object); type = type.BaseType)
                s_chain.Insert(0, type);
            s_found.Clear();
            foreach (var type in s_chain) {
                foreach (var field in type.GetFields(c_fields)) {
                    if (field.FieldType == value || field.FieldType == list)
                        s_found.Add(field);
                }
            }
            var fields = s_found.ToArray();
            s_found.Clear();
            s_chain.Clear();
            s_fields[(owner, value)] = fields;
            return fields;
        }
    }
}
