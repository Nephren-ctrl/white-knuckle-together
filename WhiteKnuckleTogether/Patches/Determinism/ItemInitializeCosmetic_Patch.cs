using HarmonyLib;
using JetBrains.Annotations;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// An item cosmetic with a swap item destroys the item and spawns a different one in its place. Cosmetics differ per
    /// player, so in a multiplayer run that would give players different objects and shift every roll after it. Such
    /// cosmetics are skipped there; cosmetics that only change the item's hand sprite or mesh still apply.
    /// </summary>
    [WKTCriticalPatch(WKTPatchCategory.RunSync), HarmonyPatch(typeof(Item), nameof(Item.Initialize))]
    internal sealed class ItemInitializeCosmetic_Patch {
        [UsedImplicitly]
        private static void Prefix(Item __instance, out bool __state) {
            __state = false;
            if (!WKTContentSeeding.IsActive || !__instance.allowCosmetics) return;
            var cosmetic = CL_CosmeticManager.GetActiveItemCosmetic(__instance.prefabName);
            if (cosmetic == null || cosmetic.swapItem == null) return;
            __instance.allowCosmetics = false;
            __state = true;
        }

        [UsedImplicitly]
        private static void Finalizer(Item __instance, bool __state) {
            if (__state)
                __instance.allowCosmetics = true;
        }
    }
}
