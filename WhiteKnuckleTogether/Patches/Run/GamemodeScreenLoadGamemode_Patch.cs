using HarmonyLib;
using JetBrains.Annotations;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// Play on a gamemode. Offline the original runs untouched. In a session, the host starts multiplayer runs for
    /// everyone, clients cannot start them, and playing a singleplayer gamemode leaves the session first. Critical:
    /// without it the host would load a multiplayer gamemode alone, with progression unlocked.
    /// </summary>
    [WKTCriticalPatch(WKTPatchCategory.RunSync), HarmonyPatch(typeof(UI_GamemodeScreen), nameof(UI_GamemodeScreen.LoadGamemode))]
    internal sealed class GamemodeScreenLoadGamemode_Patch {
        [UsedImplicitly]
        private static bool Prefix(UI_GamemodeScreen __instance) {
            var wkt = WKT.InstanceOrNull;
            if (wkt == null || wkt.Network.Mode == NetworkMode.None) return true;
            return wkt.Network.HandlePlayRequest(__instance.GetGamemode());
        }
    }
}
