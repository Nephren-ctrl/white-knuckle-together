using System;
using System.Collections.Generic;
using WhiteKnuckleTogether.Networking.LiteNetLib;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// One step of a player taking the item out of another's hand (see <see cref="WKTItemStealing"/>): the thief's request,
    /// relayed by the host to the victim, or the victim's hand-over of the item, relayed to the thief. Reused, so reading
    /// allocates nothing but strings.
    /// </summary>
    internal sealed class WKTItemSteal {
        public const byte KIND_REQUEST = 0;
        public const byte KIND_GIVE = 1;

        public byte Kind { get; set; }
        public byte ThiefId { get; set; }
        public byte VictimId { get; set; }
        public byte VictimHand { get; set; }
        public byte ThiefHand { get; set; }
        /// <summary>The item's prefab: on a request, the one the thief saw; on a hand-over, the one handed over.</summary>
        public string PrefabId { get; set; } = string.Empty;
        /// <summary>The handed-over item's data. Empty on a request.</summary>
        public List<string> ItemData { get; } = new();

        public void Write(NetDataWriter writer) {
            writer.Put(Kind);
            writer.Put(ThiefId);
            writer.Put(VictimId);
            writer.Put(VictimHand);
            writer.Put(ThiefHand);
            writer.Put(PrefabId, WKTWire.MAX_ID_LENGTH);
            var count = Kind == KIND_GIVE ? Math.Min(ItemData.Count, WKTWire.MAX_ITEM_DATA) : 0;
            writer.Put((byte)count);
            for (var i = 0; i < count; i++)
                writer.Put(ItemData[i], WKTWire.MAX_ID_LENGTH);
        }

        /// <summary>Reads into <paramref name="steal"/>. Fails on truncation, unknown kinds, bad hands, an empty prefab and too much data.</summary>
        public static bool TryRead(NetDataReader reader, WKTItemSteal steal) {
            steal.ItemData.Clear();
            if (!reader.TryGetByte(out var kind) || kind > KIND_GIVE) return false;
            if (!reader.TryGetByte(out var thief) || !reader.TryGetByte(out var victim) || thief == victim) return false;
            if (!reader.TryGetByte(out var victimHand) || victimHand > 1 || !reader.TryGetByte(out var thiefHand) || thiefHand > 1) return false;
            if (!WKTReaderUtility.TryGetBoundedString(reader, WKTWire.MAX_ID_LENGTH, out var prefabId) || prefabId.Length == 0) return false;
            if (!reader.TryGetByte(out var count) || count > WKTWire.MAX_ITEM_DATA || (kind == KIND_REQUEST && count > 0)) return false;
            for (var i = 0; i < count; i++) {
                if (!WKTReaderUtility.TryGetBoundedString(reader, WKTWire.MAX_ID_LENGTH, out var data)) return false;
                steal.ItemData.Add(data);
            }
            steal.Kind = kind;
            steal.ThiefId = thief;
            steal.VictimId = victim;
            steal.VictimHand = victimHand;
            steal.ThiefHand = thiefHand;
            steal.PrefabId = prefabId;
            return true;
        }
    }
}
