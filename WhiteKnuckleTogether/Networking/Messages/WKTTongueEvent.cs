using UnityEngine;
using WhiteKnuckleTogether.Networking.LiteNetLib;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// Host to clients: one barnacle tongue changed state. The host decides what a tongue does and where it reaches; a client
    /// only animates it, and grabs its own player when the tongue hits it there. World-offset-corrected.
    /// </summary>
    public readonly struct WKTTongueEvent {
        public const byte NOBODY = byte.MaxValue;
        /// <summary>The game's tongue states: retracting, reaching, searching, grabbing.</summary>
        public const byte MAX_STATE = 3;
        public const int SIZE = sizeof(ushort) + sizeof(byte) + sizeof(byte) + WKTWire.VECTOR3_SIZE + sizeof(byte);

        public readonly ushort Handle;
        public readonly byte Tongue;
        public readonly byte State;
        public readonly Vector3 Target;
        /// <summary>The player the tongue holds, or <see cref="NOBODY"/>.</summary>
        public readonly byte Grabbed;

        public WKTTongueEvent(ushort handle, byte tongue, byte state, Vector3 target, byte grabbed) {
            Handle = handle;
            Tongue = tongue;
            State = state;
            Target = target;
            Grabbed = grabbed;
        }

        public void Write(NetDataWriter writer) {
            writer.Put(Handle);
            writer.Put(Tongue);
            writer.Put(State);
            WKTWire.Put(writer, Target);
            writer.Put(Grabbed);
        }

        /// <summary>Fails on truncation, handle 0, unknown states and bad positions.</summary>
        public static bool TryRead(NetDataReader reader, out WKTTongueEvent tongueEvent) {
            tongueEvent = default;
            if (!reader.TryGetUShort(out var handle) || handle == 0 || !reader.TryGetByte(out var tongue)) return false;
            if (!reader.TryGetByte(out var state) || state > MAX_STATE) return false;
            if (!WKTWire.TryGetPosition(reader, out var target) || !reader.TryGetByte(out var grabbed)) return false;
            tongueEvent = new WKTTongueEvent(handle, tongue, state, target, grabbed);
            return true;
        }
    }
}
