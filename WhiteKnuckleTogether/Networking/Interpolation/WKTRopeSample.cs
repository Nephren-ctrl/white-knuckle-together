using UnityEngine;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// A received rope state as <see cref="WKTRopeSnapshotBuffer"/> replays it: which stored node slots to blend and how far,
    /// rather than the nodes themselves, so samples stay small and allocation free. <see cref="Position"/> is the rope's end
    /// in its own space, which tells teleports apart.
    /// </summary>
    public readonly struct WKTRopeSample : IWKTInterpolatable<WKTRopeSample> {
        public readonly int From;
        public readonly int To;
        public readonly float T;
        public readonly Vector3 Velocity;

        public WKTRopeSample(int slot, Vector3 end, Vector3 velocity) : this(slot, slot, 0f, end, velocity) { }

        private WKTRopeSample(int from, int to, float t, Vector3 end, Vector3 velocity) {
            From = from;
            To = to;
            T = t;
            Position = end;
            Velocity = velocity;
        }

        public Vector3 Position { get; }

        public WKTRopeSample LerpTo(in WKTRopeSample to, float t) =>
            new(From, to.From, t, Position + (to.Position - Position) * t, Velocity + (to.Velocity - Velocity) * t);

        /// <summary>Ropes are never extrapolated: the nodes hold.</summary>
        public WKTRopeSample WithPosition(Vector3 position) => this;
    }
}
