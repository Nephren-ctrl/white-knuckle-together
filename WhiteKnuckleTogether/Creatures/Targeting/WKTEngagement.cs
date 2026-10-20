using DarkMachine.AI;
using HarmonyLib;
using UnityEngine;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// Which enemy types hand their simulation to the player they are engaged with, and where each keeps its target, from
    /// the game's own code. Looked up once per type.
    /// <list type="bullet">
    /// <item>The basic denizen and navigator families (moth fliers, swimmers, hoppers, gantry turrets, crawlers, phages),
    /// basic fliers and turrets: <c>targetComponent</c> (an <see cref="AITargetComponent"/>) and <see cref="AIGameEntity.target"/>.</item>
    /// <item>Bloodbugs and drones: their own private <c>Target</c> record.</item>
    /// </list>
    /// Everything else stays the host's to simulate. That covers enemies driven from the host's player directly (hunters,
    /// chase killers, trackers, teeth, vent things, pseudopods, faces), barnacles (whose tongues clients already decide) and
    /// ticks (whose attaching the host decides).
    /// </summary>
    internal static class WKTEngagement {
        /// <summary>How long a newly given target stays remembered, as when the game retargets an enemy that took damage.</summary>
        private const float c_memory = 4f;
        private static readonly AccessTools.FieldRef<DEN_Bloodbug, DEN_Bloodbug.Target> s_bloodbugTarget =
            AccessTools.FieldRefAccess<DEN_Bloodbug, DEN_Bloodbug.Target>("bloodbugTarget");
        private static readonly AccessTools.FieldRef<DEN_Drone, DEN_Drone.Target> s_droneTarget = AccessTools.FieldRefAccess<DEN_Drone, DEN_Drone.Target>("droneTarget");

        /// <summary>Whether authority over <paramref name="entity"/> follows the player it is engaged with.</summary>
        public static bool Supports(GameEntity? entity) => entity != null && WKTEngagementKinds.Of(entity.GetType()) != WKTEngagementKind.None;

        /// <summary>What the enemy is going for right now, or null.</summary>
        public static Transform? ReadTarget(GameEntity entity) {
            switch (WKTEngagementKinds.Of(entity.GetType())) {
                case WKTEngagementKind.TargetComponent:
                    var component = GetTargetComponent(entity);
                    return component != null && component.HasTarget() ? component.target.targetTransform : null;
                case WKTEngagementKind.Bloodbug:
                    return s_bloodbugTarget((DEN_Bloodbug)entity)?.transform;
                case WKTEngagementKind.Drone:
                    return s_droneTarget((DEN_Drone)entity)?.transform;
                default:
                    return null;
            }
        }

        /// <summary>
        /// Points the enemy at <paramref name="target"/>, remembered as freshly seen, or clears its target when null, so its
        /// own AI carries on from there once it is simulated here.
        /// </summary>
        public static void SetTarget(GameEntity entity, Transform? target) {
            switch (WKTEngagementKinds.Of(entity.GetType())) {
                case WKTEngagementKind.TargetComponent:
                    var component = GetTargetComponent(entity);
                    if (component == null) return;
                    if (target != null)
                        component.SetTarget(target);
                    else
                        component.target.Clear();
                    break;
                case WKTEngagementKind.Bloodbug:
                    var bloodbug = (DEN_Bloodbug)entity;
                    SetRecord(s_bloodbugTarget(bloodbug), target, bloodbug.loseTargetTimer);
                    break;
                case WKTEngagementKind.Drone:
                    var drone = (DEN_Drone)entity;
                    SetRecord(s_droneTarget(drone), target, drone.loseTargetTimer);
                    break;
                default:
                    return;
            }
            var ai = (AIGameEntity)entity;
            ai.target = target;
            if (target != null && ai.sight != null)
                ai.sight.SetTargetAsVisible(target, c_memory);
        }

        /// <summary>
        /// Which player <paramref name="target"/> is: the local player's id for the local player, a remote player's id for their
        /// stand-in on the host, and <see cref="WKTEntityAuthority.NOBODY"/> for anything else.
        /// </summary>
        public static byte PlayerOf(Transform? target, byte localId) {
            if (target == null) return WKTEntityAuthority.NOBODY;
            var proxy = target.GetComponentInParent<WKTPlayerProxy>();
            if (proxy != null) return proxy.PlayerId;
            var player = target.GetComponentInParent<ENT_Player>();
            return player != null && player == ENT_Player.playerObject ? localId : WKTEntityAuthority.NOBODY;
        }

        private static void SetRecord(DEN_Bloodbug.Target? record, Transform? target, float memory) {
            if (record == null) return;
            if (target == null) {
                record.Clear();
                return;
            }
            record.transform = target;
            record.tagger = target.GetComponentInParent<ObjectTagger>();
            record.entity = target.GetComponentInParent<GameEntity>();
            record.isVisible = true;
            record.lastSighted = target.position;
            record.lastSightedTimer = memory;
        }

        private static void SetRecord(DEN_Drone.Target? record, Transform? target, float memory) {
            if (record == null) return;
            if (target == null) {
                record.Clear();
                return;
            }
            record.transform = target;
            record.tagger = target.GetComponentInParent<ObjectTagger>();
            record.entity = target.GetComponentInParent<GameEntity>();
            record.isVisible = true;
            record.lastSighted = target.position;
            record.lastSightedTimer = memory;
        }

        /// <summary>Whether a tick is latched onto its victim, as its own model shows (on a puppet, as the owner's state shows).</summary>
        public static bool IsLatched(GameEntity? entity) => entity is DEN_Hopper_Tick { attachModel: { activeSelf: true } };

        public static AITargetComponent? GetTargetComponent(GameEntity entity) =>
            entity switch {
                DEN_BasicDenizen basic => basic.targetComponent,
                DEN_BasicNavigator navigator => navigator.targetComponent,
                DEN_BasicFlier flier => flier.targetComponent,
                _ => null
            };
    }
}
