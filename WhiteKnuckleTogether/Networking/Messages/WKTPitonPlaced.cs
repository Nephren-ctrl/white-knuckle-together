using UnityEngine;
using WhiteKnuckleTogether.Networking.LiteNetLib;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// A player placed a piton. Sent by the placer to the host, which passes it on and replays it to late joiners. Everyone
    /// builds the piton from the item's own piton prefab, in the same level. World-offset-corrected.
    /// </summary>
    public readonly struct WKTPitonPlaced {
        public readonly uint Id;
        /// <summary>The piton item placed, whose hand item knows the piton prefab.</summary>
        public readonly string ItemPrefab;
        public readonly ulong LevelKey;
        public readonly Vector3 Position;
        public readonly Quaternion Rotation;
        /// <summary>How much it has been hammered in since it was placed, so a late joiner sees it as deep.</summary>
        public readonly float Hammered;

        public WKTPitonPlaced(uint id, string itemPrefab, ulong levelKey, Vector3 position, Quaternion rotation, float hammered) {
            Id = id;
            ItemPrefab = itemPrefab;
            LevelKey = levelKey;
            Position = position;
            Rotation = rotation;
            Hammered = hammered;
        }

        public WKTPitonPlaced WithHammered(float amount) => new(Id, ItemPrefab, LevelKey, Position, Rotation, Hammered + amount);

        public void Write(NetDataWriter writer) {
            writer.Put(Id);
            writer.Put(ItemPrefab, WKTWire.MAX_ID_LENGTH);
            writer.Put(LevelKey);
            WKTWire.Put(writer, Position);
            WKTWire.Put(writer, Rotation);
            writer.Put(Hammered);
        }

        /// <summary>Fails on truncation, id 0, an empty prefab, level key 0, bad poses and a negative or non-finite hammered amount.</summary>
        public static bool TryRead(NetDataReader reader, out WKTPitonPlaced piton) {
            piton = default;
            if (!reader.TryGetUInt(out var id) || id == 0) return false;
            if (!WKTReaderUtility.TryGetBoundedString(reader, WKTWire.MAX_ID_LENGTH, out var itemPrefab) || itemPrefab.Length == 0) return false;
            if (!reader.TryGetULong(out var levelKey) || levelKey == 0) return false;
            if (!WKTWire.TryGetPosition(reader, out var position) || !WKTWire.TryGetRotation(reader, out var rotation)) return false;
            if (!reader.TryGetFloat(out var hammered) || !(hammered >= 0f && hammered <= WKTWire.MAX_AMOUNT)) return false;
            piton = new WKTPitonPlaced(id, itemPrefab, levelKey, position, rotation, hammered);
            return true;
        }
    }
}
