using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using JetBrains.Annotations;

// A level button's own state is kept alike on every machine in a synced co-op run. See WKTButtonSync.
namespace WhiteKnuckleTogether {
    /// <summary>
    /// Every press of a level button, from a hand, a rebar, a hammer or a grappling hook: a press it took is shown on every
    /// machine, and a button that starts a mover waits for everyone to be near it. A toggle button's press runs the plain
    /// button's inside it, which counts as the same press.
    /// </summary>
    [HarmonyPatch]
    internal sealed class ButtonPress_Patch {
        private static int s_depth;

        [UsedImplicitly]
        private static IEnumerable<MethodBase> TargetMethods() {
            yield return AccessTools.Method(typeof(CL_Button), nameof(CL_Button.Interact), Type.EmptyTypes);
            yield return AccessTools.Method(typeof(CL_ToggleButton), nameof(CL_ToggleButton.Interact), Type.EmptyTypes);
        }

        [UsedImplicitly]
        private static bool Prefix(CL_Button __instance, out Press __state) {
            __state = default;
            var sync = WKTDoorSync.Instance;
            if (sync == null || WKTButtonSync.IsApplying || WKTDoorSync.IsApplying) return true;
            if (++s_depth > 1) {
                __state = new Press(true, false, 0, false);
                return true;
            }
            if (__instance.CanInteract() && !sync.Buttons.AllowPress(__instance)) {
                __state = new Press(true, false, 0, false);
                return false;
            }
            __state = new Press(true, true, PressField.Get(__instance), __instance is CL_ToggleButton { state: true });
            return true;
        }

        [UsedImplicitly]
        private static void Postfix(CL_Button __instance, Press __state) {
            if (!__state.IsOuter || WKTDoorSync.Instance is not { } sync) return;
            var toggled = __instance is CL_ToggleButton toggle && toggle.state != __state.On;
            if (toggled || PressField.Get(__instance) != __state.Presses)
                sync.Buttons.OnLocalPress(__instance);
        }

        [UsedImplicitly]
        private static Exception? Finalizer(Exception? __exception, Press __state) {
            if (__state.Counted)
                s_depth--;
            return __exception;
        }

        /// <summary>Found when first used: the test runtime cannot load the game's types eagerly.</summary>
        private static class PressField {
            private static readonly AccessTools.FieldRef<CL_Button, int> s_presses = AccessTools.FieldRefAccess<CL_Button, int>("presses");

            public static int Get(CL_Button button) => s_presses(button);
        }

        private readonly struct Press {
            /// <summary>This call raised the depth, so it lowers it again.</summary>
            public readonly bool Counted;
            /// <summary>The outermost call of a press, which went ahead; it sends the press if one was taken.</summary>
            public readonly bool IsOuter;
            public readonly int Presses;
            public readonly bool On;

            public Press(bool counted, bool isOuter, int presses, bool on) {
                Counted = counted;
                IsOuter = isOuter;
                Presses = presses;
                On = on;
            }
        }
    }

    /// <summary>Host only: level scripting turned a button on or off, which everyone sees.</summary>
    [HarmonyPatch(typeof(CL_Button), nameof(CL_Button.SetInteractable))]
    internal sealed class ButtonSetInteractable_Patch {
        [UsedImplicitly]
        private static void Postfix(CL_Button __instance) {
            if (WKTButtonSync.IsApplying || WKTDoorSync.Instance is not { IsHost: true } sync) return;
            sync.Buttons.OnHostInteractableChanged(__instance);
        }
    }
}
