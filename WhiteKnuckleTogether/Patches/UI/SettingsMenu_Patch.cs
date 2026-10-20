using HarmonyLib;
using JetBrains.Annotations;

// The mod's settings in the game's settings menu. See WKTSettingsInjector. Not critical: without them the mod's settings keep
// their saved values and can still be edited in the config file.
namespace WhiteKnuckleTogether {
    /// <summary>The settings menu exists in the main menu and in every run, and starts once in each.</summary>
    [HarmonyPatch(typeof(UI_SettingsMenu), "Start")]
    internal sealed class SettingsMenuStart_Patch {
        [UsedImplicitly]
        private static void Postfix(UI_SettingsMenu __instance) => WKTSettingsInjector.Inject(__instance.transform);
    }

    /// <summary>
    /// Escape cancels a mod key capture or closes the chat, and must not also close the menu it is over, which reads it as
    /// "back".
    /// </summary>
    [HarmonyPatch(typeof(UI_CloseButtonOnBack), "Update")]
    internal sealed class CloseButtonOnBackUpdate_Patch {
        [UsedImplicitly]
        private static bool Prefix() => !WKTKeyRebindRow.OwnsEscape && !WKTChat.OwnsKeyboard;
    }
}
