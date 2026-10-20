using UnityEngine;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// What the machine simulating a world object shares beyond its pose, so another machine can take the object over: how
    /// it moves and spins, and which player an enemy is engaged with.
    /// </summary>
    public readonly struct WKTEntityMotion {
        public Vector3 Velocity { get; }
        public Vector3 AngularVelocity { get; }
        /// <summary>A player id, or <see cref="WKTEntityAuthority.NOBODY"/>.</summary>
        public byte Engaged { get; }

        public WKTEntityMotion(Vector3 velocity, Vector3 angularVelocity, byte engaged) {
            Velocity = velocity;
            AngularVelocity = angularVelocity;
            Engaged = engaged;
        }
    }
}
