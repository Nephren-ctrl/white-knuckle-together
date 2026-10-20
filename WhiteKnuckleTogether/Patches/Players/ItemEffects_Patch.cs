using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using JetBrains.Annotations;
using UnityEngine;
using AudioSet = UT_AudioClipHandler.AudioSet;

namespace WhiteKnuckleTogether {
    /// <summary>Records the effects the local player's items play while their own code runs (see <see cref="WKTItemEffects"/>).</summary>
    [HarmonyPatch]
    internal sealed class HandItemEffectScope_Patch {
        private const BindingFlags c_declared = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        /// <summary>Every method a hand item type declares with a body, and the coroutines and closures nested in it.</summary>
        public static List<MethodBase> GetTargets() {
            var targets = new List<MethodBase>();
            foreach (var type in WKTGameTypes.All) {
                if (!typeof(HandItem).IsAssignableFrom(type) || type.ContainsGenericParameters) continue;
                Add(type, targets);
                foreach (var nested in type.GetNestedTypes(BindingFlags.NonPublic | BindingFlags.Public)) {
                    if (!nested.ContainsGenericParameters)
                        Add(nested, targets);
                }
            }
            return targets;
        }

        private static void Add(System.Type type, List<MethodBase> targets) {
            foreach (var method in type.GetMethods(c_declared)) {
                if (method.IsAbstract || method.IsGenericMethodDefinition || method.GetMethodBody() == null || method.IsSpecialName) continue;
                targets.Add(method);
            }
        }

        [UsedImplicitly]
        private static IEnumerable<MethodBase> TargetMethods() => GetTargets();

        [UsedImplicitly]
        private static void Prefix(out bool __state) {
            __state = WKTItemEffects.Instance != null;
            if (__state)
                WKTItemEffects.Enter();
        }

        [UsedImplicitly]
        private static void Finalizer(bool __state) {
            if (__state)
                WKTItemEffects.Exit();
        }
    }

    /// <summary>
    /// A hand item's clip handler playing a sound (often from an animation event) records it too: through <c>AudioManager</c>
    /// inside the scope, or on its own audio source, read after it played.
    /// </summary>
    [HarmonyPatch]
    internal sealed class HandItemClipScope_Patch {
        private static readonly AccessTools.FieldRef<AudioSet, UT_AudioClipHandler?> s_handler = AccessTools.FieldRefAccess<AudioSet, UT_AudioClipHandler?>("handler");
        private static readonly AccessTools.FieldRef<AudioSet, AudioSource?> s_source = AccessTools.FieldRefAccess<AudioSet, AudioSource?>("aud");
        private static readonly Dictionary<int, bool> s_onHandItem = new();

        [UsedImplicitly]
        private static IEnumerable<MethodBase> TargetMethods() {
            yield return AccessTools.Method(typeof(AudioSet), nameof(AudioSet.Play), new[] { typeof(float), typeof(AudioSource) });
            yield return AccessTools.Method(typeof(AudioSet), nameof(AudioSet.Play), new[] { typeof(float), typeof(Vector3) });
            yield return AccessTools.Method(typeof(AudioSet), nameof(AudioSet.PlaySound), new[] { typeof(float), typeof(AudioClip) });
            yield return AccessTools.Method(typeof(AudioSet), nameof(AudioSet.PlayAtPoint));
        }

        /// <summary>
        /// 1 while a hand item's sound is recorded. Below zero while another clip handler (an enemy, the world) plays inside
        /// an item's code: its sounds are shared their own way, so recording stops, and the negated depth is restored after.
        /// </summary>
        [UsedImplicitly]
        private static void Prefix(AudioSet __instance, out int __state) {
            __state = 0;
            if (WKTItemEffects.Instance == null) return;
            if (IsOnHandItem(s_handler(__instance))) {
                WKTItemEffects.Enter();
                __state = 1;
            }
            else if (WKTItemEffects.IsRecording)
                __state = -WKTItemEffects.Suspend();
        }

        [UsedImplicitly]
        private static void Postfix(AudioSet __instance, object[] __args, int __state) {
            if (__state != 1) return;
            var source = __args.Length > 1 && __args[1] is AudioSource given ? given : s_source(__instance);
            if (source != null && source.isPlaying && source.clip != null && !source.loop)
                WKTItemEffects.Instance?.OnSound(source.clip, source.transform.position, source.volume, source.pitch);
        }

        [UsedImplicitly]
        private static void Finalizer(int __state) {
            if (__state == 1)
                WKTItemEffects.Exit();
            else if (__state < 0)
                WKTItemEffects.Resume(-__state);
        }

        private static bool IsOnHandItem(UT_AudioClipHandler? handler) {
            if (handler == null) return false;
            var id = handler.GetInstanceID();
            if (s_onHandItem.TryGetValue(id, out var onHandItem)) return onHandItem;
            onHandItem = handler.GetComponentInParent<HandItem>(true) != null;
            s_onHandItem[id] = onHandItem;
            return onHandItem;
        }
    }

    /// <summary>Sounds an item plays through <c>AudioManager</c> while recording.</summary>
    [HarmonyPatch]
    internal sealed class ItemSound_Patch {
        [UsedImplicitly]
        private static IEnumerable<MethodBase> TargetMethods() {
            foreach (var method in typeof(AudioManager).GetMethods(BindingFlags.Static | BindingFlags.Public)) {
                if (method.Name != nameof(AudioManager.PlaySound)) continue;
                var parameters = method.GetParameters();
                if (parameters.Length >= 4 && parameters[0].ParameterType == typeof(AudioClip) &&
                    (parameters[1].ParameterType == typeof(Vector3) || parameters[1].ParameterType == typeof(Transform)))
                    yield return method;
            }
        }

        [UsedImplicitly]
        private static void Postfix(object[] __args) {
            if (!WKTItemEffects.IsRecording || __args[0] is not AudioClip clip) return;
            var position = __args[1] switch {
                Vector3 point => point,
                Transform from when from != null => from.position,
                _ => Vector3.zero
            };
            WKTItemEffects.Instance!.OnSound(clip, position, __args[2] is float volume ? volume : 1f, __args[3] is float pitch ? pitch : 1f);
        }
    }

    /// <summary>Particles an item plays while recording.</summary>
    [HarmonyPatch]
    internal sealed class ItemParticle_Patch {
        [UsedImplicitly]
        private static IEnumerable<MethodBase> TargetMethods() {
            yield return AccessTools.Method(typeof(FXManager), nameof(FXManager.PlayParticle));
            yield return AccessTools.Method(typeof(FXManager), nameof(FXManager.PlayParticleWithRotation));
            yield return AccessTools.Method(typeof(FXManager), nameof(FXManager.PlayParticleWithRadius));
        }

        [UsedImplicitly]
        private static void Postfix(object[] __args) {
            if (WKTItemEffects.IsRecording && __args[0] is string id && __args[1] is Vector3 position)
                WKTItemEffects.Instance!.OnParticle(id, position, __args[__args.Length > 3 && __args[2] is float ? 3 : 2] is int amount ? amount : 10);
        }
    }

    /// <summary>Tracers an item plays while recording, such as a gun's shot.</summary>
    [HarmonyPatch(typeof(FXManager), nameof(FXManager.PlayTracer))]
    internal sealed class ItemTracer_Patch {
        [UsedImplicitly]
        private static void Postfix(string tracerParticleID, Vector3 startPoint, Vector3 endPoint, float speed) {
            if (WKTItemEffects.IsRecording)
                WKTItemEffects.Instance!.OnTracer(tracerParticleID, startPoint, endPoint, speed);
        }
    }
}
