using UnityEngine;
using WhiteKnuckleTogether.Networking.LiteNetLib;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// A player fired a hand item, such as a rebar throw or a flare shot. The shooter's own shot is the real one; everyone else
    /// shows a copy that never hits anything. Sent client to host, which shows it and passes it on, and host to clients.
    /// World-offset-corrected.
    /// </summary>
    public readonly struct WKTPlayerShot {
        /// <summary>The fastest shot accepted, in metres per second. Rejects garbage without limiting any real hand item.</summary>
        public const float MAX_SPEED = 2000f;

        public readonly byte Shooter;
        /// <summary>The item fired, which knows its projectile prefabs.</summary>
        public readonly string ItemPrefab;
        /// <summary>The projectile prefab's name, among the item's projectiles.</summary>
        public readonly string Projectile;
        public readonly Vector3 Position;
        public readonly Quaternion Rotation;
        public readonly Vector3 Velocity;

        public WKTPlayerShot(byte shooter, string itemPrefab, string projectile, Vector3 position, Quaternion rotation, Vector3 velocity) {
            Shooter = shooter;
            ItemPrefab = itemPrefab;
            Projectile = projectile;
            Position = position;
            Rotation = rotation;
            Velocity = velocity;
        }

        public void Write(NetDataWriter writer) {
            writer.Put(Shooter);
            writer.Put(ItemPrefab, WKTWire.MAX_ID_LENGTH);
            writer.Put(Projectile, WKTWire.MAX_ID_LENGTH);
            WKTWire.Put(writer, Position);
            WKTWire.Put(writer, Rotation);
            WKTWire.Put(writer, Velocity);
        }

        /// <summary>Fails on truncation, empty names, bad poses and speeds over <see cref="MAX_SPEED"/>.</summary>
        public static bool TryRead(NetDataReader reader, out WKTPlayerShot shot) {
            shot = default;
            if (!reader.TryGetByte(out var shooter)) return false;
            if (!WKTReaderUtility.TryGetBoundedString(reader, WKTWire.MAX_ID_LENGTH, out var itemPrefab) || itemPrefab.Length == 0) return false;
            if (!WKTReaderUtility.TryGetBoundedString(reader, WKTWire.MAX_ID_LENGTH, out var projectile) || projectile.Length == 0) return false;
            if (!WKTWire.TryGetPosition(reader, out var position) || !WKTWire.TryGetRotation(reader, out var rotation)) return false;
            if (!WKTWire.TryGetPosition(reader, out var velocity) || velocity.sqrMagnitude > MAX_SPEED * MAX_SPEED) return false;
            shot = new WKTPlayerShot(shooter, itemPrefab, projectile, position, rotation, velocity);
            return true;
        }
    }
}
