using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using JetBrains.Annotations;
using UnityEngine;
using UnityEngine.Events;

namespace WhiteKnuckleTogether {
    /// <summary>A door setting itself up does so freely on every machine; afterwards the host owns it.</summary>
    [WKTCriticalPatch(WKTPatchCategory.WorldSync), HarmonyPatch(typeof(UT_Door), "Start")]
    internal sealed class DoorStart_Patch {
        [UsedImplicitly]
        private static void Prefix(out bool __state) {
            __state = WKTDoorSync.Instance != null;
            if (__state)
                WKTDoorSync.DoorStartDepth++;
        }

        [UsedImplicitly]
        private static void Postfix(UT_Door __instance) {
            WKTDoorSync.Instance?.OnDoorStarted(__instance);
            WKTSharedMotion.OnDoorStarted(__instance);
        }

        [UsedImplicitly]
        private static void Finalizer(bool __state) {
            if (__state)
                WKTDoorSync.DoorStartDepth--;
        }
    }

    /// <summary>
    /// A timer a client started by itself starts on the host too, whose timers alone fire (see <see cref="WKTDoorSync.OnTimerPlay"/>).
    /// A repeating timer starting itself again from its own tick is not a start.
    /// </summary>
    [HarmonyPatch(typeof(UT_Timer), nameof(UT_Timer.Play))]
    internal sealed class TimerPlay_Patch {
        [UsedImplicitly]
        private static void Prefix(UT_Timer __instance, bool reset) {
            if (TimerUpdate_Patch.Ticking != __instance)
                WKTDoorSync.Instance?.OnTimerPlay(__instance, reset);
        }
    }

    [HarmonyPatch(typeof(UT_Timer), "Update")]
    internal sealed class TimerUpdate_Patch {
        /// <summary>The timer whose Update is running, while it does.</summary>
        public static UT_Timer? Ticking { get; private set; }

        [UsedImplicitly]
        private static void Prefix(UT_Timer __instance) => Ticking = __instance;

        [UsedImplicitly]
        private static void Finalizer() => Ticking = null;
    }

    /// <summary>Doors only open and close on the host, which tells clients.</summary>
    [WKTCriticalPatch(WKTPatchCategory.WorldSync), HarmonyPatch]
    internal sealed class DoorOpenClose_Patch {
        public static IEnumerable<MethodBase> GetTargets() {
            yield return AccessTools.Method(typeof(UT_Door), nameof(UT_Door.Open));
            yield return AccessTools.Method(typeof(UT_Door), nameof(UT_Door.Close));
        }

        [UsedImplicitly]
        private static IEnumerable<MethodBase> TargetMethods() => GetTargets();

        [UsedImplicitly]
        private static bool Prefix(UT_Door __instance) {
            var sync = WKTDoorSync.Instance;
            return sync == null || sync.AllowDoorChange(__instance);
        }

        [UsedImplicitly]
        private static void Postfix(UT_Door __instance) {
            var sync = WKTDoorSync.Instance;
            if (sync != null && sync.IsHost)
                sync.OnHostDoorChanged(__instance);
        }
    }

    /// <summary>
    /// World events a client's player sets off are sent to the host; the host fires once-only triggers once. An entity's own
    /// events follow the machine simulating it (see <see cref="WKTEntityEvents"/>).
    /// </summary>
    [WKTCriticalPatch(WKTPatchCategory.WorldSync), HarmonyPatch(typeof(UnityEvent), nameof(UnityEvent.Invoke), new Type[0])]
    internal sealed class UnityEventInvoke_Patch {
        [UsedImplicitly]
        private static bool Prefix(UnityEventBase __instance) {
            var sync = WKTDoorSync.Instance;
            return (sync == null || sync.OnInvoke(__instance, null)) && WKTEntityEvents.BeforeInvoke(__instance, null);
        }
    }

    /// <summary>The same for events that pass whoever set them off, such as trigger zones and hand triggers.</summary>
    [WKTCriticalPatch(WKTPatchCategory.WorldSync), HarmonyPatch(typeof(UnityEvent<Transform>), nameof(UnityEvent<Transform>.Invoke), typeof(Transform))]
    internal sealed class UnityEventTransformInvoke_Patch {
        [UsedImplicitly]
        private static bool Prefix(UnityEventBase __instance, Transform __0) {
            var sync = WKTDoorSync.Instance;
            return (sync == null || sync.OnInvoke(__instance, __0)) && WKTEntityEvents.BeforeInvoke(__instance, __0);
        }
    }
}
