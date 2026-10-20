using System;
using UnityEngine;

namespace WhiteKnuckleTogether {
    /// <summary>The rules of a race (see <see cref="WKTRace"/>), apart from Unity objects so they can be tested.</summary>
    internal static class WKTRaceRules {
        /// <summary>No byte is a player: nobody won.</summary>
        public const byte NO_WINNER = byte.MaxValue;
        /// <summary>How long after the start the orb begins to hurt, so everyone gets going first. Only at the start of the race.</summary>
        public const float GRACE_SECONDS = 10f;
        /// <summary>How fast the orb's center can move after the leader, in m/s, while close to them. Fast enough for any steady climb.</summary>
        public const float ORB_SPEED = 12f;
        public const float ORB_SMOOTH_TIME = 0.6f;
        /// <summary>
        /// How much faster the orb may move for each radius it lags behind the leader: a leader outpacing it pulls it along
        /// faster the further ahead they get. At <see cref="MAX_LAG"/> it may move twice as fast.
        /// </summary>
        public const float CATCH_UP_PER_RADIUS = 4f;
        /// <summary>The furthest the orb's center ever is from the leader, as a fraction of its radius.</summary>
        public const float MAX_LAG = 0.25f;
        /// <summary>How long a player can be outside the orb (after the grace) before it starts to hurt.</summary>
        public const float DAMAGE_DELAY_SECONDS = 1f;
        /// <summary>
        /// Damage outside the orb, per second, as a fraction of full health: once <see cref="DAMAGE_DELAY_SECONDS"/> have passed, and
        /// <see cref="RAMP_SECONDS"/> after that. The ramp adds up to full health, so a full-health player without perks dies about
        /// 10 seconds after leaving the orb.
        /// </summary>
        public const float MIN_DAMAGE_PER_SECOND = 0.02f;
        public const float MAX_DAMAGE_PER_SECOND = 0.202f;
        public const float RAMP_SECONDS = 9f;
        /// <summary>Damage is dealt in steps this far apart, so its hurt effects play as a pulse rather than every frame.</summary>
        public const float DAMAGE_TICK_SECONDS = 0.5f;
        /// <summary>The leader's body is held at the orb's center by this much above their feet.</summary>
        public const float CENTER_HEIGHT = 1f;

        /// <summary>
        /// The orb's center after <paramref name="deltaTime"/>, following <paramref name="target"/> (the leader) smoothly, faster
        /// the further behind it is, and never more than <see cref="MAX_LAG"/> of <paramref name="radius"/> away.
        /// </summary>
        public static Vector3 MoveOrb(Vector3 center, Vector3 target, ref Vector3 velocity, float radius, float deltaTime) {
            var lag = radius > 0f ? Vector3.Distance(center, target) / radius : 0f;
            var maxSpeed = ORB_SPEED * (1f + CATCH_UP_PER_RADIUS * lag);
            var moved = Vector3.SmoothDamp(center, target, ref velocity, ORB_SMOOTH_TIME, maxSpeed, deltaTime);
            return ClampLag(moved, target, radius);
        }

        /// <summary><paramref name="center"/>, pulled toward <paramref name="target"/> until it is at most <see cref="MAX_LAG"/> of <paramref name="radius"/> away.</summary>
        public static Vector3 ClampLag(Vector3 center, Vector3 target, float radius) {
            var maxLag = MAX_LAG * radius;
            var offset = center - target;
            return offset.sqrMagnitude > maxLag * maxLag ? target + offset.normalized * maxLag : center;
        }

        public static float GetRadius(OrbRadiusId id) => id switch {
            OrbRadiusId.Small => 15f,
            OrbRadiusId.Large => 45f,
            _ => 30f
        };

        /// <summary>Damage per second, as a fraction of full health, after <paramref name="secondsOutside"/> outside the orb.</summary>
        public static float GetDamagePerSecond(float secondsOutside) {
            if (!IsDamaging(secondsOutside)) return 0f;
            return Mathf.Lerp(MIN_DAMAGE_PER_SECOND, MAX_DAMAGE_PER_SECOND, Mathf.Clamp01((secondsOutside - DAMAGE_DELAY_SECONDS) / RAMP_SECONDS));
        }

        /// <summary>Whether a player <paramref name="secondsOutside"/> outside the orb is past the delay and getting hurt.</summary>
        public static bool IsDamaging(float secondsOutside) => secondsOutside >= DAMAGE_DELAY_SECONDS;

        /// <summary>Whether the orb hurts <paramref name="secondsSinceStart"/> into the race.</summary>
        public static bool IsHurting(float secondsSinceStart) => secondsSinceStart >= GRACE_SECONDS;

        /// <summary>Whether <paramref name="position"/> is outside the orb.</summary>
        public static bool IsOutside(Vector3 position, Vector3 center, float radius) => (position - center).sqrMagnitude > radius * radius;

        /// <summary>
        /// The order players finished the race in, into <paramref name="standings"/>: the winner first, then whoever was still
        /// climbing (sharing second, in <paramref name="players"/>' order), then the eliminated, the last to fall first.
        /// <paramref name="eliminated"/> lists eliminations in the order they happened; players not in <paramref name="players"/>
        /// (who left) are skipped. Returns how many were written.
        /// </summary>
        public static int GetStandings(byte winner, ReadOnlySpan<byte> players, ReadOnlySpan<bool> dead, ReadOnlySpan<byte> eliminated, Span<byte> standings) {
            var count = 0;
            if (winner != NO_WINNER && players.IndexOf(winner) >= 0)
                standings[count++] = winner;
            for (var i = 0; i < players.Length; i++) {
                if (!dead[i] && players[i] != winner)
                    standings[count++] = players[i];
            }
            for (var i = eliminated.Length - 1; i >= 0; i--) {
                var id = eliminated[i];
                var index = players.IndexOf(id);
                if (id == winner || index < 0 || !dead[index] || standings.Slice(0, count).IndexOf(id) >= 0) continue;
                standings[count++] = id;
            }
            // The dead whose fall this machine missed (they died before it joined) come last.
            for (var i = 0; i < players.Length; i++) {
                if (dead[i] && players[i] != winner && standings.Slice(0, count).IndexOf(players[i]) < 0)
                    standings[count++] = players[i];
            }
            return count;
        }

        /// <summary>
        /// Whether the race is over by elimination: one player left alive out of several who started (they win), or nobody
        /// alive at all (nobody wins). A race started alone only ends when that player dies or finishes.
        /// </summary>
        public static bool IsDecided(int started, int alive, byte lastAlive, out byte winner) {
            winner = NO_WINNER;
            if (alive == 0) return true;
            if (started < 2 || alive > 1) return false;
            winner = lastAlive;
            return true;
        }
    }
}
