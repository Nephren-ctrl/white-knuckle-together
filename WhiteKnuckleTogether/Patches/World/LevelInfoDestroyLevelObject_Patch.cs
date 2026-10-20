using HarmonyLib;
using JetBrains.Annotations;

namespace WhiteKnuckleTogether {
    /// <summary>Forgets an unloading level's content keys. Not critical: missing it only keeps keys alive until the run ends.</summary>
    [HarmonyPatch(typeof(WorldLoader.LevelInfo), nameof(WorldLoader.LevelInfo.DestroyLevelObject))]
    internal sealed class LevelInfoDestroyLevelObject_Patch {
        [UsedImplicitly]
        private static void Prefix(WorldLoader.LevelInfo __instance) {
            if (WKTContentSeeding.IsActive)
                WKTContentSeeding.ForgetLevel(__instance.GetLevel());
        }
    }
}
