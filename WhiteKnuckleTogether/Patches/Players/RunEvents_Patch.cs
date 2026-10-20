using HarmonyLib;
using JetBrains.Annotations;

namespace WhiteKnuckleTogether {
    /// <summary>A perk the local player chooses during a multiplayer run goes to everyone's chat (see <see cref="WKTRunEvents"/>).</summary>
    [HarmonyPatch(typeof(ENT_Player), nameof(ENT_Player.AddPerk))]
    internal sealed class PerkEvent_Patch {
        [UsedImplicitly]
        private static void Postfix(ENT_Player __instance, Perk __0, bool __2) {
            if (__2 && __0 != null && __instance == ENT_Player.playerObject)
                WKTRunEvents.OnPerk(__0);
        }
    }

    /// <summary>Marks the perk bought at a perk machine, which the machine adds a moment later, as a choice.</summary>
    [HarmonyPatch(typeof(App_PerkPage), nameof(App_PerkPage.PurchaseSelected))]
    internal sealed class PerkPurchase_Patch {
        private static readonly AccessTools.FieldRef<App_PerkPage, App_PerkPage_Card?> s_selectedCard =
            AccessTools.FieldRefAccess<App_PerkPage, App_PerkPage_Card?>("selectedCard");

        [UsedImplicitly]
        private static void Prefix(App_PerkPage __instance) {
            var card = s_selectedCard(__instance);
            if (card != null)
                WKTRunEvents.OnPerkBought(card.perk);
        }
    }
}
