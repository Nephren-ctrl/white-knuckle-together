using System;
using UnityEngine;
using WhiteKnuckleTogether.Networking.LiteNetLib;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// One rope's state on the wire: its key, its end's velocity and its links (see <see cref="WKTRopeLink"/>). Batches of
    /// them follow a send sequence and a count in <see cref="WKTMessageId.RopeStates"/>; one follows a byte in
    /// <see cref="WKTMessageId.RopeHold"/> and <see cref="WKTMessageId.RopeAuthority"/>.
    /// </summary>
    public static class WKTRopeState {
        /// <summary><see cref="WKTMessageId.RopeHold"/>'s byte: the sender took hold of the rope, rather than letting go.</summary>
        public const byte HOLD_GRABBED = 1;
        public const int MAX_LINKS = 63;
        public const int ENTRY_HEADER_SIZE = sizeof(ulong) + WKTWire.VECTOR3_SIZE + sizeof(byte);

        public static int GetEntrySize(int linkCount) => ENTRY_HEADER_SIZE + linkCount * WKTRopeLink.SIZE;

        public static void WriteEntry(NetDataWriter writer, ulong key, Vector3 velocity, ReadOnlySpan<WKTRopeLink> links) {
            writer.Put(key);
            WKTWire.Put(writer, Vector3.ClampMagnitude(velocity, WKTWire.MAX_SPEED));
            writer.Put((byte)links.Length);
            foreach (var link in links)
                link.Write(writer);
        }

        /// <summary>
        /// Reads one rope's state into <paramref name="links"/>. Fails on truncation, key 0, no links, more links than
        /// <see cref="MAX_LINKS"/> or <paramref name="links"/> holds, and a bad velocity.
        /// </summary>
        public static bool TryReadEntry(NetDataReader reader, out ulong key, out Vector3 velocity, Span<WKTRopeLink> links, out int linkCount) {
            velocity = Vector3.zero;
            linkCount = 0;
            if (!reader.TryGetULong(out key) || key == 0 || !WKTWire.TryGetVelocity(reader, out velocity) || !reader.TryGetByte(out var count)) return false;
            if (count == 0 || count > MAX_LINKS || count > links.Length) return false;
            for (var i = 0; i < count; i++) {
                if (!WKTRopeLink.TryRead(reader, out links[i])) return false;
            }
            linkCount = count;
            return true;
        }
    }
}
