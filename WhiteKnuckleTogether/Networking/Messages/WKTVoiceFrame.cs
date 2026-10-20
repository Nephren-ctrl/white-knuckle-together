using UnityEngine;
using WhiteKnuckleTogether.Networking.LiteNetLib;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// The <see cref="WKTMessageId.VoiceFrame"/> payload: one 20 ms Opus packet of a player's voice and its sequence number.
    /// A client sends its own frames without a player id; the host adds the speaker's id when relaying them. A dead player
    /// talking to the living (<see cref="ModifierId.DeadCanTalk"/>) adds a flag and where their spectator camera is.
    /// </summary>
    internal static class WKTVoiceFrame {
        public const byte FLAG_TO_LIVING = 1;

        public static void Write(NetDataWriter writer, ushort sequence, byte[] packet, int length) {
            writer.Put(sequence);
            writer.Put((ushort)length);
            writer.Put(packet, 0, length);
        }

        /// <summary>
        /// Reads a frame into <paramref name="packet"/>, which must hold <see cref="WKTVoiceConsts.MAX_PACKET_BYTES"/>. Rejects
        /// empty, oversized and truncated packets.
        /// </summary>
        public static bool TryRead(NetDataReader reader, out ushort sequence, byte[] packet, out int length) {
            length = 0;
            if (!reader.TryGetUShort(out sequence) || !reader.TryGetUShort(out var size)) return false;
            if (size == 0 || size > WKTVoiceConsts.MAX_PACKET_BYTES || reader.AvailableBytes < size) return false;
            reader.GetBytes(packet, size);
            length = size;
            return true;
        }

        /// <summary>After the frame: it is a dead player's, meant for the living too, spoken from <paramref name="wirePosition"/>.</summary>
        public static void WriteToLiving(NetDataWriter writer, Vector3 wirePosition) {
            writer.Put(FLAG_TO_LIVING);
            WKTWire.Put(writer, wirePosition);
        }

        /// <summary>Whether the frame just read is meant for the living too, and from where. False for a plain frame.</summary>
        public static bool TryReadToLiving(NetDataReader reader, out Vector3 wirePosition) {
            wirePosition = default;
            if (!reader.TryGetByte(out var flags) || (flags & FLAG_TO_LIVING) == 0) return false;
            return WKTWire.TryGetPosition(reader, out wirePosition);
        }
    }
}
