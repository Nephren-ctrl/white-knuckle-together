using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using JetBrains.Annotations;
using UnityEngine;

namespace WhiteKnuckleTogether {
    /// <summary>Mother judges the group, not each player on their own machine (see <see cref="WKTMotherJudgement"/>).</summary>
    [HarmonyPatch(typeof(UT_StatChecker), nameof(UT_StatChecker.Check))]
    internal sealed class MotherJudgement_Patch {
        [UsedImplicitly]
        private static bool Prefix(UT_StatChecker __instance) {
            var judgement = WKTMotherJudgement.Instance;
            if (judgement == null || !WKTMotherJudgement.IsJudgement(__instance, out var flag) || flag == null) return true;
            judgement.OnLocalArrived(__instance, flag);
            return false;
        }
    }

    /// <summary>
    /// A force mover pulls each player to their own spot around its target in a multiplayer run, so players lifted together,
    /// like to Mother's platform, do not pile into one another.
    /// </summary>
    [HarmonyPatch(typeof(UT_PlayerForceMover), "FixedUpdate")]
    internal sealed class ForceMoverOffset_Patch {
        [UsedImplicitly]
        private static bool Prefix(UT_PlayerForceMover __instance) {
            var network = WKT.InstanceOrNull?.Network;
            if (network == null || !network.IsMultiplayerRun || !__instance.active || __instance.targetPoint == null) return true;
            var player = ENT_Player.GetPlayer();
            if (player == null) return false;
            var target = __instance.targetPoint.position + WKTPlayerSpots.Offset(network.Session.LocalSlot);
            var toTarget = target - player.transform.position;
            if (__instance.limitByDistance && toTarget.magnitude > __instance.distanceLimit) return false;
            player.SetTempGravityMult(__instance.activePlayerGravity);
            player.SetTempFrictionMultiplier(__instance.playerDragMultiplier);
            player.AddForce(Vector3.ClampMagnitude(toTarget, 1f) * __instance.playerPullForce * Time.fixedDeltaTime);
            if (__instance.resetFallingDistance)
                player.ResetFallingHeight();
            return false;
        }
    }

    /// <summary>
    /// Mother's wrath empties the bag of each player it throws on their own machine, never the host's for everyone, and
    /// spreads what falls out around them instead of dropping it all on one spot.
    /// </summary>
    [HarmonyPatch(typeof(UT_ItemDumper), nameof(UT_ItemDumper.Dump))]
    internal sealed class ItemDumperSpread_Patch {
        private static readonly List<Item> s_items = new();

        [UsedImplicitly]
        private static bool Prefix(UT_ItemDumper __instance) {
            var network = WKT.InstanceOrNull?.Network;
            if (network == null || !network.IsMultiplayerRun || Inventory.instance == null) return true;
            __instance.StartCoroutine(Dump(__instance));
            return false;
        }

        private static IEnumerator Dump(UT_ItemDumper dumper) {
            s_items.Clear();
            foreach (var item in Inventory.instance.GetItems()) {
                if (dumper.destroyItemTags is { Count: > 0 } && item.HasTagInList(dumper.destroyItemTags))
                    item.Destroy();
                else
                    s_items.Add(item);
            }
            // Copied so another dump while this one runs cannot change what is dropped.
            var items = s_items.ToArray();
            s_items.Clear();
            yield return null;
            for (var i = 0; i < items.Length && i < dumper.maxDumpedItems; i++) {
                var player = ENT_Player.GetPlayer();
                if (player == null || Inventory.instance == null) yield break;
                var item = items[i];
                if (item == null || item.hasBeenDestroyed) continue;
                var position = player.transform.position + WKTDropSpread.Offset(i, dumper.maxDumpedItems);
                Inventory.instance.DropItemIntoWorld(item, position);
                var dropObject = item.GetDropObject();
                if (dropObject != null && dropObject.TryGetComponent<Rigidbody>(out var body)) {
                    body.isKinematic = false;
                    body.AddForce(Random.insideUnitSphere * dumper.dumpForce, ForceMode.VelocityChange);
                    body.angularVelocity = Random.insideUnitSphere * 15f;
                }
                yield return new WaitForSeconds(dumper.dumpRate);
            }
        }
    }
}
