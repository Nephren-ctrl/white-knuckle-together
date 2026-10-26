using System.Collections.Generic;
using Pathfinding;
using UnityEngine;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// Which players a Teeth cannot get to, from its own pathfinding, so it goes for someone it can (see <see cref="WKTTargets"/>).
    /// A path the Teeth asked for toward its target that fails, or ends well short of the target (a player hiding in a vent,
    /// on a ledge the graph does not reach), marks that player out of reach for a while. A path that gets there clears it.
    /// The Teeth only paths toward its target while chasing, so the host also asks the pathfinder about every nearby player
    /// every few seconds (<see cref="Probe"/>): a wandering or burrowed Teeth would otherwise keep after a player it can never
    /// reach. It only ever steers the Teeth away from someone while another living player is in reach. Host only.
    /// </summary>
    internal static class WKTTeethReach {
        /// <summary>How far from the target a path may end and still count as reaching them.</summary>
        public const float REACH_DISTANCE = 3f;
        /// <summary>How close to the target a path's goal must be for the path to be judged at all: wandering paths go elsewhere.</summary>
        public const float GOAL_DISTANCE = 2f;
        /// <summary>How long a player stays out of reach after a path to them failed, before the Teeth tries them again.</summary>
        public const float FORGET_SECONDS = 12f;
        /// <summary>How often the host asks whether each Teeth can get to each player.</summary>
        public const float PROBE_INTERVAL = 2f;
        /// <summary>Players further than this from a Teeth are not asked about: the search would cover the whole graph.</summary>
        public const float PROBE_RANGE = 80f;
        private static readonly Dictionary<(int Teeth, byte Player), float> s_unreachable = new();
        /// <summary>Probes the pathfinder has not answered yet, so none is asked twice.</summary>
        private static readonly HashSet<(int Teeth, byte Player)> s_probing = new();
        private static float s_nextProbe;

        public static void Reset() {
            s_unreachable.Clear();
            s_probing.Clear();
            s_nextProbe = 0f;
        }

        /// <summary>
        /// Whether a finished path counts as reaching a target at <paramref name="target"/>, or null when it was not aimed at the
        /// target at all. <paramref name="failed"/> is whether the pathfinder gave up. Pure.
        /// </summary>
        public static bool? Judges(bool failed, Vector3 goal, Vector3 end, Vector3 target) {
            if ((goal - target).sqrMagnitude > GOAL_DISTANCE * GOAL_DISTANCE) return null;
            return !failed && (end - goal).sqrMagnitude <= REACH_DISTANCE * REACH_DISTANCE;
        }

        /// <summary>A Teeth's path toward its current target finished: remembers whether the target can be reached.</summary>
        public static void OnPathComplete(DEN_Teeth teeth, bool failed, Vector3 goal, Vector3 end) {
            var target = teeth.target;
            if (target == null || Judges(failed, goal, end, target.position) is not { } reached) return;
            Mark((teeth.GetInstanceID(), WKTTargets.TargetOf(teeth)), reached);
        }

        /// <summary>
        /// Every <see cref="PROBE_INTERVAL"/>, asks the pathfinder for a way from each of the host's Teeth to each living player
        /// within <see cref="PROBE_RANGE"/>, through the ground the Teeth itself may walk.
        /// </summary>
        public static void Probe(List<WKTNetIdentity> teeth) {
            if (!WKTPlayerPositions.IsCoveringAllPlayers || Time.time < s_nextProbe || AstarPath.active == null) return;
            s_nextProbe = Time.time + PROBE_INTERVAL;
            foreach (var identity in teeth) {
                if (identity == null || identity.IsPuppet || identity.Entity is not DEN_Teeth entity || !entity.isActiveAndEnabled) continue;
                var tags = entity.TryGetComponent<Seeker>(out var seeker) ? seeker.traversableTags : -1;
                if (ENT_Player.playerObject is { dead: false } local)
                    StartProbe(entity, WKTTargets.LOCAL, local.transform.position, tags);
                for (var i = 0; i < WKTPlayerPositions.RemoteCount; i++) {
                    if (!WKTPlayerPositions.IsRemoteDead(i))
                        StartProbe(entity, WKTPlayerPositions.GetRemoteId(i), WKTPlayerPositions.GetRemotePosition(i), tags);
                }
            }
        }

        /// <summary>Whether <paramref name="teeth"/> found no way to <paramref name="playerId"/> lately.</summary>
        public static bool IsUnreachable(Component teeth, byte playerId) =>
            s_unreachable.Count > 0 && s_unreachable.TryGetValue((teeth.GetInstanceID(), playerId), out var until) && until > Time.time;

        /// <summary>Whether <paramref name="teeth"/> found no way to anyone lately: a cheap check before looking closer.</summary>
        public static bool HasUnreachable(Component teeth) {
            if (s_unreachable.Count == 0) return false;
            var id = teeth.GetInstanceID();
            var now = Time.time;
            foreach (var pair in s_unreachable) {
                if (pair.Key.Teeth == id && pair.Value > now) return true;
            }
            return false;
        }

        private static void StartProbe(DEN_Teeth teeth, byte playerId, Vector3 target, int tags) {
            var from = teeth.transform.position;
            if ((target - from).sqrMagnitude > PROBE_RANGE * PROBE_RANGE) return;
            var key = (teeth.GetInstanceID(), playerId);
            if (!s_probing.Add(key)) return;
            var path = ABPath.Construct(from, target, p => OnProbed(key, p));
            path.enabledTags = tags;
            AstarPath.StartPath(path);
        }

        private static void OnProbed((int Teeth, byte Player) key, Path path) {
            // Gone with a reset (a new run) while the pathfinder worked on it.
            if (!s_probing.Remove(key) || path is not ABPath probe) return;
            Mark(key, Judges(probe.error, probe.originalEndPoint, probe.endPoint, probe.originalEndPoint) == true);
        }

        private static void Mark((int Teeth, byte Player) key, bool reached) {
            if (reached)
                s_unreachable.Remove(key);
            else
                s_unreachable[key] = Time.time + FORGET_SECONDS;
        }
    }
}
