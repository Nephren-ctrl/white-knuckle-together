using UnityEngine.Events;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// The group goes in and out of the Bazaar together in a synced co-op run. Its entrance hatch's press already reaches every
    /// machine as a level event; the hatch inside it, which is outside the levels, would only take out whoever pressed it, and
    /// left the others (spectators too) alone in an empty world. Both hatches wait for everyone to come to them (see
    /// <see cref="WKTButtonSync"/>), and leaving through one takes out everyone in a Bazaar (see <see cref="WKTDoorSync"/>).
    /// Anyone taken out holding stock they did not pay for drops it inside first, as the shop's door keeps them in in
    /// singleplayer.
    /// </summary>
    internal static class WKTBazaar {
        private const string c_chamber_prefix = "C_Roach_Bazaar";
        private const string c_unpurchased = "unpurchased";
        private const string c_enter = nameof(UT_ChamberLoader.EnterChamber);
        private const string c_leave = nameof(M_Chamber.LeaveChamber);
        private const string c_check = nameof(UT_EventChance.Check);
        /// <summary>At most this many items are dropped on the way out, in case one refuses to leave the inventory.</summary>
        private const int c_max_drops = 32;

        /// <summary>Set while this machine leaves a Bazaar because someone else did, which is never sent on.</summary>
        public static bool IsApplyingLeave { get; private set; }

        public static bool IsBazaar(M_Chamber? chamber) => chamber != null && chamber.name.StartsWith(c_chamber_prefix, System.StringComparison.Ordinal);

        /// <summary>Whether a button takes players into a Bazaar (directly or through its chance check) or out of one.</summary>
        public static bool IsHatch(CL_Button button) => IsHatchEvent(button.activeEvent, true);

        /// <summary>The Bazaar the local player is in, if any.</summary>
        public static M_Chamber? GetCurrent() {
            var parent = WorldLoader.GetCurrentLevelParentRoot();
            return parent != null && parent.TryGetComponent<M_Chamber>(out var chamber) && IsBazaar(chamber) ? chamber : null;
        }

        /// <summary>Someone left the Bazaar: the local player leaves it too, if in one, dropping stock they did not pay for.</summary>
        public static void LeaveHere() {
            if (GetCurrent() is not { } chamber) return;
            DropUnpaidStock();
            IsApplyingLeave = true;
            try {
                chamber.LeaveChamber();
            }
            finally {
                IsApplyingLeave = false;
            }
            WKT.Instance.Logger.LogInfo("Left the Bazaar with the group.");
        }

        private static bool IsHatchEvent(UnityEventBase? unityEvent, bool followChance) {
            if (unityEvent == null) return false;
            var count = unityEvent.GetPersistentEventCount();
            for (var i = 0; i < count; i++) {
                var method = unityEvent.GetPersistentMethodName(i);
                switch (unityEvent.GetPersistentTarget(i)) {
                    case UT_ChamberLoader loader when method == c_enter && LoadsBazaar(loader):
                    case M_Chamber chamber when method == c_leave && IsBazaar(chamber):
                    case UT_EventChance chance when followChance && method == c_check && IsHatchEvent(chance.successEvent, false):
                        return true;
                }
            }
            return false;
        }

        private static bool LoadsBazaar(UT_ChamberLoader loader) {
            if (loader.chambers == null) return false;
            foreach (var chamber in loader.chambers) {
                if (IsBazaar(chamber)) return true;
            }
            return false;
        }

        private static void DropUnpaidStock() {
            var inventory = Inventory.instance;
            if (inventory == null || ENT_Player.playerObject is not { } player) return;
            var position = player.transform.position;
            for (var i = 0; i < c_max_drops && inventory.GetItemByTag(c_unpurchased) is { } item; i++) {
                var hand = HandOf(inventory, item);
                if (hand >= 0)
                    inventory.DropItemFromHand(position, hand, force: true);
                else {
                    inventory.bagItems.Remove(item);
                    inventory.DropItemIntoWorld(item, position, false);
                    inventory.CalculateEncumberance();
                }
            }
        }

        private static int HandOf(Inventory inventory, Item item) {
            for (var h = 0; h < inventory.itemHands.Length; h++) {
                if (inventory.itemHands[h].currentItem == item) return h;
            }
            return -1;
        }
    }
}
