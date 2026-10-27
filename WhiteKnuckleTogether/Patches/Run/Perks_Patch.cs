using System;
using System.Collections.Generic;
using HarmonyLib;
using JetBrains.Annotations;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// Each player gets their own perk choices at a perk terminal or the experimental perk machine. The game seeds a page's
    /// cards from the terminal's world seed, which every machine shares, so the page's random numbers are re-seeded once with
    /// the player's id mixed in, before its first cards are dealt.
    /// </summary>
    [HarmonyPatch(typeof(App_PerkPage), "GenerateCards")]
    internal sealed class PerPlayerPerks_Patch {
        private static readonly AccessTools.FieldRef<App_PerkPage, Random> s_rng = AccessTools.FieldRefAccess<App_PerkPage, Random>("rng");
        private static readonly HashSet<int> s_seeded = new();

        [UsedImplicitly]
        private static void Prefix(App_PerkPage __instance) {
            var network = WKT.InstanceOrNull?.Network;
            if (network is not { IsMultiplayerRun: true } || network.LocalPlayer is not { } local || !s_seeded.Add(__instance.GetInstanceID())) return;
            var shared = s_rng(__instance);
            if (shared == null) return;
            s_rng(__instance) = new Random(WKTPerkSeeds.ForPlayer(shared.Next(), local.Id));
        }
    }

    /// <summary>With shared roaches, a priced perk is only bought once the host holds the roaches back for it (see <see cref="WKTSharedRoaches"/>).</summary>
    [HarmonyPatch(typeof(App_PerkPage), nameof(App_PerkPage.PurchaseSelected))]
    internal sealed class SharedRoachPurchase_Patch {
        private static readonly AccessTools.FieldRef<App_PerkPage, App_PerkPage_Card?> s_selected = AccessTools.FieldRefAccess<App_PerkPage, App_PerkPage_Card?>("selectedCard");

        [UsedImplicitly]
        private static bool Prefix(App_PerkPage __instance) {
            var roaches = WKTSharedRoaches.Instance;
            if (roaches == null) return true;
            var card = s_selected(__instance);
            if (card == null || card.perk == null || !card.CanAfford()) return true;
            return roaches.BeforePurchase(card.perk.cost, static (page, _) => ((App_PerkPage)page).PurchaseSelected(), __instance);
        }
    }

    /// <summary>With shared roaches, a perk page refresh is only bought once the host holds the roaches back for it.</summary>
    [HarmonyPatch(typeof(App_PerkPage), nameof(App_PerkPage.PurchaseRefresh))]
    internal sealed class SharedRoachRefresh_Patch {
        private static readonly AccessTools.FieldRef<App_PerkPage, bool> s_fulfilled = AccessTools.FieldRefAccess<App_PerkPage, bool>("fullfilled");

        [UsedImplicitly]
        private static bool Prefix(App_PerkPage __instance) {
            var roaches = WKTSharedRoaches.Instance;
            if (roaches == null || s_fulfilled(__instance) || CL_GameManager.GetRoaches() < __instance.refreshCost) return true;
            return roaches.BeforePurchase(__instance.refreshCost, static (page, _) => ((App_PerkPage)page).PurchaseRefresh(), __instance);
        }
    }

    /// <summary>With shared roaches, a vending machine item is only bought once the host holds the roaches back for it.</summary>
    [HarmonyPatch(typeof(ENV_VendingMachine), nameof(ENV_VendingMachine.Buy), typeof(int), typeof(bool), typeof(bool))]
    internal sealed class SharedRoachVending_Patch {
        private static readonly AccessTools.FieldRef<ENV_VendingMachine, bool> s_dead = AccessTools.FieldRefAccess<ENV_VendingMachine, bool>("dead");
        private static readonly AccessTools.FieldRef<ENV_VendingMachine, bool> s_blocked = AccessTools.FieldRefAccess<ENV_VendingMachine, bool>("isBlocked");

        [UsedImplicitly]
        private static bool Prefix(ENV_VendingMachine __instance, int i, bool force, bool free) {
            var roaches = WKTSharedRoaches.Instance;
            // Only a purchase the game would make is held back; the rest plays its own deny or broken sound.
            if (roaches == null || force || free || s_dead(__instance) || s_blocked(__instance)) return true;
            var button = __instance.buttons[i];
            if (!button.available || button.purchase.price > CL_GameManager.GetRoaches()) return true;
            return roaches.BeforePurchase(button.purchase.price, static (machine, index) => ((ENV_VendingMachine)machine).Buy(index), __instance, i);
        }
    }

    /// <summary>With shared roaches, every change to this machine's roaches goes to the purse (see <see cref="WKTSharedRoaches"/>).</summary>
    [HarmonyPatch(typeof(CL_GameManager), nameof(CL_GameManager.AddRoaches))]
    internal sealed class SharedRoachChange_Patch {
        [UsedImplicitly]
        private static void Postfix(int i, bool global) {
            if (!global)
                WKTSharedRoaches.Instance?.OnLocalChange(i);
        }
    }

    /// <summary>The seed of a player's own perk choices. Pure, so it can be tested without Unity.</summary>
    internal static class WKTPerkSeeds {
        public static int ForPlayer(int shared, byte playerId) => unchecked(shared ^ (playerId + 1) * 0x2C1B3C6D);
    }
}
