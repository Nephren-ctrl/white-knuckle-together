using System;
using System.Collections.Generic;
using UnityEngine;
using WhiteKnuckleTogether.Networking.LiteNetLib;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// A client dropped or threw an item into the world. The host spawns the real item from this, keyed from the client and
    /// <see cref="Token"/>, so the client's own dropped copy becomes its puppet. World-offset-corrected.
    /// </summary>
    public sealed class WKTDropRequest {
        /// <summary>The client's copy is held in place, such as a rebar stuck in a wall: the host's must not fall either.</summary>
        public const byte FLAG_KINEMATIC = 1 << 0;
        /// <summary>The item was used already, such as a lit flare.</summary>
        public const byte FLAG_USED = 1 << 1;
        private const byte c_known_flags = FLAG_KINEMATIC | FLAG_USED;

        public uint Token { get; set; }
        public string PrefabId { get; set; } = string.Empty;
        public Vector3 Position { get; set; }
        public Quaternion Rotation { get; set; } = Quaternion.identity;
        public Vector3 Velocity { get; set; }
        public byte Flags { get; set; }
        /// <summary>The item's <c>Item.data</c>.</summary>
        public List<string> ItemData { get; } = new();

        public void Write(NetDataWriter writer) {
            writer.Put(Token);
            writer.Put(PrefabId, WKTWire.MAX_ID_LENGTH);
            WKTWire.Put(writer, Position);
            WKTWire.Put(writer, Rotation);
            WKTWire.Put(writer, Velocity);
            writer.Put(Flags);
            var count = Math.Min(ItemData.Count, WKTWire.MAX_ITEM_DATA);
            writer.Put((byte)count);
            for (var i = 0; i < count; i++)
                writer.Put(ItemData[i], WKTWire.MAX_ID_LENGTH);
        }

        /// <summary>
        /// Reads into <paramref name="request"/>. Fails on truncation, an empty prefab, bad poses or speeds, unknown flags and too
        /// much data.
        /// </summary>
        public static bool TryRead(NetDataReader reader, WKTDropRequest request) {
            request.ItemData.Clear();
            if (!reader.TryGetUInt(out var token)) return false;
            if (!WKTReaderUtility.TryGetBoundedString(reader, WKTWire.MAX_ID_LENGTH, out var prefabId) || prefabId.Length == 0) return false;
            if (!WKTWire.TryGetPosition(reader, out var position) || !WKTWire.TryGetRotation(reader, out var rotation)) return false;
            if (!WKTWire.TryGetVelocity(reader, out var velocity)) return false;
            if (!reader.TryGetByte(out var flags) || (flags & ~c_known_flags) != 0) return false;
            if (!reader.TryGetByte(out var count) || count > WKTWire.MAX_ITEM_DATA) return false;
            for (var i = 0; i < count; i++) {
                if (!WKTReaderUtility.TryGetBoundedString(reader, WKTWire.MAX_ID_LENGTH, out var data)) return false;
                request.ItemData.Add(data);
            }
            request.Token = token;
            request.PrefabId = prefabId;
            request.Position = position;
            request.Rotation = rotation;
            request.Velocity = velocity;
            request.Flags = flags;
            return true;
        }
    }
}
