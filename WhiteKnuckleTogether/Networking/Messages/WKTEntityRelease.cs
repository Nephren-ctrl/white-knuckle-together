using UnityEngine;
using WhiteKnuckleTogether.Networking.LiteNetLib;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// A player let go of a world object they held with every hand. Client to host, reliable. It carries the object's motion
    /// on the releasing player's screen, so a throw carries on wherever the object is simulated next. World-offset-corrected.
    /// </summary>
    public readonly struct WKTEntityRelease {
        public const int SIZE = sizeof(ushort) + WKTWire.VECTOR3_SIZE * 3 + WKTWire.QUATERNION_SIZE;

        public ushort Handle { get; }
        public WKTEntityPose Pose { get; }
        public Vector3 Velocity { get; }
        public Vector3 AngularVelocity { get; }

        public WKTEntityRelease(ushort handle, in WKTEntityPose pose, Vector3 velocity, Vector3 angularVelocity) {
            Handle = handle;
            Pose = pose;
            Velocity = velocity;
            AngularVelocity = angularVelocity;
        }

        public void Write(NetDataWriter writer) {
            writer.Put(Handle);
            WKTWire.Put(writer, Pose.Position);
            WKTWire.Put(writer, Pose.Rotation);
            WKTWire.Put(writer, Velocity);
            WKTWire.Put(writer, AngularVelocity);
        }

        /// <summary>Fails on truncation, handle 0, bad poses and speeds over <see cref="WKTWire.MAX_SPEED"/>.</summary>
        public static bool TryRead(NetDataReader reader, out WKTEntityRelease value) {
            value = default;
            if (!reader.TryGetUShort(out var handle) || handle == 0) return false;
            if (!WKTWire.TryGetPosition(reader, out var position) || !WKTWire.TryGetRotation(reader, out var rotation)) return false;
            if (!WKTWire.TryGetVelocity(reader, out var velocity) || !WKTWire.TryGetVelocity(reader, out var angular)) return false;
            value = new WKTEntityRelease(handle, new WKTEntityPose(position, rotation), velocity, angular);
            return true;
        }
    }
}
