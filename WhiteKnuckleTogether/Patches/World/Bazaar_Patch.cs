using System.Collections.Generic;
using HarmonyLib;
using JetBrains.Annotations;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// The host binds the Bazaar's stock to clients again once the trader has set it up. Each item registers while it is
    /// still being made, before it is put on its stand and tagged for sale, so clients would otherwise get it untagged: their
    /// trading post then took every item for one the player brings, and the stock was free to use.
    /// </summary>
    [HarmonyPatch(typeof(ENV_RoachTrader.RoachTradeGroup), nameof(ENV_RoachTrader.RoachTradeGroup.Spawn))]
    internal sealed class BazaarStock_Patch {
        [UsedImplicitly]
        private static void Postfix(ENV_RoachTrader.RoachTradeGroup __instance) {
            if (WKTWorldSync.Instance is not { IsHost: true } sync || __instance.spawnLocations == null) return;
            foreach (var location in __instance.spawnLocations) {
                if (location == null) continue;
                foreach (var identity in location.GetComponentsInChildren<WKTNetIdentity>(true))
                    sync.Rebind(identity);
            }
        }
    }

    /// <summary>
    /// A purchase at the Bazaar's trading post clears the paid items' for-sale state on the buyer's machine only; the host's
    /// copies are paid too and bound to everyone again (see <see cref="WKTWorldSync.OnLocalPaid"/>). A post that destroys
    /// what the player brings at once does so while <see cref="IsTrading"/>, so a client's are removed by the host too.
    /// </summary>
    [HarmonyPatch(typeof(ENV_Barterer), nameof(ENV_Barterer.TryPurchase))]
    internal sealed class BazaarPurchase_Patch {
        private static readonly List<Item_Object> s_forSale = new();

        public static bool IsTrading { get; private set; }

        [UsedImplicitly]
        private static void Prefix(ENV_Barterer __instance) {
            s_forSale.Clear();
            if (WKTWorldSync.Instance == null) return;
            IsTrading = true;
            foreach (var item in __instance.itemsInZone) {
                if (item != null && item.itemData != null && item.itemData.HasTag(WKTItemModules.UNPURCHASED))
                    s_forSale.Add(item);
            }
        }

        [UsedImplicitly]
        private static void Postfix() {
            if (WKTWorldSync.Instance is { } sync) {
                foreach (var item in s_forSale) {
                    if (item != null && !item.itemData.HasTag(WKTItemModules.UNPURCHASED))
                        sync.OnLocalPaid(item);
                }
            }
            s_forSale.Clear();
        }

        [UsedImplicitly]
        private static void Finalizer() => IsTrading = false;
    }

    /// <summary>
    /// The roach trader takes what a player paid with: it sweeps the items away and destroys them a moment later on the buyer's
    /// machine. Those a client paid with are the host's world items, so the host removes its copies when the sweep ends, or
    /// the client would get them back where they lay.
    /// </summary>
    [HarmonyPatch(typeof(ENV_RoachTrader), nameof(ENV_RoachTrader.OnPurchase))]
    internal sealed class BazaarSold_Patch {
        private static readonly HashSet<Item> s_sold = new();

        /// <summary>Whether <paramref name="item"/> was paid to the trader here, forgetting it: it is being destroyed.</summary>
        public static bool TakeSold(Item item) => s_sold.Count > 0 && s_sold.Remove(item);

        [UsedImplicitly]
        private static void Postfix(List<Item_Object> soldItems) {
            if (WKTWorldSync.Instance is not { IsHost: false } || soldItems == null) return;
            foreach (var item in soldItems) {
                if (item != null && item.itemData != null)
                    s_sold.Add(item.itemData);
            }
        }
    }

    /// <summary>Leaving a Bazaar by its hatch takes everyone in one out with the player who left (see <see cref="WKTBazaar"/>).</summary>
    [HarmonyPatch(typeof(M_Chamber), nameof(M_Chamber.LeaveChamber))]
    internal sealed class BazaarLeave_Patch {
        [UsedImplicitly]
        private static void Prefix(M_Chamber __instance, out bool __state) =>
            __state = !WKTBazaar.IsApplyingLeave && WKTDoorSync.Instance != null && WKTBazaar.IsBazaar(__instance);

        [UsedImplicitly]
        private static void Postfix(bool __state) {
            if (__state)
                WKTDoorSync.Instance?.OnLocalBazaarLeft();
        }
    }
}
