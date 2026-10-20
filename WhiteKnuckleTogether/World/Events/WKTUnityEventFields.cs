using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Events;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// The serialized UnityEvent fields of a component type, in declaration order from the most derived type. The order is
    /// the same on every machine, so an event's index identifies it between players.
    /// </summary>
    internal static class WKTUnityEventFields {
        private const BindingFlags c_flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        private static readonly Dictionary<Type, FieldInfo[]> s_cache = new();
        // Unity keeps a persistent listener's arguments in internal types: reached once by reflection.
        private static readonly FieldInfo? s_persistentCalls = typeof(UnityEventBase).GetField("m_PersistentCalls", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly MethodInfo? s_getListener = s_persistentCalls?.FieldType.GetMethod("GetListener", BindingFlags.Instance | BindingFlags.Public);
        private static readonly PropertyInfo? s_arguments = s_getListener?.ReturnType.GetProperty("arguments", BindingFlags.Instance | BindingFlags.Public);
        private static readonly PropertyInfo? s_stringArgument = s_arguments?.PropertyType.GetProperty("stringArgument", BindingFlags.Instance | BindingFlags.Public);

        /// <summary>Whether Unity's internals for reading persistent arguments were found.</summary>
        public static bool CanReadArguments => s_stringArgument != null;

        public static FieldInfo[] Get(Type type) {
            if (s_cache.TryGetValue(type, out var fields)) return fields;
            var result = new List<FieldInfo>();
            for (var current = type; current != null && current != typeof(MonoBehaviour) && current != typeof(object); current = current.BaseType) {
                foreach (var field in current.GetFields(c_flags)) {
                    var serialized = field.IsPublic || field.IsDefined(typeof(SerializeField), false);
                    if (serialized && !field.IsNotSerialized && typeof(UnityEventBase).IsAssignableFrom(field.FieldType))
                        result.Add(field);
                }
            }
            fields = result.ToArray();
            s_cache[type] = fields;
            return fields;
        }

        /// <summary>The string a persistent listener is called with, such as <c>"close:true"</c>. Read once, when a level is indexed.</summary>
        public static bool TryGetStringArgument(UnityEventBase unityEvent, int index, out string argument) {
            argument = string.Empty;
            if (!CanReadArguments || index < 0 || index >= unityEvent.GetPersistentEventCount()) return false;
            var group = s_persistentCalls!.GetValue(unityEvent);
            var call = group != null ? s_getListener!.Invoke(group, new object[] { index }) : null;
            var arguments = call != null ? s_arguments!.GetValue(call) : null;
            if (arguments == null || s_stringArgument!.GetValue(arguments) is not string value) return false;
            argument = value;
            return true;
        }

        /// <summary>
        /// Invokes only the world listeners of <paramref name="unityEvent"/>, or only its local ones, by switching the others off
        /// for the call. Their states are restored even when a listener throws. Listeners added from code always run.
        /// </summary>
        public static void InvokeFiltered(UnityEventBase unityEvent, Transform? argument, WKTListenerMasks masks, bool world) {
            var count = unityEvent.GetPersistentEventCount();
            Span<UnityEventCallState> saved = count <= WKTListenerMasks.MAX_TRACKED ? stackalloc UnityEventCallState[count] : new UnityEventCallState[count];
            for (var i = 0; i < count; i++) {
                saved[i] = unityEvent.GetPersistentListenerState(i);
                var included = world ? masks.IsWorld(i) : masks.IsLocal(i);
                if (!included && saved[i] != UnityEventCallState.Off)
                    unityEvent.SetPersistentListenerState(i, UnityEventCallState.Off);
            }
            try {
                switch (unityEvent) {
                    case UnityEvent<Transform> withTransform:
                        withTransform.Invoke(argument!);
                        break;
                    case UnityEvent plain:
                        plain.Invoke();
                        break;
                }
            }
            finally {
                for (var i = 0; i < count; i++) {
                    if (unityEvent.GetPersistentListenerState(i) != saved[i])
                        unityEvent.SetPersistentListenerState(i, saved[i]);
                }
            }
        }
    }
}
