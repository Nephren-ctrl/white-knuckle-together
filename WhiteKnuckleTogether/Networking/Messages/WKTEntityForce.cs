using System;
using UnityEngine;
using WhiteKnuckleTogether.Networking.LiteNetLib;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// A push for a world entity: <c>AddForce(force)</c>, or <c>AddForceAtPosition(force, position)</c> when
    /// <see cref="AtPosition"/>. Positions are world-offset-corrected.
    /// </summary>
    public readonly struct WKTEntityForce {
        /// <summary>Stronger than any push the game gives, but rejects garbage.</summary>
        public const float MAX_FORCE = 1e5f;

        public readonly ushort Handle;
        public readonly bool AtPosition;
        public readonly Vector3 Force;
        /// <summary>Only meaningful with <see cref="AtPosition"/>.</summary>
        public readonly Vector3 Position;

        public WKTEntityForce(ushort handle, Vector3 force, bool atPosition = false, Vector3 position = default) {
            Handle = handle;
            Force = force;
            AtPosition = atPosition;
            Position = position;
        }

        public void Write(NetDataWriter writer) {
            writer.Put(Handle);
            writer.Put(AtPosition);
            writer.Put(Mathf.Clamp(Force.x, -MAX_FORCE, MAX_FORCE));
            writer.Put(Mathf.Clamp(Force.y, -MAX_FORCE, MAX_FORCE));
            writer.Put(Mathf.Clamp(Force.z, -MAX_FORCE, MAX_FORCE));
            if (AtPosition)
                WKTWire.Put(writer, Position);
        }

        /// <summary>Fails on truncation, handle 0, forces beyond <see cref="MAX_FORCE"/> or not finite, and bad positions.</summary>
        public static bool TryRead(NetDataReader reader, out WKTEntityForce force) {
            force = default;
            if (!reader.TryGetUShort(out var handle) || handle == 0 || !reader.TryGetBool(out var atPosition) || !reader.TryGetFloat(out var x) ||
                !reader.TryGetFloat(out var y) || !reader.TryGetFloat(out var z)) return false;
            // NaN fails the comparisons, so it is rejected too.
            if (!(Math.Abs(x) <= MAX_FORCE && Math.Abs(y) <= MAX_FORCE && Math.Abs(z) <= MAX_FORCE)) return false;
            var position = Vector3.zero;
            if (atPosition && !WKTWire.TryGetPosition(reader, out position)) return false;
            force = new WKTEntityForce(handle, new Vector3(x, y, z), atPosition, position);
            return true;
        }
    }
}
