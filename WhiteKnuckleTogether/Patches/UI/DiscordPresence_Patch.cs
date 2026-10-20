using HarmonyLib;
using JetBrains.Annotations;

// Multiplayer in Discord rich presence. See WKTDiscordPresence. Not critical: without it, Discord shows what it does in
// singleplayer.
namespace WhiteKnuckleTogether {
    [HarmonyPatch(typeof(DiscordManager), "UpdateRichPresence")]
    internal sealed class DiscordPresence_Patch {
        [UsedImplicitly]
        private static bool Prefix(DiscordManager __instance) => WKTDiscordPresence.BeforeUpdate(__instance);
    }
}
