using HarmonyLib;
using UnityEngine;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// The Vent Thing grabbing any player in a synced co-op run, not only the host's. It is simulated by the host, and the
    /// game picks its target once, in <c>Awake</c>, then only acts on a grab when the target is the local player:
    /// <list type="bullet">
    /// <item>While its hand holds nobody, the host points it at the nearest player again (see <see cref="WKTTargets"/>).</item>
    /// <item>Its "is the target the player" checks also accept the engaged player's stand-in (<see cref="WKTPlayerProxy"/>), so
    /// the grab's effects reach them, as for other enemies.</item>
    /// <item>The grab's screen effects (the vignettes and the hand over the camera) are the held player's: the host shows them
    /// only when it is held itself, and the held player's machine shows them from the state the host sends (see
    /// <see cref="WKTVentThingPresenter"/>).</item>
    /// </list>
    /// </summary>
    internal static class WKTVentThingGrabs {
        private static readonly AccessTools.FieldRef<DEN_VentThing, bool> s_hasGrabbedTarget = AccessTools.FieldRefAccess<DEN_VentThing, bool>("hasGrabbedTarget");

        /// <summary>Host: points a Vent Thing whose hand holds nobody at the nearest player, and keeps one that holds someone on them.</summary>
        public static void Retarget(DEN_VentThing thing) {
            if (!WKTPlayerPositions.IsCoveringAllPlayers) return;
            // Whoever it holds stays its target until it lets go.
            var holding = s_hasGrabbedTarget(thing);
            WKTTargets.Pin(thing, holding);
            if (holding) return;
            var target = WKTTargets.TransformFor(thing);
            if (target != null && target != thing.target)
                thing.SetTarget(target);
        }

        /// <summary>Replaces <c>targetEntity == ENT_Player.playerObject</c>: the target is the player the Vent Thing engages.</summary>
        public static bool IsEngagedPlayer(GameEntity? entity, DEN_VentThing self) {
            if (entity == null) return false;
            if (entity == ENT_Player.playerObject) return true;
            return entity is WKTPlayerProxy && WKTTargets.TransformFor(self) == entity.transform;
        }

        /// <summary>Replaces the grab's <c>CL_UIManager.SetVignetteTarget</c>: only on the held player's own screen.</summary>
        public static void SetVignetteTarget(CL_UIManager ui, string name, float value, DEN_VentThing self) {
            if (WKTTargets.TargetOf(self) == WKTTargets.LOCAL)
                ui.SetVignetteTarget(name, value);
        }

        /// <summary>
        /// Host, after the Vent Thing's own update: while it goes for another player, its hand shows as the hand in the world,
        /// never as the hand over the host's camera.
        /// </summary>
        public static void ShowHandForOthers(DEN_VentThing thing) {
            if (!WKTPlayerPositions.IsCoveringAllPlayers) return;
            var heldHere = WKTTargets.TargetOf(thing) == WKTTargets.LOCAL;
            if (thing.handSpriteRenderer != null && thing.handSpriteRenderer.enabled != heldHere)
                thing.handSpriteRenderer.enabled = heldHere;
            if (heldHere) return;
            if (s_hasGrabbedTarget(thing) && thing.handRenderer != null && !thing.handRenderer.activeSelf)
                thing.handRenderer.SetActive(true);
        }
    }
}
