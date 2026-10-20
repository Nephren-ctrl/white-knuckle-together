using System;
using System.Collections.Generic;
using UnityEngine;
using WhiteKnuckleTogether.Networking.LiteNetLib;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// The host tells clients about one of its world objects: the handle every later message about it uses, the key a
    /// client's own copy has if it built the same one, and what is needed to spawn it if it did not. Sent reliably.
    /// Positions are world-offset-corrected.
    /// </summary>
    public sealed class WKTEntityBind {
        public const byte FLAG_ACTIVE = 1 << 0;
        private const byte c_known_flags = FLAG_ACTIVE;
        private static readonly bool[] s_knownKinds = BuildKnownKinds();

        public ushort Handle { get; set; }
        public ulong Key { get; set; }
        public WKTNetKind Kind { get; set; }
        public byte Flags { get; set; }
        /// <summary>The asset id to spawn it from: <c>GameEntity.entityPrefabID</c> or <c>Item.prefabName</c>. May be empty.</summary>
        public string PrefabId { get; set; } = string.Empty;
        /// <summary>The level it belongs to, from <c>WKTContentSeeding</c>. Zero when it belongs to none.</summary>
        public ulong LevelKey { get; set; }
        public Vector3 Position { get; set; }
        public Quaternion Rotation { get; set; } = Quaternion.identity;
        /// <summary>An item's <c>Item.data</c>. Empty for everything else.</summary>
        public List<string> ItemData { get; } = new();

        public bool IsActive => (Flags & FLAG_ACTIVE) != 0;

        public void CopyFrom(WKTEntityBind other) {
            if (ReferenceEquals(other, this)) return;
            Handle = other.Handle;
            Key = other.Key;
            Kind = other.Kind;
            Flags = other.Flags;
            PrefabId = other.PrefabId;
            LevelKey = other.LevelKey;
            Position = other.Position;
            Rotation = other.Rotation;
            ItemData.Clear();
            ItemData.AddRange(other.ItemData);
        }

        public void Write(NetDataWriter writer) {
            writer.Put(Handle);
            writer.Put(Key);
            writer.Put((byte)Kind);
            writer.Put(Flags);
            writer.Put(PrefabId, WKTWire.MAX_ID_LENGTH);
            writer.Put(LevelKey);
            WKTWire.Put(writer, Position);
            WKTWire.Put(writer, Rotation);
            var count = Math.Min(ItemData.Count, WKTWire.MAX_ITEM_DATA);
            writer.Put((byte)count);
            for (var i = 0; i < count; i++)
                writer.Put(ItemData[i], WKTWire.MAX_ID_LENGTH);
        }

        /// <summary>
        /// Reads into <paramref name="bind"/>. Fails on truncation, handle 0, unknown kinds or flags, overlong strings, too much
        /// item data, out-of-range positions and non-unit rotations.
        /// </summary>
        public static bool TryRead(NetDataReader reader, WKTEntityBind bind) {
            bind.ItemData.Clear();
            if (!reader.TryGetUShort(out var handle) || handle == 0) return false;
            if (!reader.TryGetULong(out var key)) return false;
            if (!reader.TryGetByte(out var kind) || !s_knownKinds[kind]) return false;
            if (!reader.TryGetByte(out var flags) || (flags & ~c_known_flags) != 0) return false;
            if (!WKTReaderUtility.TryGetBoundedString(reader, WKTWire.MAX_ID_LENGTH, out var prefabId)) return false;
            if (!reader.TryGetULong(out var levelKey)) return false;
            if (!WKTWire.TryGetPosition(reader, out var position) || !WKTWire.TryGetRotation(reader, out var rotation)) return false;
            if (!reader.TryGetByte(out var count) || count > WKTWire.MAX_ITEM_DATA) return false;
            for (var i = 0; i < count; i++) {
                if (!WKTReaderUtility.TryGetBoundedString(reader, WKTWire.MAX_ID_LENGTH, out var data)) return false;
                bind.ItemData.Add(data);
            }
            bind.Handle = handle;
            bind.Key = key;
            bind.Kind = (WKTNetKind)kind;
            bind.Flags = flags;
            bind.PrefabId = prefabId;
            bind.LevelKey = levelKey;
            bind.Position = position;
            bind.Rotation = rotation;
            return true;
        }

        private static bool[] BuildKnownKinds() {
            var known = new bool[byte.MaxValue + 1];
            foreach (WKTNetKind kind in Enum.GetValues(typeof(WKTNetKind)))
                known[(byte)kind] = true;
            return known;
        }
    }
}
