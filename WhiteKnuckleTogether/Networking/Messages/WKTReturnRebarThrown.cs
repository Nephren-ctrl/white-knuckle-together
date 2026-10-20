using System;
using System.Collections.Generic;
using UnityEngine;
using WhiteKnuckleTogether.Networking.LiteNetLib;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// A player threw their return rebar, or (replayed by the host to a player who joins late) one is stuck in a wall. Sent by
    /// the thrower to the host, which passes it on. Everyone else shows a copy built from the item's own projectile prefab.
    /// World-offset-corrected.
    /// </summary>
    public sealed class WKTReturnRebarThrown {
        /// <summary>It is stuck in a wall at <see cref="Position"/>, in the level <see cref="LevelKey"/>.</summary>
        public const byte FLAG_STUCK = 1 << 0;
        /// <summary>Its thrower died while it flew back to them, so it has nobody to return to (see <see cref="WKTReturnRebarEvent.RELEASED"/>).</summary>
        public const byte FLAG_OWNERLESS = 1 << 1;
        private const byte c_known_flags = FLAG_STUCK | FLAG_OWNERLESS;

        public uint Id { get; set; }
        /// <summary>Who threw it. Only they can recall it while they are alive.</summary>
        public byte Owner { get; set; }
        /// <summary>The item thrown, whose hand item knows the projectile prefab.</summary>
        public string ItemPrefab { get; set; } = string.Empty;
        /// <summary>The projectile prefab's name, among the item's projectiles.</summary>
        public string Projectile { get; set; } = string.Empty;
        public Vector3 Position { get; set; }
        public Quaternion Rotation { get; set; } = Quaternion.identity;
        public Vector3 Velocity { get; set; }
        public byte Flags { get; set; }
        /// <summary>The level it is stuck in. Zero unless <see cref="FLAG_STUCK"/> is set.</summary>
        public ulong LevelKey { get; set; }
        /// <summary>The thrown item's <c>Item.data</c>, such as its strain, so whoever recalls it gets the same item.</summary>
        public List<string> ItemData { get; } = new();

        public bool IsStuck => (Flags & FLAG_STUCK) != 0;
        public bool IsOwnerless => (Flags & FLAG_OWNERLESS) != 0;

        public void CopyFrom(WKTReturnRebarThrown other) {
            Id = other.Id;
            Owner = other.Owner;
            ItemPrefab = other.ItemPrefab;
            Projectile = other.Projectile;
            Position = other.Position;
            Rotation = other.Rotation;
            Velocity = other.Velocity;
            Flags = other.Flags;
            LevelKey = other.LevelKey;
            ItemData.Clear();
            ItemData.AddRange(other.ItemData);
        }

        public void Write(NetDataWriter writer) {
            writer.Put(Id);
            writer.Put(Owner);
            writer.Put(ItemPrefab, WKTWire.MAX_ID_LENGTH);
            writer.Put(Projectile, WKTWire.MAX_ID_LENGTH);
            WKTWire.Put(writer, Position);
            WKTWire.Put(writer, Rotation);
            WKTWire.Put(writer, Velocity);
            writer.Put(Flags);
            writer.Put(LevelKey);
            var count = Math.Min(ItemData.Count, WKTWire.MAX_ITEM_DATA);
            writer.Put((byte)count);
            for (var i = 0; i < count; i++)
                writer.Put(ItemData[i], WKTWire.MAX_ID_LENGTH);
        }

        /// <summary>
        /// Reads into <paramref name="thrown"/>. Fails on truncation, id 0, empty names, bad poses, speeds over
        /// <see cref="WKTPlayerShot.MAX_SPEED"/>, unknown flags, a stuck rebar without a level and too much data.
        /// </summary>
        public static bool TryRead(NetDataReader reader, WKTReturnRebarThrown thrown) {
            thrown.ItemData.Clear();
            if (!reader.TryGetUInt(out var id) || id == 0 || !reader.TryGetByte(out var owner)) return false;
            if (!WKTReaderUtility.TryGetBoundedString(reader, WKTWire.MAX_ID_LENGTH, out var itemPrefab) || itemPrefab.Length == 0) return false;
            if (!WKTReaderUtility.TryGetBoundedString(reader, WKTWire.MAX_ID_LENGTH, out var projectile) || projectile.Length == 0) return false;
            if (!WKTWire.TryGetPosition(reader, out var position) || !WKTWire.TryGetRotation(reader, out var rotation)) return false;
            if (!WKTWire.TryGetPosition(reader, out var velocity) || velocity.sqrMagnitude > WKTPlayerShot.MAX_SPEED * WKTPlayerShot.MAX_SPEED) return false;
            if (!reader.TryGetByte(out var flags) || (flags & ~c_known_flags) != 0) return false;
            if (!reader.TryGetULong(out var levelKey) || ((flags & FLAG_STUCK) != 0) != (levelKey != 0)) return false;
            if (!reader.TryGetByte(out var count) || count > WKTWire.MAX_ITEM_DATA) return false;
            for (var i = 0; i < count; i++) {
                if (!WKTReaderUtility.TryGetBoundedString(reader, WKTWire.MAX_ID_LENGTH, out var data)) return false;
                thrown.ItemData.Add(data);
            }
            thrown.Id = id;
            thrown.Owner = owner;
            thrown.ItemPrefab = itemPrefab;
            thrown.Projectile = projectile;
            thrown.Position = position;
            thrown.Rotation = rotation;
            thrown.Velocity = velocity;
            thrown.Flags = flags;
            thrown.LevelKey = levelKey;
            return true;
        }
    }
}
