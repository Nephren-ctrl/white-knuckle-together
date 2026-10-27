using HarmonyLib;
using JetBrains.Annotations;

namespace WhiteKnuckleTogether {
    /// <summary>A session event's locator spawner, whose enemies the host spawns for everyone (see <see cref="WKTEventSpawns"/>).</summary>
    [HarmonyPatch(typeof(SessionEventModule_LocatorSpawner), nameof(SessionEventModule_LocatorSpawner.Spawn))]
    internal sealed class EventSpawns_Patch {
        [UsedImplicitly]
        private static bool Prefix(SessionEventModule_LocatorSpawner __instance) => WKTWorldSync.Instance?.EventSpawns.BeforeSpawn(__instance) ?? true;

        [UsedImplicitly]
        private static void Finalizer() => WKTWorldSync.Instance?.EventSpawns.AfterSpawn();
    }
}
