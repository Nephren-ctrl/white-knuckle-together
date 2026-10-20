using HarmonyLib;
using JetBrains.Annotations;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// A client's puppet tongue never decides where to reach: it follows the host and only checks whether it hits the local
    /// player (see <see cref="WKTTongueSync.PuppetTick"/>).
    /// </summary>
    [WKTCriticalPatch(WKTPatchCategory.WorldSync), HarmonyPatch(typeof(Barnacle_Tongue_Basic), "AI")]
    internal sealed class BarnacleTongueAI_Patch {
        [UsedImplicitly]
        private static bool Prefix(Barnacle_Tongue_Basic __instance, float __0) {
            var sync = WKTWorldSync.Instance;
            if (sync == null || sync.IsHost || !WKTTongueSync.IsPuppetTongue(__instance, out var barnacle)) return true;
            sync.Tongues.PuppetTick(__instance, barnacle!, __0);
            return false;
        }
    }

    /// <summary>
    /// A tongue's health is its own, not the barnacle's, so breaking one on a client's screen is sent to the host. Without it
    /// the tongue pulls back only there, and keeps holding the host's player.
    /// </summary>
    [WKTCriticalPatch(WKTPatchCategory.WorldSync), HarmonyPatch(typeof(Barnacle_Tongue_Basic), nameof(Barnacle_Tongue_Basic.KillTongue))]
    internal sealed class BarnacleTongueKill_Patch {
        [UsedImplicitly]
        private static void Postfix(Barnacle_Tongue_Basic __instance) {
            var sync = WKTWorldSync.Instance;
            if (sync == null || sync.IsHost || !WKTTongueSync.IsPuppetTongue(__instance, out var barnacle)) return;
            sync.Tongues.OnPuppetTongueKilled(__instance, barnacle!);
        }
    }

    /// <summary>The host's tongues never grab a remote player themselves: that player's own machine decides it.</summary>
    [WKTCriticalPatch(WKTPatchCategory.WorldSync), HarmonyPatch(typeof(Barnacle_Tongue_Basic), "ScanForHitEntity")]
    internal sealed class BarnacleTongueScan_Patch {
        [UsedImplicitly]
        private static bool Prefix(Barnacle_Tongue_Basic __instance) {
            var sync = WKTWorldSync.Instance;
            return sync == null || !sync.IsHost || WKTTongueSync.AllowHostScan(__instance);
        }
    }
}
