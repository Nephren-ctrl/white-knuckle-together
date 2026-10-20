using System;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// Where a looping path mover (<c>UT_PathMover</c>) is at a point in its loop, timed the way <c>MoveAlongPath</c> moves it.
    /// The loop starts on arriving at node 0. It waits there for that node's wait time, then travels to node 1, waits there,
    /// and so on, back to node 0. Pure, so it can be tested without Unity.
    /// <para>
    /// The game's own <c>Awake</c> gets this wrong for any offset but 0: it compares against one segment instead of the time
    /// so far, aims at the node behind and leaves the target at node 0 while waiting. Set from it, a mover jumped back or cut
    /// across its loop once it started.
    /// </para>
    /// </summary>
    internal readonly struct WKTPathPhase {
        /// <summary>The node the mover waits at, or last left.</summary>
        public readonly int From;
        /// <summary>How far along the trip from <see cref="From"/> to the next node it is, from 0 to 1. 0 while waiting.</summary>
        public readonly float Progress;
        /// <summary>Seconds still to wait at <see cref="From"/>, or -1 while travelling.</summary>
        public readonly float WaitLeft;

        public WKTPathPhase(int from, float progress, float waitLeft) {
            From = from;
            Progress = progress;
            WaitLeft = waitLeft;
        }

        public bool IsWaiting => WaitLeft >= 0f;

        /// <summary>
        /// The phase at <paramref name="time"/> seconds into a loop. <paramref name="waits"/>[i] is node i's wait time;
        /// <paramref name="travelTimes"/>[i] is how long the trip from node i to the next one takes. False when the loop
        /// takes no time or the inputs don't make sense.
        /// </summary>
        public static bool TryFind(ReadOnlySpan<float> waits, ReadOnlySpan<float> travelTimes, double time, out WKTPathPhase phase) {
            phase = default;
            var count = waits.Length;
            if (count == 0 || travelTimes.Length != count || !double.IsFinite(time)) return false;
            var period = 0d;
            for (var i = 0; i < count; i++) {
                if (!float.IsFinite(waits[i]) || !float.IsFinite(travelTimes[i]) || travelTimes[i] < 0f) return false;
                period += Math.Max(waits[i], 0f) + travelTimes[i];
            }
            if (period <= 1e-6) return false;
            var left = time % period;
            if (left < 0d)
                left += period;
            for (var i = 0; i < count; i++) {
                var wait = Math.Max(waits[i], 0f);
                if (left < wait) {
                    phase = new WKTPathPhase(i, 0f, (float)(wait - left));
                    return true;
                }
                left -= wait;
                var travel = travelTimes[i];
                if (left < travel) {
                    phase = new WKTPathPhase(i, (float)(left / travel), -1f);
                    return true;
                }
                left -= travel;
            }
            // Only rounding gets here: the very end of the loop is its start.
            phase = new WKTPathPhase(0, 0f, Math.Max(waits[0], 0f));
            return true;
        }
    }
}
