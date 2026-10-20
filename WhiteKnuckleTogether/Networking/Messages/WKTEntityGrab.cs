using UnityEngine;
using WhiteKnuckleTogether.Networking.LiteNetLib;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// A player grabbed a world object: a prop, or a handhold on a creature they ride. Client to host, reliable. The anchor is
    /// where on the object they hold it, in the object's local space, so the host can pull a prop several players hold.
    /// </summary>
    public readonly struct WKTEntityGrab {
        public const int SIZE = sizeof(ushort) + sizeof(byte) + WKTWire.VECTOR3_SIZE;
        /// <summary>The farthest a hold point may be from the object's origin. Generous, but rejects garbage.</summary>
        public const float MAX_ANCHOR = 100f;

        public ushort Handle { get; }
        /// <summary>Which handhold of the object, in child order. 0 for props.</summary>
        public byte Part { get; }
        public Vector3 Anchor { get; }

        public WKTEntityGrab(ushort handle, byte part, Vector3 anchor) {
            Handle = handle;
            Part = part;
            Anchor = anchor;
        }

        public void Write(NetDataWriter writer) {
            writer.Put(Handle);
            writer.Put(Part);
            WKTWire.Put(writer, Anchor);
        }

        /// <summary>Fails on truncation, handle 0, and anchors that are not finite or farther than <see cref="MAX_ANCHOR"/>.</summary>
        public static bool TryRead(NetDataReader reader, out WKTEntityGrab value) {
            value = default;
            if (!reader.TryGetUShort(out var handle) || handle == 0 || !reader.TryGetByte(out var part)) return false;
            if (!WKTWire.TryGetPosition(reader, out var anchor) || !(anchor.sqrMagnitude <= MAX_ANCHOR * MAX_ANCHOR)) return false;
            value = new WKTEntityGrab(handle, part, anchor);
            return true;
        }
    }
}
