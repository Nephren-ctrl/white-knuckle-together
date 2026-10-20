using HarmonyLib;
using JetBrains.Annotations;

// Trading with Mother in a shared world. See WKTMotherTrades. Not critical: without them, only the host can trade with her.
namespace WhiteKnuckleTogether {
    /// <summary>Mother holds out her hand for remote players with items too.</summary>
    [HarmonyPatch(typeof(DEN_Mother), nameof(DEN_Mother.Update))]
    internal sealed class MotherOffer_Patch {
        [UsedImplicitly]
        private static void Postfix(DEN_Mother __instance) => WKTMotherTrades.AfterMotherUpdate(__instance);
    }

    /// <summary>A client's hand-in goes to the host's Mother.</summary>
    [HarmonyPatch(typeof(UT_ItemInteractor), nameof(UT_ItemInteractor.Interact))]
    internal sealed class MotherHandIn_Patch {
        [UsedImplicitly]
        private static bool Prefix(UT_ItemInteractor __instance, Clickable.InteractionInfo __0) => WKTMotherTrades.BeforeInteract(__instance, __0);
    }

    /// <summary>Before the trade is worked out, so even what she says while working it out is heard by everyone.</summary>
    [HarmonyPatch(typeof(DEN_Mother), nameof(DEN_Mother.GrabItem))]
    internal sealed class MotherGrab_Patch {
        [UsedImplicitly]
        private static void Prefix(DEN_Mother __instance) => WKTMotherTrades.OnGrabbed(__instance);
    }

    [HarmonyPatch]
    internal sealed class MotherRelease_Patch {
        [UsedImplicitly]
        private static System.Collections.Generic.IEnumerable<System.Reflection.MethodBase> TargetMethods() {
            yield return AccessTools.Method(typeof(DEN_Mother), nameof(DEN_Mother.RemoveItem));
            yield return AccessTools.Method(typeof(DEN_Mother), nameof(DEN_Mother.DestroyItem));
        }

        [UsedImplicitly]
        private static void Postfix(DEN_Mother __instance) => WKTMotherTrades.OnReleased(__instance);
    }

    /// <summary>A remote player's trade pays them, not the host.</summary>
    [HarmonyPatch(typeof(ENT_Player), nameof(ENT_Player.AddPerk))]
    internal sealed class MotherRewardPerk_Patch {
        [UsedImplicitly]
        private static bool Prefix(Perk __0, ref Perk? __result) {
            if (WKTMotherTrades.BeforeAddPerk(__0)) return true;
            __result = null;
            return false;
        }
    }

    /// <summary>The flags a remote player's trade sets are theirs too.</summary>
    [HarmonyPatch(typeof(CL_GameManager), nameof(CL_GameManager.SetGameFlag))]
    internal sealed class MotherRewardFlag_Patch {
        [UsedImplicitly]
        private static void Postfix(string __0, bool __1) {
            if (WKTMotherTrades.IsRemoteTrade)
                WKTMotherTrades.OnFlagSet(__0, __1);
        }
    }

    /// <summary>Whether Mother trades at all depends on the giver's perks, not the host's.</summary>
    [HarmonyPatch(typeof(ENT_Player), nameof(ENT_Player.HasPerk))]
    internal sealed class MotherTraderPerk_Patch {
        [UsedImplicitly]
        private static bool Prefix(string __0, ref bool __result) {
            if (!WKTMotherTrades.IsRemoteTrade || !WKTMotherTrades.TryGetTraderPerk(__0, out var has)) return true;
            __result = has;
            return false;
        }
    }
}
