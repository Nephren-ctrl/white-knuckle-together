using System.Collections.Generic;
using UnityEngine;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// Which players a Teeth cannot get to, from its own pathfinding, so it goes for someone it can (see <see cref="WKTTargets"/>).
    /// A path the Teeth asked for toward its target that fails, or ends well short of the target (a player hiding in a vent,
    /// on a ledge the graph does not reach), marks that player out of reach for a while. A path that gets there clears it.
    /// It only ever steers the Teeth away from someone while another living player is in reach. Host only.
    /// </summary>
    internal static class WKTTeethReach {
        /// <summary>How far from the target a path may end and still count as reaching them.</summary>
        public const float REACH_DISTANCE = 3f;
        /// <summary>How close to the target a path's goal must be for the path to be judged at all: wandering paths go elsewhere.</summary>
        public const float GOAL_DISTANCE = 2f;
        /// <summary>How long a player stays out of reach after a path to them failed, before the Teeth tries them again.</summary>
        public const float FORGET_SECONDS = 12f;
        private static readonly Dictionary<(int Teeth, byte Player), float> s_unreachable = new();

        public static void Reset() => s_unreachable.Clear();

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
            var key = (teeth.GetInstanceID(), WKTTargets.TargetOf(teeth));
            if (reached)
                s_unreachable.Remove(key);
            else
                s_unreachable[key] = Time.time + FORGET_SECONDS;
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
    }
}
