using UnityEngine;
using WhiteKnuckleTogether.Networking.LiteNetLib;

namespace WhiteKnuckleTogether {
    /// <summary>Shared encodings for world messages. Reads validate everything, since peers are never trusted.</summary>
    public static class WKTWire {
        public const int VECTOR3_SIZE = sizeof(float) * 3;
        public const int QUATERNION_SIZE = sizeof(float) * 4;
        /// <summary>The most a damage amount may be. Generous, but rejects garbage.</summary>
        public const float MAX_AMOUNT = 1e6f;
        /// <summary>The longest prefab id, damage type or item data string accepted.</summary>
        public const int MAX_ID_LENGTH = 96;
        /// <summary>The most item data strings an item carries.</summary>
        public const int MAX_ITEM_DATA = 16;
        /// <summary>The longest hierarchy path (names joined by '/') sent to name an object below its level.</summary>
        public const int MAX_PATH_LENGTH = 256;
        /// <summary>Faster than anything in the game moves or spins, in units or radians per second. Rejects garbage.</summary>
        public const float MAX_SPEED = 500f;
        /// <summary>A rotation's squared length must be within this of 1.</summary>
        private const float c_unit_tolerance = 0.01f;

        public static void Put(NetDataWriter writer, Vector3 value) {
            writer.Put(value.x);
            writer.Put(value.y);
            writer.Put(value.z);
        }

        public static void Put(NetDataWriter writer, Quaternion value) {
            writer.Put(value.x);
            writer.Put(value.y);
            writer.Put(value.z);
            writer.Put(value.w);
        }

        /// <summary>A position or offset: every coordinate within <see cref="WKTPlayerPose.MAX_COORDINATE"/>.</summary>
        public static bool TryGetPosition(NetDataReader reader, out Vector3 value) {
            value = default;
            if (reader.AvailableBytes < VECTOR3_SIZE) return false;
            value = new Vector3(reader.GetFloat(), reader.GetFloat(), reader.GetFloat());
            return WKTPlayerPose.IsCoordinate(value.x) && WKTPlayerPose.IsCoordinate(value.y) && WKTPlayerPose.IsCoordinate(value.z);
        }

        /// <summary>A linear or angular velocity: finite and no faster than <see cref="MAX_SPEED"/>.</summary>
        public static bool TryGetVelocity(NetDataReader reader, out Vector3 value) {
            value = default;
            if (reader.AvailableBytes < VECTOR3_SIZE) return false;
            value = new Vector3(reader.GetFloat(), reader.GetFloat(), reader.GetFloat());
            var sqr = value.x * value.x + value.y * value.y + value.z * value.z;
            return sqr <= MAX_SPEED * MAX_SPEED;
        }

        /// <summary>A rotation: finite and of unit length, renormalized against rounding.</summary>
        public static bool TryGetRotation(NetDataReader reader, out Quaternion value) {
            value = Quaternion.identity;
            if (reader.AvailableBytes < QUATERNION_SIZE) return false;
            var x = reader.GetFloat();
            var y = reader.GetFloat();
            var z = reader.GetFloat();
            var w = reader.GetFloat();
            var sqr = x * x + y * y + z * z + w * w;
            if (float.IsNaN(sqr) || sqr < 1f - c_unit_tolerance || sqr > 1f + c_unit_tolerance) return false;
            var length = Mathf.Sqrt(sqr);
            value = new Quaternion(x / length, y / length, z / length, w / length);
            return true;
        }

        /// <summary>An amount such as damage: finite and within [0, <see cref="MAX_AMOUNT"/>].</summary>
        public static bool TryGetAmount(NetDataReader reader, out float value) =>
            reader.TryGetFloat(out value) && value >= 0f && value <= MAX_AMOUNT;
    }
}
