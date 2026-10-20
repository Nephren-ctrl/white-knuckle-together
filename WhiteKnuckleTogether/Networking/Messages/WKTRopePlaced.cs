using UnityEngine;
using WhiteKnuckleTogether.Networking.LiteNetLib;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// A player's thrown rebar left a rope hanging in a wall. Sent by the thrower to the host, which passes it on and replays
    /// it to late joiners. Everyone builds it from the projectile's own hit effect, in the same level. World-offset-corrected.
    /// </summary>
    public readonly struct WKTRopePlaced {
        /// <summary>The projectile's default hit effect, rather than one of its custom ones.</summary>
        public const sbyte DEFAULT_EFFECT = -1;

        /// <summary>Named by the thrower, as pitons are (see <see cref="WKTPitonRegistry.MakeId"/>).</summary>
        public readonly uint Id;
        /// <summary>The item thrown, whose hand item knows the projectile prefab.</summary>
        public readonly string ItemPrefab;
        /// <summary>The projectile prefab's name, among the item's projectiles.</summary>
        public readonly string Projectile;
        /// <summary>Which of the projectile's hit effects it left: <see cref="DEFAULT_EFFECT"/> or a custom one's index.</summary>
        public readonly sbyte Effect;
        public readonly ulong LevelKey;
        public readonly Vector3 Position;
        public readonly Quaternion Rotation;

        public WKTRopePlaced(uint id, string itemPrefab, string projectile, sbyte effect, ulong levelKey, Vector3 position, Quaternion rotation) {
            Id = id;
            ItemPrefab = itemPrefab;
            Projectile = projectile;
            Effect = effect;
            LevelKey = levelKey;
            Position = position;
            Rotation = rotation;
        }

        public void Write(NetDataWriter writer) {
            writer.Put(Id);
            writer.Put(ItemPrefab, WKTWire.MAX_ID_LENGTH);
            writer.Put(Projectile, WKTWire.MAX_ID_LENGTH);
            writer.Put(Effect);
            writer.Put(LevelKey);
            WKTWire.Put(writer, Position);
            WKTWire.Put(writer, Rotation);
        }

        /// <summary>Fails on truncation, id 0, empty names, an effect below <see cref="DEFAULT_EFFECT"/>, level key 0 and bad poses.</summary>
        public static bool TryRead(NetDataReader reader, out WKTRopePlaced placed) {
            placed = default;
            if (!reader.TryGetUInt(out var id) || id == 0) return false;
            if (!WKTReaderUtility.TryGetBoundedString(reader, WKTWire.MAX_ID_LENGTH, out var itemPrefab) || itemPrefab.Length == 0) return false;
            if (!WKTReaderUtility.TryGetBoundedString(reader, WKTWire.MAX_ID_LENGTH, out var projectile) || projectile.Length == 0) return false;
            if (!reader.TryGetSByte(out var effect) || effect < DEFAULT_EFFECT) return false;
            if (!reader.TryGetULong(out var levelKey) || levelKey == 0) return false;
            if (!WKTWire.TryGetPosition(reader, out var position) || !WKTWire.TryGetRotation(reader, out var rotation)) return false;
            placed = new WKTRopePlaced(id, itemPrefab, projectile, effect, levelKey, position, rotation);
            return true;
        }
    }
}
