using System;
using System.Collections.Generic;
using DarkMachine.AI;
using HarmonyLib;
using UnityEngine;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// How stalking creatures (the Face and the Teeth) pick whom to go for in a multiplayer run, on top of
    /// <see cref="WKTTargets"/>:
    /// <list type="bullet">
    /// <item>A player who hurts one draws it for a few seconds.</item>
    /// <item>Once aggressive (Face hunting, blocking or diving; Teeth chasing), it goes for the nearest player, sticking with
    /// them unless another is clearly closer, so a group gets attacked as a group.</item>
    /// <item>Otherwise it picks a player at random, weighted toward whoever has climbed furthest, and keeps them for a while.
    /// Players lower down are picked too, now and then.</item>
    /// </list>
    /// </summary>
    internal static class WKTProgressTargets {
        /// <summary>How long a stalking creature keeps the player it picked.</summary>
        public const float PICK_DURATION = 20f;
        /// <summary>How long a hit draws a creature to the player who dealt it.</summary>
        public const float FOCUS_DURATION = 8f;
        private static readonly string[] s_aggressiveStates = { "hunt", "block", "dive", "chase" };
        private static readonly Dictionary<int, Choice> s_picks = new();
        private static readonly Dictionary<int, Choice> s_focus = new();
        private static readonly byte[] s_ids = new byte[WKTLobbyConsts.MAX_PLAYERS + 1];
        private static readonly float[] s_heights = new float[WKTLobbyConsts.MAX_PLAYERS + 1];

        public static bool Covers(Component? self) => self is DEN_Face or DEN_Teeth;

        public static void Reset() {
            s_picks.Clear();
            s_focus.Clear();
        }

        /// <summary>Host only: a player hurt a stalking creature, which turns on them (<see cref="WKTTargets.LOCAL"/> for the host's player).</summary>
        public static void OnDamaged(Component creature, byte target) {
            if (Covers(creature))
                s_focus[creature.GetInstanceID()] = new Choice(target, Time.time + FOCUS_DURATION);
        }

        /// <summary>
        /// Whom a stalking creature goes for, or false to leave it to the nearest-player rule. <paramref name="isEligible"/> says
        /// whether a player can be gone for.
        /// </summary>
        public static bool TryChoose(Component self, Func<byte, bool> isEligible, out byte target) {
            target = WKTTargets.LOCAL;
            var id = self.GetInstanceID();
            var now = Time.time;
            if (s_focus.TryGetValue(id, out var focus)) {
                if (focus.Until > now && isEligible(focus.PlayerId)) {
                    target = focus.PlayerId;
                    return true;
                }
                s_focus.Remove(id);
            }
            if (self is AIGameEntity entity && IsAggressive(StateAccess.Get(entity)?.name)) return false;
            if (s_picks.TryGetValue(id, out var pick) && pick.Until > now && isEligible(pick.PlayerId)) {
                target = pick.PlayerId;
                return true;
            }
            var count = 0;
            if (isEligible(WKTTargets.LOCAL) && ENT_Player.playerObject is { } local) {
                s_ids[count] = WKTTargets.LOCAL;
                s_heights[count++] = local.transform.position.y;
            }
            for (var i = 0; i < WKTPlayerPositions.RemoteCount && count < s_ids.Length; i++) {
                var remote = WKTPlayerPositions.GetRemoteId(i);
                if (!isEligible(remote)) continue;
                s_ids[count] = remote;
                s_heights[count++] = WKTPlayerPositions.GetRemotePosition(i).y;
            }
            if (count == 0) return false;
            target = s_ids[WKTProgressWeights.Pick(s_heights.AsSpan(0, count), UnityEngine.Random.value)];
            s_picks[id] = new Choice(target, now + PICK_DURATION);
            return true;
        }

        public static bool IsAggressive(string? state) => state != null && Array.IndexOf(s_aggressiveStates, state) >= 0;

        /// <summary>Apart, so the rest of this class works where Harmony's field access cannot (tests).</summary>
        private static class StateAccess {
            private static readonly AccessTools.FieldRef<AIGameEntity, AIStateComponent?> s_state = AccessTools.FieldRefAccess<AIGameEntity, AIStateComponent?>("currentState");

            public static AIStateComponent? Get(AIGameEntity entity) => s_state(entity);
        }

        private readonly struct Choice {
            public readonly byte PlayerId;
            public readonly float Until;

            public Choice(byte playerId, float until) {
                PlayerId = playerId;
                Until = until;
            }
        }
    }

    /// <summary>The weighted pick toward the furthest-along player. Pure, so it can be tested without Unity.</summary>
    internal static class WKTProgressWeights {
        /// <summary>A player's weight shrinks by a factor of e for every this many meters they are below the highest player.</summary>
        public const float FALLOFF = 15f;
        /// <summary>Nobody is ever left out entirely.</summary>
        public const float MIN_WEIGHT = 0.1f;

        public static float WeightOf(float height, float highest) => Mathf.Max(MIN_WEIGHT, Mathf.Exp((height - highest) / FALLOFF));

        /// <summary>The index picked from <paramref name="heights"/> for a random <paramref name="roll"/> in [0, 1).</summary>
        public static int Pick(ReadOnlySpan<float> heights, float roll) {
            var highest = float.NegativeInfinity;
            foreach (var height in heights)
                highest = Mathf.Max(highest, height);
            var total = 0f;
            foreach (var height in heights)
                total += WeightOf(height, highest);
            var threshold = Mathf.Clamp01(roll) * total;
            for (var i = 0; i < heights.Length; i++) {
                threshold -= WeightOf(heights[i], highest);
                if (threshold < 0f) return i;
            }
            return heights.Length - 1;
        }
    }
}
