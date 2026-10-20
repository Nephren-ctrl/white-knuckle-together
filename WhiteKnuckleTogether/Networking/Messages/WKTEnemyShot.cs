using UnityEngine;
using WhiteKnuckleTogether.Networking.LiteNetLib;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// An enemy projectile's id between machines: the id of the player whose machine fired it, then a 24-bit count of that
    /// machine's shots. Pure, so it can be tested without Unity.
    /// </summary>
    public static class WKTShotId {
        public const uint MAX_SEQUENCE = 0xFFFFFF;

        public static uint Of(byte origin, uint sequence) => ((uint)origin << 24) | (sequence & MAX_SEQUENCE);

        public static byte OriginOf(uint shotId) => (byte)(shotId >> 24);

        /// <summary>The next sequence after <paramref name="sequence"/>, skipping zero so no id is the empty one.</summary>
        public static uint Next(uint sequence) => (sequence & MAX_SEQUENCE) == MAX_SEQUENCE ? 1 : sequence + 1;
    }

    /// <summary>
    /// An enemy fired a projectile on the machine simulating it (<see cref="WKTMessageId.ProjectileFired"/>). Other machines
    /// show a copy that only hits their own player. <see cref="Held"/> shots wait in place until released
    /// (<c>projectileShootHold</c>, such as a grub aunt's spikes). Positions are world-offset-corrected.
    /// </summary>
    public readonly struct WKTShotFired {
        public readonly uint ShotId;
        public readonly ushort Shooter;
        public readonly string Prefab;
        public readonly Vector3 Position;
        public readonly Quaternion Rotation;
        public readonly Vector3 Direction;
        /// <summary>The shot's damage after the attack's override and multiplier.</summary>
        public readonly float Damage;
        public readonly bool Held;

        public WKTShotFired(uint shotId, ushort shooter, string prefab, Vector3 position, Quaternion rotation, Vector3 direction, float damage, bool held) {
            ShotId = shotId;
            Shooter = shooter;
            Prefab = prefab;
            Position = position;
            Rotation = rotation;
            Direction = direction;
            Damage = damage;
            Held = held;
        }

        public void Write(NetDataWriter writer) {
            writer.Put(ShotId);
            writer.Put(Shooter);
            writer.Put(Prefab, WKTWire.MAX_ID_LENGTH);
            WKTWire.Put(writer, Position);
            WKTWire.Put(writer, Rotation);
            WKTWire.Put(writer, Direction);
            writer.Put(Damage);
            writer.Put(Held);
        }

        /// <summary>Fails on truncation, shot id 0, handle 0, an empty or overlong prefab, a direction longer than one and bad damage.</summary>
        public static bool TryRead(NetDataReader reader, out WKTShotFired shot) {
            shot = default;
            if (!reader.TryGetUInt(out var shotId) || shotId == 0 || !reader.TryGetUShort(out var shooter) || shooter == 0) return false;
            if (!WKTReaderUtility.TryGetBoundedString(reader, WKTWire.MAX_ID_LENGTH, out var prefab) || string.IsNullOrEmpty(prefab)) return false;
            if (!WKTWire.TryGetPosition(reader, out var position) || !WKTWire.TryGetRotation(reader, out var rotation)) return false;
            if (!WKTWire.TryGetPosition(reader, out var direction) || direction.sqrMagnitude > 1.01f) return false;
            if (!WKTWire.TryGetAmount(reader, out var damage) || !reader.TryGetBool(out var held)) return false;
            shot = new WKTShotFired(shotId, shooter, prefab, position, rotation, direction, damage, held);
            return true;
        }
    }

    /// <summary>
    /// A held shot was let go (<see cref="WKTMessageId.ProjectileReleased"/>), or a shot that sticks came to rest
    /// (<see cref="WKTMessageId.ProjectileStuck"/>, which also carries what a machine without the shot needs to build it).
    /// </summary>
    public readonly struct WKTShotPose {
        public readonly uint ShotId;
        public readonly ushort Shooter;
        public readonly string Prefab;
        public readonly Vector3 Position;
        /// <summary>Released: the direction it flies off in, at most one long. Stuck: unused.</summary>
        public readonly Vector3 Direction;
        /// <summary>Stuck: its rotation where it rests. Released: unused.</summary>
        public readonly Quaternion Rotation;

        private WKTShotPose(uint shotId, ushort shooter, string prefab, Vector3 position, Vector3 direction, Quaternion rotation) {
            ShotId = shotId;
            Shooter = shooter;
            Prefab = prefab;
            Position = position;
            Direction = direction;
            Rotation = rotation;
        }

        public static WKTShotPose Released(uint shotId, Vector3 position, Vector3 direction) =>
            new(shotId, 0, string.Empty, position, Vector3.ClampMagnitude(direction, 1f), Quaternion.identity);

        public static WKTShotPose Stuck(uint shotId, ushort shooter, string prefab, Vector3 position, Quaternion rotation) =>
            new(shotId, shooter, prefab, position, Vector3.zero, rotation);

        public void WriteReleased(NetDataWriter writer) {
            writer.Put(ShotId);
            WKTWire.Put(writer, Position);
            WKTWire.Put(writer, Direction);
        }

        public void WriteStuck(NetDataWriter writer) {
            writer.Put(ShotId);
            writer.Put(Shooter);
            writer.Put(Prefab, WKTWire.MAX_ID_LENGTH);
            WKTWire.Put(writer, Position);
            WKTWire.Put(writer, Rotation);
        }

        public static bool TryReadReleased(NetDataReader reader, out WKTShotPose pose) {
            pose = default;
            if (!reader.TryGetUInt(out var shotId) || shotId == 0 || !WKTWire.TryGetPosition(reader, out var position) ||
                !WKTWire.TryGetPosition(reader, out var direction) || direction.sqrMagnitude > 1.01f) return false;
            pose = Released(shotId, position, direction);
            return true;
        }

        /// <summary>Fails on truncation, shot id 0, an empty or overlong prefab and bad vectors. The shooter may be 0 (gone).</summary>
        public static bool TryReadStuck(NetDataReader reader, out WKTShotPose pose) {
            pose = default;
            if (!reader.TryGetUInt(out var shotId) || shotId == 0 || !reader.TryGetUShort(out var shooter)) return false;
            if (!WKTReaderUtility.TryGetBoundedString(reader, WKTWire.MAX_ID_LENGTH, out var prefab) || string.IsNullOrEmpty(prefab)) return false;
            if (!WKTWire.TryGetPosition(reader, out var position) || !WKTWire.TryGetRotation(reader, out var rotation)) return false;
            pose = Stuck(shotId, shooter, prefab, position, rotation);
            return true;
        }
    }
}
