using UnityEngine;
using WhiteKnuckleTogether.Networking.LiteNetLib;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// How a rider steers a creature they ride: their move input, where their camera is and faces, and how often they jumped.
    /// Rider to host, unreliable. Only sent while the host simulates the creature because several players ride it, so the
    /// host can blend their steering. World-offset-corrected.
    /// </summary>
    public readonly struct WKTRiderInput {
        public const int SIZE = sizeof(ushort) + sizeof(float) * 2 + WKTWire.VECTOR3_SIZE * 2 + sizeof(byte);
        /// <summary>The largest move axis accepted. Input axes are at most 1; this leaves room for diagonal rounding.</summary>
        public const float MAX_MOVE = 1.5f;

        public ushort Handle { get; }
        public Vector2 Move { get; }
        public Vector3 CameraPosition { get; }
        /// <summary>Unit length.</summary>
        public Vector3 CameraForward { get; }
        /// <summary>Counts the rider's jumps off the creature, wrapping, so a lost message never loses one.</summary>
        public byte Jumps { get; }

        public WKTRiderInput(ushort handle, Vector2 move, Vector3 cameraPosition, Vector3 cameraForward, byte jumps) {
            Handle = handle;
            Move = move;
            CameraPosition = cameraPosition;
            CameraForward = cameraForward;
            Jumps = jumps;
        }

        public void Write(NetDataWriter writer) {
            writer.Put(Handle);
            writer.Put(Move.x);
            writer.Put(Move.y);
            WKTWire.Put(writer, CameraPosition);
            WKTWire.Put(writer, CameraForward);
            writer.Put(Jumps);
        }

        /// <summary>
        /// Fails on truncation, handle 0, move axes that are not finite or above <see cref="MAX_MOVE"/>, bad positions and
        /// camera directions that are not finite or nearly zero. The direction is renormalized.
        /// </summary>
        public static bool TryRead(NetDataReader reader, out WKTRiderInput value) {
            value = default;
            if (!reader.TryGetUShort(out var handle) || handle == 0) return false;
            if (!reader.TryGetFloat(out var x) || !reader.TryGetFloat(out var y) || !(Mathf.Abs(x) <= MAX_MOVE) || !(Mathf.Abs(y) <= MAX_MOVE)) return false;
            if (!WKTWire.TryGetPosition(reader, out var position) || !WKTWire.TryGetPosition(reader, out var forward)) return false;
            var length = forward.magnitude;
            if (!(length > 0.01f)) return false;
            if (!reader.TryGetByte(out var jumps)) return false;
            value = new WKTRiderInput(handle, new Vector2(x, y), position, forward / length, jumps);
            return true;
        }
    }
}
