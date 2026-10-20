using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using JetBrains.Annotations;
using UnityEngine;

namespace WhiteKnuckleTogether {
    /// <summary>The local player grabbing a handhold on a flier rides it: its simulation moves to whoever should have it now.</summary>
    [WKTCriticalPatch(WKTPatchCategory.WorldSync), HarmonyPatch(typeof(CL_Handhold), nameof(CL_Handhold.Interact))]
    internal sealed class HandholdInteract_Patch {
        [UsedImplicitly]
        private static void Postfix(CL_Handhold __instance, Clickable.InteractionInfo? info) {
            var sync = WKTWorldSync.Instance;
            if (sync != null && info != null && info.player != null && info.player == ENT_Player.playerObject)
                sync.OnLocalHandholdGrab(__instance);
        }
    }

    /// <summary>The local player letting go of the last handhold of a flier they ride stops riding it.</summary>
    [WKTCriticalPatch(WKTPatchCategory.WorldSync), HarmonyPatch(typeof(CL_Handhold), nameof(CL_Handhold.StopInteract))]
    internal sealed class HandholdStopInteract_Patch {
        [UsedImplicitly]
        private static void Postfix(CL_Handhold __instance, ENT_Player p) {
            var sync = WKTWorldSync.Instance;
            if (sync != null && p != null && p == ENT_Player.playerObject)
                sync.OnLocalHandholdRelease(__instance);
        }
    }

    /// <summary>
    /// A flier's steering runs only where the flier is simulated. A puppet flier the local player rides only keeps the hands
    /// on it and shows it ridden, and its timer for the local player recharges while they are off it; its pose comes from the
    /// machine simulating it. Where it is simulated, a buck of the local player is shown to everyone. On the host, a flier
    /// remote players ride is steered by all its riders afterwards.
    /// </summary>
    [WKTCriticalPatch(WKTPatchCategory.WorldSync), HarmonyPatch(typeof(HandholdModule_FlierSteering), nameof(HandholdModule_FlierSteering.LateUpdate))]
    internal sealed class FlierSteering_Patch {
        private static readonly AccessTools.FieldRef<HandholdModule, CL_Handhold> s_handhold = AccessTools.FieldRefAccess<HandholdModule, CL_Handhold>("handhold");
        private static readonly int s_hanging = Animator.StringToHash("Hanging");
        private static readonly AccessTools.FieldRef<CL_Handhold, List<ENT_Player.Hand>> s_hands = AccessTools.FieldRefAccess<CL_Handhold, List<ENT_Player.Hand>>("hands");

        [UsedImplicitly]
        private static bool Prefix(HandholdModule_FlierSteering __instance, out int __state) {
            __state = -1;
            var flier = __instance.basicFlier;
            if (WKTWorldSync.Instance == null || flier == null || flier.IsDead()) return true;
            if (!WKTWorldSync.IsPuppet(flier)) {
                __state = WKTSharedHolds.GetLocalHits(__instance);
                return true;
            }
            var handhold = s_handhold(__instance);
            var holding = handhold != null && handhold.GetHolding();
            WKTSharedHolds.RechargePuppet(__instance, holding);
            if (holding) {
                foreach (var hand in s_hands(handhold!))
                    hand.UpdateHandPosition();
            }
            if (__instance.animator != null)
                __instance.animator.SetBool(s_hanging, holding);
            return false;
        }

        [UsedImplicitly]
        private static void Postfix(HandholdModule_FlierSteering __instance, int __state) {
            var sync = WKTWorldSync.Instance;
            if (sync == null) return;
            if (__state >= 0 && WKTSharedHolds.GetLocalHits(__instance) > __state)
                sync.SharedHolds.OnLocalGameBuck(__instance);
            sync.SharedHolds.AfterFlierSteering(__instance);
        }
    }

    /// <summary>Jumping off a puppet flier boosts it on the machine simulating it, which the rider's steering tells.</summary>
    [WKTCriticalPatch(WKTPatchCategory.WorldSync), HarmonyPatch]
    internal sealed class FlierSteeringJump_Patch {
        public static IEnumerable<MethodBase> GetTargets() {
            yield return AccessTools.Method(typeof(HandholdModule_FlierSteering), nameof(HandholdModule_FlierSteering.OnJump));
            yield return AccessTools.Method(typeof(HandholdModule_FlierSteering), nameof(HandholdModule_FlierSteering.OnDoubleJump));
        }

        [UsedImplicitly]
        private static IEnumerable<MethodBase> TargetMethods() => GetTargets();

        [UsedImplicitly]
        private static bool Prefix(HandholdModule_FlierSteering __instance) {
            var sync = WKTWorldSync.Instance;
            var flier = __instance.basicFlier;
            if (sync == null || flier == null || !WKTWorldSync.IsPuppet(flier)) return true;
            sync.SharedHolds.OnPuppetJump();
            return false;
        }
    }

    /// <summary>
    /// Holding a prop another machine simulates keeps pulling the local player toward it as the game does, but the prop
    /// itself is a kinematic puppet whose motion comes from that machine, so the game's velocity writes to it are skipped.
    /// </summary>
    [WKTCriticalPatch(WKTPatchCategory.WorldSync), HarmonyPatch]
    internal sealed class PuppetPropVelocity_Patch {
        public static readonly MethodInfo SetLinearVelocity = AccessTools.PropertySetter(typeof(Rigidbody), nameof(Rigidbody.linearVelocity));
        private static readonly MethodInfo s_setVelocity = AccessTools.Method(typeof(WKTSharedHolds), nameof(WKTSharedHolds.SetVelocity));

        public static IEnumerable<MethodBase> GetTargets() {
            yield return AccessTools.Method(typeof(ENT_Player), nameof(ENT_Player.GrabPropUpdate));
            yield return AccessTools.Method(typeof(ENT_Player), "DropGrabbedProp");
        }

        [UsedImplicitly]
        private static IEnumerable<MethodBase> TargetMethods() => GetTargets();

        [UsedImplicitly]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod) {
            var result = WKTCallRewriter.RedirectCalls(instructions, SetLinearVelocity, s_setVelocity, out var count);
            if (count == 0)
                throw new InvalidOperationException($"Found no prop velocity to guard in {__originalMethod.DeclaringType?.Name}.{__originalMethod.Name}.");
            return result;
        }
    }

    /// <summary>
    /// The game pulls a player toward a prop they hold once it is out of reach, so they hang from it. A prop other players
    /// hold too hangs from them: its pull keeps no lift (see <see cref="WKTHoldMath.HangForce"/>), or one player lifting a
    /// prop would carry another who grabbed it into the air. A stuck prop still holds a player up.
    /// </summary>
    [WKTCriticalPatch(WKTPatchCategory.WorldSync), HarmonyPatch(typeof(ENT_Player), nameof(ENT_Player.GrabPropUpdate))]
    internal sealed class PropHangLift_Patch {
        public static readonly MethodInfo PlayerAddForce = AccessTools.Method(typeof(ENT_Player), nameof(ENT_Player.AddForce), new[] { typeof(Vector3), typeof(string) });
        public static readonly MethodInfo EntityAddForce = AccessTools.Method(typeof(GameEntity), nameof(GameEntity.AddForce), new[] { typeof(Vector3), typeof(string) });
        private static readonly MethodInfo s_hangForce = AccessTools.Method(typeof(PropHangLift_Patch), nameof(AddHangForce));
        private static int s_hand;

        [UsedImplicitly]
        private static void Prefix(int hand) => s_hand = hand;

        [UsedImplicitly]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) {
            var result = WKTCallRewriter.RedirectCalls(instructions, PlayerAddForce, s_hangForce, out var count);
            result = WKTCallRewriter.RedirectCalls(result, EntityAddForce, s_hangForce, out var baseCount);
            if (count + baseCount != 1)
                throw new InvalidOperationException($"Expected one pull toward the held prop in ENT_Player.GrabPropUpdate, found {count + baseCount}.");
            return result;
        }

        /// <summary>In place of the player's own <c>AddForce</c> in their prop hold.</summary>
        public static void AddHangForce(GameEntity self, Vector3 force, string source) =>
            self.AddForce(WKTHoldMath.HangForce(force, IsAnchored(self)), source);

        private static bool IsAnchored(GameEntity self) {
            var sync = WKTWorldSync.Instance;
            if (sync == null || self is not ENT_Player player || player.hands == null || s_hand < 0 || s_hand >= player.hands.Length) return true;
            var prop = player.hands[s_hand]?.grabTarget;
            return prop == null || prop.stuck || !prop.TryGetComponent<WKTNetIdentity>(out var identity) || !sync.SharedHolds.IsHeldByOthers(identity);
        }
    }

    /// <summary>
    /// The game drags a player toward a prop they hold out of reach less the faster the prop moves. A client's copy of a prop
    /// others hold too is a kinematic puppet that reads as still, so the client was always dragged; on the host the same
    /// prop moves, pulled by the client, so the host never was and had the upper hand. While others hold it too, its speed
    /// reads as none here as well, so every holder is dragged alike.
    /// </summary>
    [WKTCriticalPatch(WKTPatchCategory.WorldSync), HarmonyPatch(typeof(ENT_Player), nameof(ENT_Player.GrabPropUpdate))]
    internal sealed class SharedPropDrag_Patch {
        public static readonly MethodInfo GetLinearVelocity = AccessTools.PropertyGetter(typeof(Rigidbody), nameof(Rigidbody.linearVelocity));
        private static readonly MethodInfo s_dragVelocity = AccessTools.Method(typeof(SharedPropDrag_Patch), nameof(DragVelocity));

        [UsedImplicitly]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) {
            var result = WKTCallRewriter.RedirectCalls(instructions, GetLinearVelocity, s_dragVelocity, out var count);
            if (count != 1)
                throw new InvalidOperationException($"Expected one read of the held prop's speed in ENT_Player.GrabPropUpdate, found {count}.");
            return result;
        }

        /// <summary>In place of the held prop's <c>linearVelocity</c>, read for how hard it drags its holder.</summary>
        public static Vector3 DragVelocity(Rigidbody body) {
            var sync = WKTWorldSync.Instance;
            if (sync == null || !body.TryGetComponent<WKTNetIdentity>(out var identity) || !sync.SharedHolds.IsHeldByOthers(identity)) return body.linearVelocity;
            return Vector3.zero;
        }
    }
}
