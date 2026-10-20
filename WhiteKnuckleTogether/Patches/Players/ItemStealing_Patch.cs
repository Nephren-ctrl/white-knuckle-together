using HarmonyLib;
using JetBrains.Annotations;

namespace WhiteKnuckleTogether {
    /// <summary>A stolen item cannot be used until the thief lets go of the button that took it (see <see cref="WKTItemStealing"/>).</summary>
    [HarmonyPatch(typeof(HandItem), nameof(HandItem.TryUse))]
    internal sealed class StolenItemUse_Patch {
        [UsedImplicitly]
        private static bool Prefix(HandItem __instance) => __instance.hand == null || !WKTItemStealing.IsLocked(__instance.hand.id);
    }
}
