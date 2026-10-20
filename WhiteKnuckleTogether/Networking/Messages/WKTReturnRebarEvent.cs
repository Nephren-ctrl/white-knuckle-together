using UnityEngine;
using WhiteKnuckleTogether.Networking.LiteNetLib;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// Something happened to a thrown return rebar. Sent client to host, which checks it and passes it on, and host to clients.
    /// <see cref="Player"/> is whoever it is about: who stuck it, who recalls it, who released it. The host fills it in from
    /// the sender for everything but its own recall grants. World-offset-corrected.
    /// </summary>
    public readonly struct WKTReturnRebarEvent {
        /// <summary>It hit a wall at <see cref="Position"/>, in the level <see cref="LevelKey"/>, and can be climbed.</summary>
        public const byte STUCK = 0;
        /// <summary>It flies back to <see cref="Player"/>, who gets the item when it arrives.</summary>
        public const byte RECALL = 1;
        /// <summary>Client to host only: the client wants to recall a rebar whose thrower is dead or gone.</summary>
        public const byte CLAIM = 2;
        /// <summary>Whoever it flew back to died or left: it flies on from <see cref="Position"/> at <see cref="Velocity"/>, with no owner.</summary>
        public const byte RELEASED = 3;
        /// <summary>It is gone: it reached whoever recalled it, or its own copy was destroyed some other way.</summary>
        public const byte REMOVED = 4;
        public const int SIZE = sizeof(uint) + sizeof(byte) * 2 + sizeof(ulong) + WKTWire.VECTOR3_SIZE * 2 + WKTWire.QUATERNION_SIZE;

        public readonly uint Id;
        public readonly byte Kind;
        public readonly byte Player;
        public readonly ulong LevelKey;
        public readonly Vector3 Position;
        public readonly Quaternion Rotation;
        public readonly Vector3 Velocity;

        public WKTReturnRebarEvent(uint id, byte kind, byte player, ulong levelKey, Vector3 position, Quaternion rotation, Vector3 velocity) {
            Id = id;
            Kind = kind;
            Player = player;
            LevelKey = levelKey;
            Position = position;
            Rotation = rotation;
            Velocity = velocity;
        }

        public static WKTReturnRebarEvent Of(uint id, byte kind, byte player) => new(id, kind, player, 0, Vector3.zero, Quaternion.identity, Vector3.zero);

        public WKTReturnRebarEvent WithPlayer(byte player) => new(Id, Kind, player, LevelKey, Position, Rotation, Velocity);

        public void Write(NetDataWriter writer) {
            writer.Put(Id);
            writer.Put(Kind);
            writer.Put(Player);
            writer.Put(LevelKey);
            WKTWire.Put(writer, Position);
            WKTWire.Put(writer, Rotation);
            WKTWire.Put(writer, Velocity);
        }

        /// <summary>Fails on truncation, id 0, unknown kinds, a stuck rebar without a level, bad poses and speeds over <see cref="WKTPlayerShot.MAX_SPEED"/>.</summary>
        public static bool TryRead(NetDataReader reader, out WKTReturnRebarEvent rebarEvent) {
            rebarEvent = default;
            if (!reader.TryGetUInt(out var id) || id == 0 || !reader.TryGetByte(out var kind) || kind > REMOVED || !reader.TryGetByte(out var player)) return false;
            if (!reader.TryGetULong(out var levelKey) || (kind == STUCK && levelKey == 0)) return false;
            if (!WKTWire.TryGetPosition(reader, out var position) || !WKTWire.TryGetRotation(reader, out var rotation)) return false;
            if (!WKTWire.TryGetPosition(reader, out var velocity) || velocity.sqrMagnitude > WKTPlayerShot.MAX_SPEED * WKTPlayerShot.MAX_SPEED) return false;
            rebarEvent = new WKTReturnRebarEvent(id, kind, player, levelKey, position, rotation, velocity);
            return true;
        }
    }
}
