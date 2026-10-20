using WhiteKnuckleTogether.Networking.LiteNetLib;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// One rider's bucking timer for a flier, handed between the machines that simulate it: from a client that stops
    /// simulating it to the host, and from the host to a client that starts. Reliable.
    /// </summary>
    public readonly struct WKTRiderBuck {
        public const int SIZE = sizeof(ushort) + sizeof(float) + sizeof(byte);
        /// <summary>Longer than any flier waits between bucks. Rejects garbage.</summary>
        public const float MAX_TIME = 1e4f;

        public ushort Handle { get; }
        /// <summary>Seconds until the next buck, as the game's <c>buckTime</c>.</summary>
        public float Time { get; }
        /// <summary>Bucks since the rider got on, as the game's <c>currentBuckDropHits</c>.</summary>
        public byte Hits { get; }

        public WKTRiderBuck(ushort handle, float time, byte hits) {
            Handle = handle;
            Time = time;
            Hits = hits;
        }

        public void Write(NetDataWriter writer) {
            writer.Put(Handle);
            writer.Put(Time);
            writer.Put(Hits);
        }

        /// <summary>Fails on truncation, handle 0, and times that are not finite or beyond <see cref="MAX_TIME"/> either way.</summary>
        public static bool TryRead(NetDataReader reader, out WKTRiderBuck value) {
            value = default;
            if (!reader.TryGetUShort(out var handle) || handle == 0 || !reader.TryGetFloat(out var time) || !(time >= -MAX_TIME && time <= MAX_TIME)) return false;
            if (!reader.TryGetByte(out var hits)) return false;
            value = new WKTRiderBuck(handle, time, hits);
            return true;
        }
    }
}
