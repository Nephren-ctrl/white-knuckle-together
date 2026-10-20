using System;
using System.Collections.Generic;
using WhiteKnuckleTogether.Networking.LiteNetLib;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// Everything a player carries: hand items, bag and pouches. Mirrored to every machine, so whoever hosts when the player
    /// leaves can drop it all where they were (see <see cref="WKTPlayerInventories"/>). Entries are reused across clears.
    /// </summary>
    public sealed class WKTInventorySnapshot {
        /// <summary>The most items a snapshot carries.</summary>
        public const int MAX_ITEMS = 64;
        /// <summary>The item was used already, such as a lit flare.</summary>
        public const byte FLAG_USED = 1 << 0;
        private const byte c_known_flags = FLAG_USED;
        private readonly List<Entry> m_entries = new();

        public int Count { get; private set; }

        public Entry this[int index] => m_entries[index];

        /// <summary>A cleared entry at the end, or null once <see cref="MAX_ITEMS"/> are in.</summary>
        public Entry? Add() {
            if (Count == MAX_ITEMS) return null;
            if (Count == m_entries.Count)
                m_entries.Add(new Entry());
            var entry = m_entries[Count++];
            entry.PrefabId = string.Empty;
            entry.Used = false;
            entry.Data.Clear();
            return entry;
        }

        public void Clear() => Count = 0;

        public void CopyFrom(WKTInventorySnapshot other) {
            Clear();
            for (var i = 0; i < other.Count; i++) {
                var source = other[i];
                var entry = Add()!;
                entry.PrefabId = source.PrefabId;
                entry.Used = source.Used;
                entry.Data.AddRange(source.Data);
            }
        }

        public bool Matches(WKTInventorySnapshot other) {
            if (Count != other.Count) return false;
            for (var i = 0; i < Count; i++) {
                var a = m_entries[i];
                var b = other[i];
                if (a.PrefabId != b.PrefabId || a.Used != b.Used || a.Data.Count != b.Data.Count) return false;
                for (var d = 0; d < a.Data.Count; d++) {
                    if (a.Data[d] != b.Data[d]) return false;
                }
            }
            return true;
        }

        public void Write(NetDataWriter writer) {
            writer.Put((byte)Count);
            for (var i = 0; i < Count; i++) {
                var entry = m_entries[i];
                writer.Put(entry.PrefabId, WKTWire.MAX_ID_LENGTH);
                writer.Put(entry.Used ? FLAG_USED : (byte)0);
                var count = Math.Min(entry.Data.Count, WKTWire.MAX_ITEM_DATA);
                writer.Put((byte)count);
                for (var d = 0; d < count; d++)
                    writer.Put(entry.Data[d], WKTWire.MAX_ID_LENGTH);
            }
        }

        /// <summary>
        /// Reads into <paramref name="snapshot"/>, which is left empty on failure. Fails on truncation, more than
        /// <see cref="MAX_ITEMS"/> items, an empty prefab, unknown flags and too much data.
        /// </summary>
        public static bool TryRead(NetDataReader reader, WKTInventorySnapshot snapshot) {
            snapshot.Clear();
            if (!reader.TryGetByte(out var count) || count > MAX_ITEMS) return false;
            for (var i = 0; i < count; i++) {
                var entry = snapshot.Add()!;
                if (!WKTReaderUtility.TryGetBoundedString(reader, WKTWire.MAX_ID_LENGTH, out var prefabId) || prefabId.Length == 0 ||
                    !reader.TryGetByte(out var flags) || (flags & ~c_known_flags) != 0 ||
                    !reader.TryGetByte(out var dataCount) || dataCount > WKTWire.MAX_ITEM_DATA) {
                    snapshot.Clear();
                    return false;
                }
                entry.PrefabId = prefabId;
                entry.Used = (flags & FLAG_USED) != 0;
                for (var d = 0; d < dataCount; d++) {
                    if (!WKTReaderUtility.TryGetBoundedString(reader, WKTWire.MAX_ID_LENGTH, out var data)) {
                        snapshot.Clear();
                        return false;
                    }
                    entry.Data.Add(data);
                }
            }
            return true;
        }

        public sealed class Entry {
            public string PrefabId { get; set; } = string.Empty;
            public bool Used { get; set; }
            /// <summary>The item's <c>Item.data</c>.</summary>
            public List<string> Data { get; } = new();
        }
    }
}
