namespace WhiteKnuckleTogether {
    /// <summary>
    /// Allocation-free parsing of '/' separated hierarchy paths. Empty segments (leading, trailing or repeated
    /// separators) are skipped.
    /// </summary>
    internal static class UIPathParser {
        public const char SEPARATOR = '/';

        /// <summary>
        /// Reads the next non-empty segment starting at <paramref name="index"/> and advances it past the segment.
        /// </summary>
        public static bool TryReadSegment(string path, ref int index, out int start, out int length) {
            var pathLength = path.Length;
            while (index < pathLength && path[index] == SEPARATOR)
                index++;
            start = index;
            while (index < pathLength && path[index] != SEPARATOR)
                index++;
            length = index - start;
            return length > 0;
        }

        public static int CountSegments(string path) {
            var index = 0;
            var count = 0;
            while (TryReadSegment(path, ref index, out _, out _))
                count++;
            return count;
        }

        /// <summary>
        /// Gets the bounds of the last non-empty segment, which is the name of the object the path points to.
        /// </summary>
        public static bool TryGetLastSegment(string path, out int start, out int length) {
            var end = path.Length;
            while (end > 0 && path[end - 1] == SEPARATOR)
                end--;
            start = end;
            while (start > 0 && path[start - 1] != SEPARATOR)
                start--;
            length = end - start;
            return length > 0;
        }

        public static bool SegmentEquals(string path, int start, int length, string name) =>
            name.Length == length && string.CompareOrdinal(path, start, name, 0, length) == 0;
    }
}
