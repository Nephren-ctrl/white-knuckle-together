using HarmonyLib;
using JetBrains.Annotations;
using UnityEngine.EventSystems;

// The chat reads the keyboard itself. See WKTChat. Not critical: without it, Enter while typing in a menu could also press
// the menu's selected button.
namespace WhiteKnuckleTogether {
    /// <summary>The UI handles this frame's input in its Update, so the chat must claim the keyboard just before.</summary>
    [HarmonyPatch(typeof(EventSystem), "Update")]
    internal sealed class EventSystemUpdate_Patch {
        [UsedImplicitly]
        private static void Prefix(EventSystem __instance) => WKTChat.BeforeEventSystemUpdate(__instance);
    }
}
