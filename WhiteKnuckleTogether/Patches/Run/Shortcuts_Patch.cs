using HarmonyLib;
using JetBrains.Annotations;

// The host's shortcut choice in a multiplayer run (see WKTShortcutSync). Each patch does nothing outside one, so
// singleplayer is untouched.
namespace WhiteKnuckleTogether {
    /// <summary>
    /// The Expulsion Chute's button and the Hadal Diver's computer wait for a flag check. Answered from the host's choice,
    /// so an open shortcut needs no code and a closed one ignores any code entered.
    /// </summary>
    [HarmonyPatch(typeof(UT_CheckFlag), nameof(UT_CheckFlag.CheckFlag))]
    internal sealed class CheckFlag_Patch {
        private static readonly AccessTools.FieldRef<UT_CheckFlag, bool> s_hasRun = AccessTools.FieldRefAccess<UT_CheckFlag, bool>("hasRun");

        [UsedImplicitly]
        private static bool Prefix(UT_CheckFlag __instance, bool forced) {
            var open = WKTShortcutSync.GetFlagOverride(__instance.flagName);
            if (open == null) return true;
            // The game's own guards, as it checks them first.
            if ((!__instance.active && !forced) || (__instance.runOnce && s_hasRun(__instance))) return false;
            s_hasRun(__instance) = true;
            if (open.Value)
                __instance.flagTrueEvent.Invoke();
            else
                __instance.flagFalseEvent.Invoke();
            return false;
        }
    }

    /// <summary>A closed Tangled Sink's vent door stays shut, whatever opens it: its computer, once its code is entered.</summary>
    [HarmonyPatch(typeof(UT_Door), nameof(UT_Door.Open))]
    internal sealed class ShortcutDoorOpen_Patch {
        [UsedImplicitly]
        private static bool Prefix(UT_Door __instance) => WKTShortcutSync.AllowOpen(__instance);
    }

    /// <summary>A closed Rotten Duct's prison door lever stays locked, whatever unlocks it: a key, or having visited Dead Man's Handle.</summary>
    [HarmonyPatch(typeof(CL_HoldLever), nameof(CL_HoldLever.SetLocked))]
    internal sealed class ShortcutLeverSetLocked_Patch {
        [UsedImplicitly]
        private static bool Prefix(CL_HoldLever __instance, bool b) => b || WKTShortcutSync.AllowOpen(__instance);
    }

    /// <summary>A closed Hadal Diver's computer never plays its unlock, which its scanner plays once the signal is found.</summary>
    [HarmonyPatch(typeof(UT_EventController), nameof(UT_EventController.PlayEvent))]
    internal sealed class ShortcutComputerUnlock_Patch {
        [UsedImplicitly]
        private static bool Prefix(UT_EventController __instance, string eventName) =>
            eventName != WKTShortcuts.UNLOCK_EVENT || WKTShortcutSync.AllowOpen(__instance);
    }
}
