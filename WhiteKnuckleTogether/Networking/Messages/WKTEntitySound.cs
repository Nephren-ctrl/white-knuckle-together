using UnityEngine;
using WhiteKnuckleTogether.Networking.LiteNetLib;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// A sound a world entity played or stopped on the machine simulating it: which entity, which of its clip handlers (in
    /// <c>GetComponentsInChildren</c> order), which audio set (group and set indices into the handler's own lists, the same on
    /// every copy of the prefab), and the call's volume or position. Positions are world-offset-corrected.
    /// </summary>
    public readonly struct WKTEntitySound {
        /// <summary>Louder than any sound the game plays, but rejects garbage.</summary>
        public const float MAX_VOLUME = 16f;
        private const byte c_max_kind = (byte)WKTEntitySoundKind.Stop;

        public readonly ushort Handle;
        public readonly byte Handler;
        public readonly WKTEntitySoundKind Kind;
        public readonly byte Group;
        public readonly byte Set;
        /// <summary>Only meaningful for <see cref="WKTEntitySoundKind.PlayVolume"/>.</summary>
        public readonly float Volume;
        /// <summary>Only meaningful for <see cref="WKTEntitySoundKind.PlayAt"/>.</summary>
        public readonly Vector3 Position;

        public WKTEntitySound(ushort handle, byte handler, WKTEntitySoundKind kind, byte group, byte set, float volume = 0f, Vector3 position = default) {
            Handle = handle;
            Handler = handler;
            Kind = kind;
            Group = group;
            Set = set;
            Volume = volume;
            Position = position;
        }

        public void Write(NetDataWriter writer) {
            writer.Put(Handle);
            writer.Put(Handler);
            writer.Put((byte)Kind);
            writer.Put(Group);
            writer.Put(Set);
            switch (Kind) {
                case WKTEntitySoundKind.PlayVolume:
                    writer.Put(Mathf.Clamp(Volume, 0f, MAX_VOLUME));
                    break;
                case WKTEntitySoundKind.PlayAt:
                    WKTWire.Put(writer, Position);
                    break;
            }
        }

        /// <summary>Fails on truncation, handle 0, unknown kinds, volumes outside [0, <see cref="MAX_VOLUME"/>] and bad positions.</summary>
        public static bool TryRead(NetDataReader reader, out WKTEntitySound sound) {
            sound = default;
            if (!reader.TryGetUShort(out var handle) || handle == 0 || !reader.TryGetByte(out var handler) || !reader.TryGetByte(out var kind) ||
                kind > c_max_kind || !reader.TryGetByte(out var group) || !reader.TryGetByte(out var set)) return false;
            var volume = 0f;
            var position = Vector3.zero;
            switch ((WKTEntitySoundKind)kind) {
                case WKTEntitySoundKind.PlayVolume:
                    // NaN fails the comparisons, so it is rejected too.
                    if (!reader.TryGetFloat(out volume) || !(volume >= 0f && volume <= MAX_VOLUME)) return false;
                    break;
                case WKTEntitySoundKind.PlayAt:
                    if (!WKTWire.TryGetPosition(reader, out position)) return false;
                    break;
            }
            sound = new WKTEntitySound(handle, handler, (WKTEntitySoundKind)kind, group, set, volume, position);
            return true;
        }
    }
}
