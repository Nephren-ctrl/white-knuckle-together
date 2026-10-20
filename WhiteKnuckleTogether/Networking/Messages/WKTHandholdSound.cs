using UnityEngine;
using WhiteKnuckleTogether.Networking.LiteNetLib;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// A sound a handhold played for its holder: which handhold (its level content key), which of its clips (see
    /// <see cref="WKTFieldTable"/>), how loud and high, and where relative to the handhold.
    /// </summary>
    public readonly struct WKTHandholdSound {
        /// <summary>Louder or higher than any sound the game plays, but rejects garbage.</summary>
        public const float MAX_LEVEL = 16f;
        /// <summary>Further from its handhold than any hand can be, but rejects garbage.</summary>
        public const float MAX_OFFSET = 32f;

        public readonly ulong Key;
        public readonly ushort Clip;
        public readonly float Volume;
        public readonly float Pitch;
        /// <summary>Where it played, from the handhold's position.</summary>
        public readonly Vector3 Offset;

        public WKTHandholdSound(ulong key, ushort clip, float volume, float pitch, Vector3 offset) {
            Key = key;
            Clip = clip;
            Volume = volume;
            Pitch = pitch;
            Offset = offset;
        }

        public void Write(NetDataWriter writer) {
            writer.Put(Key);
            writer.Put(Clip);
            writer.Put(Volume);
            writer.Put(Pitch);
            WKTWire.Put(writer, Vector3.ClampMagnitude(Offset, MAX_OFFSET));
        }

        /// <summary>Fails on truncation, key 0, levels outside [0, <see cref="MAX_LEVEL"/>] and offsets beyond <see cref="MAX_OFFSET"/>.</summary>
        public static bool TryRead(NetDataReader reader, out WKTHandholdSound sound) {
            sound = default;
            if (!reader.TryGetULong(out var key) || key == 0 || !reader.TryGetUShort(out var clip) || !reader.TryGetFloat(out var volume) ||
                !reader.TryGetFloat(out var pitch) || reader.AvailableBytes < WKTWire.VECTOR3_SIZE) return false;
            // NaN fails the comparisons, so it is rejected too.
            if (!(volume >= 0f && volume <= MAX_LEVEL) || !(pitch >= 0f && pitch <= MAX_LEVEL)) return false;
            var offset = new Vector3(reader.GetFloat(), reader.GetFloat(), reader.GetFloat());
            // Clamping on write can round a hair past the limit.
            if (!(offset.sqrMagnitude <= MAX_OFFSET * MAX_OFFSET * 1.001f)) return false;
            sound = new WKTHandholdSound(key, clip, volume, pitch, offset);
            return true;
        }
    }
}
