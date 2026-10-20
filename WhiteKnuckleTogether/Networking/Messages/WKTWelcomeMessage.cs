using System.Collections.Generic;
using WhiteKnuckleTogether.Networking.LiteNetLib;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// Sent by the host to a newly admitted client: the client's assigned id followed by every player in the session,
    /// including the client itself and the host.
    /// </summary>
    public static class WKTWelcomeMessage {
        public static void WriteHeader(NetDataWriter writer, byte localPlayerId, byte playerCount) {
            writer.Put(localPlayerId);
            writer.Put(playerCount);
        }

        /// <summary>
        /// Reads and validates the payload after the message id into <paramref name="entries"/> (cleared first).
        /// Rejects truncated data, counts above the lobby capacity, duplicate ids, a missing local player, and anything
        /// other than exactly one host. On failure the entries list contents are undefined.
        /// </summary>
        public static bool TryRead(NetDataReader reader, List<WKTPlayerEntry> entries, out byte localPlayerId) {
            entries.Clear();
            localPlayerId = 0;
            if (!reader.TryGetByte(out localPlayerId)) return false;
            if (!reader.TryGetByte(out var count)) return false;
            if (count == 0 || count > WKTLobbyConsts.MAX_PLAYERS) return false;
            var hostCount = 0;
            var foundLocal = false;
            for (var i = 0; i < count; i++) {
                if (!WKTPlayerEntry.TryRead(reader, out var entry)) return false;
                for (var j = 0; j < entries.Count; j++) {
                    if (entries[j].Id == entry.Id)
                        return false;
                }
                if (entry.IsHost)
                    hostCount++;
                if (entry.Id == localPlayerId) {
                    // The local player is never the host on a client.
                    if (entry.IsHost) return false;
                    foundLocal = true;
                }
                entries.Add(entry);
            }
            return foundLocal && hostCount == 1;
        }
    }
}
