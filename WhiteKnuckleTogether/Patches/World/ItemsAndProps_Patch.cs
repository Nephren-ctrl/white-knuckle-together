using HarmonyLib;
using JetBrains.Annotations;

namespace WhiteKnuckleTogether {
    /// <summary>A client picking up one of the host's items asks the host for it.</summary>
    [WKTCriticalPatch(WKTPatchCategory.WorldSync), HarmonyPatch(typeof(Inventory), nameof(Inventory.AddItemToHand), typeof(Item), typeof(ENT_Player.Hand))]
    internal sealed class InventoryAddItemToHand_Patch {
        [UsedImplicitly]
        private static void Prefix(Item? i) {
            if (i == null) return;
            WKTWorldSync.Instance?.OnLocalPickup(i);
            if (!WKTItemStealing.IsGiving)
                WKTRunEvents.OnPickup(i);
        }
    }

    /// <summary>A client dropping an item asks the host to make it a world object everyone sees.</summary>
    [WKTCriticalPatch(WKTPatchCategory.WorldSync), HarmonyPatch(typeof(Inventory), nameof(Inventory.DropItemIntoWorld))]
    internal sealed class InventoryDropItemIntoWorld_Patch {

        [UsedImplicitly]
        private static void Postfix(Item? item) {
            if (item != null)
                WKTWorldSync.Instance?.OnLocalDrop(item);
        }
    }

    /// <summary>
    /// A client buying from a vending machine spawns the item on its machine alone; it is handed to the host like a drop so
    /// everyone sees it. Vended items are the machine's newest siblings, so they are found by the sibling count before and after.
    /// </summary>
    [WKTCriticalPatch(WKTPatchCategory.WorldSync), HarmonyPatch(typeof(ENV_VendingMachine), nameof(ENV_VendingMachine.Buy), typeof(int), typeof(bool), typeof(bool))]
    internal sealed class VendingMachineBuy_Patch {
        [UsedImplicitly]
        private static void Prefix(ENV_VendingMachine __instance, out int __state) {
            var parent = __instance.transform.parent;
            __state = WKTWorldSync.Instance != null && !WKTWorldSync.Instance.IsHost && parent != null ? parent.childCount : -1;
        }

        [UsedImplicitly]
        private static void Postfix(ENV_VendingMachine __instance, int __state) {
            var sync = WKTWorldSync.Instance;
            var parent = __instance.transform.parent;
            if (__state < 0 || sync == null || parent == null) return;
            for (var i = __state; i < parent.childCount; i++) {
                if (parent.GetChild(i).TryGetComponent<Item_Object>(out var itemObject))
                    sync.OnLocalSpawnedItem(itemObject);
            }
        }
    }

    /// <summary>The local player grabbing a prop takes it over from whoever simulated it.</summary>
    [WKTCriticalPatch(WKTPatchCategory.WorldSync), HarmonyPatch(typeof(CL_Prop), nameof(CL_Prop.Interact))]
    internal sealed class PropInteract_Patch {
        [UsedImplicitly]
        private static void Prefix(CL_Prop __instance, Clickable.InteractionInfo? info) {
            var sync = WKTWorldSync.Instance;
            // The game stores the hold point as the hand's hold position, in the prop's space, before calling this.
            if (sync != null && info != null && info.player == ENT_Player.playerObject)
                sync.OnLocalGrab(__instance, info.hand != null ? info.hand.holdPosition : __instance.anchorPoint);
        }
    }

    /// <summary>The local player letting go of a prop with every hand hands it on to whoever should simulate it now.</summary>
    [WKTCriticalPatch(WKTPatchCategory.WorldSync), HarmonyPatch(typeof(CL_Prop), nameof(CL_Prop.StopInteract))]
    internal sealed class PropStopInteract_Patch {
        private static readonly AccessTools.FieldRef<CL_Prop, System.Collections.Generic.List<ENT_Player.Hand>> s_handsHolding =
            AccessTools.FieldRefAccess<CL_Prop, System.Collections.Generic.List<ENT_Player.Hand>>("handsHolding");

        [UsedImplicitly]
        private static void Postfix(CL_Prop __instance, ENT_Player p) {
            var sync = WKTWorldSync.Instance;
            if (sync != null && p == ENT_Player.playerObject && s_handsHolding(__instance).Count == 0)
                sync.OnLocalRelease(__instance);
        }
    }

    /// <summary>
    /// A prop another machine simulates skips the game's physics step, and with it the only code that quiets its drag sound,
    /// so that part runs here (see <see cref="WKTPuppetPropAudio"/>). Runs after the puppet patch skipped the step.
    /// </summary>
    [HarmonyPatch(typeof(CL_Prop), nameof(CL_Prop.FixedUpdate))]
    internal sealed class PuppetPropAudio_Patch {
        [UsedImplicitly]
        private static void Postfix(CL_Prop __instance) {
            if (WKTWorldSync.IsPuppet(__instance))
                WKTPuppetPropAudio.FixedUpdate(__instance);
        }
    }
}
