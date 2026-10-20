using UnityEngine;
using WhiteKnuckleTogether.Networking.LiteNetLib;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// The host's death goo: its height (world-offset-corrected), natural speed and state. Sent host to clients as the
    /// <see cref="WKTMessageId.GooState"/> payload, after the send sequence.
    /// </summary>
    public readonly struct WKTGooPose : IWKTInterpolatable<WKTGooPose> {
        /// <summary>The game's own <c>active</c> flag: the goo moves and kills.</summary>
        public const byte FLAG_ACTIVE = 1 << 0;
        /// <summary>The goo has started rising (the game's <c>hasStarted</c>).</summary>
        public const byte FLAG_STARTED = 1 << 1;
        private const byte c_known_flags = FLAG_ACTIVE | FLAG_STARTED;
        public const int SIZE = sizeof(ushort) + sizeof(float) * 2 + sizeof(byte);

        public readonly float Height;
        public readonly float Speed;
        public readonly byte Flags;

        public WKTGooPose(float height, float speed, byte flags) {
            Height = height;
            Speed = speed;
            Flags = flags;
        }

        public Vector3 Position => new(0f, Height, 0f);
        public bool IsActive => (Flags & FLAG_ACTIVE) != 0;
        public bool HasStarted => (Flags & FLAG_STARTED) != 0;

        /// <summary>Blends the height. Speed and flags are the newer state's.</summary>
        public WKTGooPose LerpTo(in WKTGooPose to, float t) => new(Height + (to.Height - Height) * t, to.Speed, to.Flags);

        public WKTGooPose WithPosition(Vector3 position) => new(position.y, Speed, Flags);

        public void Write(NetDataWriter writer, ushort sequence) {
            writer.Put(sequence);
            writer.Put(Height);
            writer.Put(Speed);
            writer.Put(Flags);
        }

        /// <summary>Fails on truncation, non-finite or out-of-range heights and speeds, and unknown flags.</summary>
        public static bool TryRead(NetDataReader reader, out ushort sequence, out WKTGooPose pose) {
            pose = default;
            if (!reader.TryGetUShort(out sequence) || !reader.TryGetFloat(out var height) || !reader.TryGetFloat(out var speed) ||
                !reader.TryGetByte(out var flags)) return false;
            // NaN fails both comparisons, so it is rejected too.
            if (!WKTPlayerPose.IsCoordinate(height) || !(speed >= 0f && speed <= WKTPlayerPose.MAX_COORDINATE) || (flags & ~c_known_flags) != 0)
                return false;
            pose = new WKTGooPose(height, speed, flags);
            return true;
        }
    }
}
