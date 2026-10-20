using UnityEngine;
using WhiteKnuckleTogether.Networking.LiteNetLib;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// A push a player holding a rope gives it, for the machine simulating the rope: their swing each physics step, or the
    /// one-off push of grabbing it. The <see cref="WKTMessageId.RopeForce"/> payload.
    /// </summary>
    public readonly struct WKTRopeForce {
        /// <summary>A one-off push, applied once, rather than a swing applied every physics step until it goes stale.</summary>
        public const byte FLAG_IMPULSE = 1 << 0;
        /// <summary>Far beyond any push the game gives a rope, but rejects garbage.</summary>
        public const float MAX_FORCE = 1e5f;
        public const int SIZE = sizeof(ulong) + WKTWire.VECTOR3_SIZE + sizeof(byte);
        private const byte c_known_flags = FLAG_IMPULSE;

        public readonly ulong Key;
        public readonly Vector3 Force;
        public readonly byte Flags;

        public WKTRopeForce(ulong key, Vector3 force, byte flags) {
            Key = key;
            Force = force;
            Flags = flags;
        }

        public bool IsImpulse => (Flags & FLAG_IMPULSE) != 0;

        public void Write(NetDataWriter writer) {
            writer.Put(Key);
            WKTWire.Put(writer, Vector3.ClampMagnitude(Force, MAX_FORCE));
            writer.Put(Flags);
        }

        /// <summary>Fails on truncation, key 0, a non-finite or oversized force and unknown flags.</summary>
        public static bool TryRead(NetDataReader reader, out WKTRopeForce force) {
            force = default;
            if (!reader.TryGetULong(out var key) || key == 0 || reader.AvailableBytes < WKTWire.VECTOR3_SIZE) return false;
            var vector = new Vector3(reader.GetFloat(), reader.GetFloat(), reader.GetFloat());
            // NaN fails the comparison, so it is rejected too.
            if (!(vector.sqrMagnitude <= MAX_FORCE * MAX_FORCE) || !reader.TryGetByte(out var flags) || (flags & ~c_known_flags) != 0) return false;
            force = new WKTRopeForce(key, vector, flags);
            return true;
        }
    }
}
