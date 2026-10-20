using System;
using WhiteKnuckleTogether.Networking.LiteNetLib;

namespace WhiteKnuckleTogether {
    /// <summary>Defensive read helpers for data coming from remote peers, which must never be trusted.</summary>
    public static class WKTReaderUtility {
        /// <summary>
        /// Reads a string written by <see cref="NetDataWriter.Put(string, int)"/>. Fails without throwing on truncated
        /// data, invalid UTF-8, or strings longer than <paramref name="maxLength"/> characters. The reader position is
        /// undefined after a failure.
        /// </summary>
        public static bool TryGetBoundedString(NetDataReader reader, int maxLength, out string result) {
            result = string.Empty;
            if (!reader.TryGetUShort(out var size)) return false;
            if (size == 0) return true;
            var byteCount = size - 1;
            if (reader.AvailableBytes < byteCount) return false;
            // A UTF-16 char takes at most 3 UTF-8 bytes (surrogate pairs take 4 bytes for 2 chars), so anything longer
            // cannot decode to maxLength chars. Rejects oversized strings before allocating.
            if (byteCount > maxLength * 3) return false;
            try {
                var str = NetDataWriter.uTF8Encoding.GetString(reader.RawData, reader.Position, byteCount);
                if (str.Length > maxLength) return false;
                reader.SkipBytes(byteCount);
                result = str;
                return true;
            }
            catch (ArgumentException) {
                // The shared encoding throws on invalid bytes. DecoderFallbackException derives from ArgumentException.
                return false;
            }
        }
    }
}
