using System;
using System.Collections.Generic;
using UnityEngine;
using WhiteKnuckleTogether.Networking.LiteNetLib;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// Something that happened to one world object. Shared by several messages: a despawn carries only the handle, an
    /// activation change the handle and flag, and a kill or damage request the damage as well. Positions are
    /// world-offset-corrected.
    /// </summary>
    public readonly struct WKTEntityEvent {
        /// <summary>The most damage tags a hit carries besides its type (the game's hits carry two or three).</summary>
        public const int MAX_TAGS = 8;
        public ushort Handle { get; }
        /// <summary>Activation changes only: whether the object is now active.</summary>
        public bool Active { get; }
        /// <summary>Kills and damage requests only.</summary>
        public float Amount { get; }
        public string DamageType { get; }
        public Vector3 Position { get; }
        public Vector3 Force { get; }
        public Vector3 Direction { get; }
        /// <summary>
        /// Kills and damage requests only: the hit's tags other than its type, such as "player" or "explosive", which damage
        /// effects, resistances and weaknesses check. Never null.
        /// </summary>
        public string[] Tags { get; }

        private WKTEntityEvent(ushort handle, bool active, float amount, string damageType, Vector3 position, Vector3 force, Vector3 direction,
            string[] tags) {
            Handle = handle;
            Active = active;
            Amount = amount;
            DamageType = damageType;
            Position = position;
            Force = force;
            Direction = direction;
            Tags = tags;
        }

        public static WKTEntityEvent Despawn(ushort handle) => new(handle, false, 0f, string.Empty, default, default, default, Array.Empty<string>());

        public static WKTEntityEvent Activation(ushort handle, bool active) =>
            new(handle, active, 0f, string.Empty, default, default, default, Array.Empty<string>());

        public static WKTEntityEvent Damage(ushort handle, float amount, string? damageType, Vector3 position, Vector3 force, Vector3 direction,
            string[]? tags = null) =>
            new(handle, false, amount, damageType ?? string.Empty, position, force, direction, tags ?? Array.Empty<string>());

        /// <summary>
        /// A hit's tags to send: those other than its type (which <c>CreateDamageInfo</c> adds back), without empty ones,
        /// overlong ones or repeats, at most <see cref="MAX_TAGS"/>.
        /// </summary>
        public static string[] TagsToSend(List<string>? tags, string? damageType) {
            if (tags == null || tags.Count == 0) return Array.Empty<string>();
            var count = 0;
            var kept = new string[Math.Min(tags.Count, MAX_TAGS)];
            foreach (var tag in tags) {
                if (count == kept.Length) break;
                if (string.IsNullOrEmpty(tag) || tag == damageType || tag.Length > WKTWire.MAX_ID_LENGTH || Array.IndexOf(kept, tag, 0, count) >= 0) continue;
                kept[count++] = tag;
            }
            if (count == 0) return Array.Empty<string>();
            if (count < kept.Length)
                Array.Resize(ref kept, count);
            return kept;
        }

        /// <summary>Adds the tags to a hit rebuilt from this event with <c>CreateDamageInfo</c>, which holds only the type.</summary>
        public void AddTagsTo(List<string> tags) {
            foreach (var tag in Tags) {
                if (!tags.Contains(tag))
                    tags.Add(tag);
            }
        }

        public void WriteHandle(NetDataWriter writer) => writer.Put(Handle);

        public void WriteActivation(NetDataWriter writer) {
            writer.Put(Handle);
            writer.Put(Active);
        }

        public void WriteDamage(NetDataWriter writer) {
            writer.Put(Handle);
            writer.Put(Amount);
            writer.Put(DamageType, WKTWire.MAX_ID_LENGTH);
            WKTWire.Put(writer, Position);
            WKTWire.Put(writer, Force);
            WKTWire.Put(writer, Direction);
            writer.Put((byte)Tags.Length);
            foreach (var tag in Tags)
                writer.Put(tag, WKTWire.MAX_ID_LENGTH);
        }

        public static bool TryReadHandle(NetDataReader reader, out WKTEntityEvent value) {
            value = default;
            if (!reader.TryGetUShort(out var handle) || handle == 0) return false;
            value = Despawn(handle);
            return true;
        }

        public static bool TryReadActivation(NetDataReader reader, out WKTEntityEvent value) {
            value = default;
            if (!reader.TryGetUShort(out var handle) || handle == 0 || !reader.TryGetByte(out var active) || active > 1) return false;
            value = Activation(handle, active == 1);
            return true;
        }

        /// <summary>
        /// Fails on truncation, handle 0, an amount outside [0, <see cref="WKTWire.MAX_AMOUNT"/>], overlong types or tags, empty
        /// tags, more than <see cref="MAX_TAGS"/> tags and bad vectors.
        /// </summary>
        public static bool TryReadDamage(NetDataReader reader, out WKTEntityEvent value) {
            value = default;
            if (!reader.TryGetUShort(out var handle) || handle == 0) return false;
            if (!WKTWire.TryGetAmount(reader, out var amount)) return false;
            if (!WKTReaderUtility.TryGetBoundedString(reader, WKTWire.MAX_ID_LENGTH, out var damageType)) return false;
            if (!WKTWire.TryGetPosition(reader, out var position)) return false;
            if (!WKTWire.TryGetPosition(reader, out var force) || !WKTWire.TryGetPosition(reader, out var direction)) return false;
            if (!reader.TryGetByte(out var tagCount) || tagCount > MAX_TAGS) return false;
            var tags = tagCount == 0 ? Array.Empty<string>() : new string[tagCount];
            for (var i = 0; i < tagCount; i++) {
                if (!WKTReaderUtility.TryGetBoundedString(reader, WKTWire.MAX_ID_LENGTH, out var tag) || string.IsNullOrEmpty(tag)) return false;
                tags[i] = tag;
            }
            value = Damage(handle, amount, damageType, position, force, direction, tags);
            return true;
        }
    }
}
