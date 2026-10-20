using UnityEngine;
using WhiteKnuckleTogether.Networking.LiteNetLib;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// Which machine simulates a world object now. Sent by the host to everyone, reliably, whenever that changes. It carries
    /// the object's last known motion, so the new owner carries on from where everyone saw it. World-offset-corrected.
    /// </summary>
    public readonly struct WKTEntityAuthority {
        /// <summary>The owner meaning the host simulates the object, and the engaged player meaning nobody.</summary>
        public const byte HOST = 255;
        public const byte NOBODY = 255;
        public const int SIZE = sizeof(ushort) + sizeof(byte) + WKTWire.VECTOR3_SIZE * 3 + WKTWire.QUATERNION_SIZE + sizeof(float) + sizeof(byte);
        /// <summary>The most health accepted, as in <see cref="WKTEntityState"/>.</summary>
        private const float c_max_health = 1e7f;

        public ushort Handle { get; }
        /// <summary>The player id that simulates the object, or <see cref="HOST"/>.</summary>
        public byte Owner { get; }
        public WKTEntityPose Pose { get; }
        public Vector3 Velocity { get; }
        public Vector3 AngularVelocity { get; }
        public float Health { get; }
        /// <summary>The player the enemy is engaged with, or <see cref="NOBODY"/>.</summary>
        public byte Engaged { get; }

        public WKTEntityAuthority(ushort handle, byte owner, in WKTEntityPose pose, Vector3 velocity, Vector3 angularVelocity, float health, byte engaged) {
            Handle = handle;
            Owner = owner;
            Pose = pose;
            Velocity = velocity;
            AngularVelocity = angularVelocity;
            Health = health;
            Engaged = engaged;
        }

        public void Write(NetDataWriter writer) {
            writer.Put(Handle);
            writer.Put(Owner);
            WKTWire.Put(writer, Pose.Position);
            WKTWire.Put(writer, Pose.Rotation);
            WKTWire.Put(writer, Velocity);
            WKTWire.Put(writer, AngularVelocity);
            writer.Put(Health);
            writer.Put(Engaged);
        }

        /// <summary>Fails on truncation, handle 0, bad poses, speeds over <see cref="WKTWire.MAX_SPEED"/> and health outside [-1e7, 1e7].</summary>
        public static bool TryRead(NetDataReader reader, out WKTEntityAuthority value) {
            value = default;
            if (!reader.TryGetUShort(out var handle) || handle == 0 || !reader.TryGetByte(out var owner)) return false;
            if (!WKTWire.TryGetPosition(reader, out var position) || !WKTWire.TryGetRotation(reader, out var rotation)) return false;
            if (!WKTWire.TryGetVelocity(reader, out var velocity) || !WKTWire.TryGetVelocity(reader, out var angular)) return false;
            if (!reader.TryGetFloat(out var health) || !(health >= -c_max_health && health <= c_max_health)) return false;
            if (!reader.TryGetByte(out var engaged)) return false;
            value = new WKTEntityAuthority(handle, owner, new WKTEntityPose(position, rotation), velocity, angular, health, engaged);
            return true;
        }
    }
}
