using HarmonyLib;
using JetBrains.Annotations;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// An explosion goes off on every machine, from a death or item every machine runs. Each player's own explosion hurts and
    /// pushes them; the host's explosion hits the world's entities, each on the machine simulating it, so none is hit once
    /// per player (see <see cref="WKTSharedEffectScope"/>).
    /// </summary>
    [WKTCriticalPatch(WKTPatchCategory.WorldSync), HarmonyPatch(typeof(UT_Explosion), nameof(UT_Explosion.Explode))]
    internal sealed class SharedExplosion_Patch {
        [UsedImplicitly]
        private static void Prefix(out bool __state) {
            __state = WKTWorldSync.Instance != null;
            if (__state)
                WKTSharedEffectScope.Enter();
        }

        [UsedImplicitly]
        private static void Finalizer(bool __state) {
            if (__state)
                WKTSharedEffectScope.Exit();
        }
    }
}
