using System;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// Allocation-free vertical flipping of tightly packed pixel buffers. Steam delivers images top row first while
    /// Unity textures expect the bottom row first.
    /// </summary>
    internal static class ImageRowFlipper {
        public static int GetByteCount(int width, int height, int bytesPerPixel) {
            if (width <= 0)
                throw new ArgumentOutOfRangeException(nameof(width), width, "Width must be positive.");
            if (height <= 0)
                throw new ArgumentOutOfRangeException(nameof(height), height, "Height must be positive.");
            if (bytesPerPixel <= 0)
                throw new ArgumentOutOfRangeException(nameof(bytesPerPixel), bytesPerPixel, "Bytes per pixel must be positive.");
            return checked(width * height * bytesPerPixel);
        }

        /// <summary>
        /// Copies <paramref name="source"/> into <paramref name="destination"/> with the row order reversed. The
        /// buffers must not overlap.
        /// </summary>
        public static void FlipRows(ReadOnlySpan<byte> source, Span<byte> destination, int width, int height, int bytesPerPixel) {
            var byteCount = GetByteCount(width, height, bytesPerPixel);
            if (source.Length < byteCount)
                throw new ArgumentException($"Source holds {source.Length} bytes but {byteCount} are required.", nameof(source));
            if (destination.Length < byteCount)
                throw new ArgumentException($"Destination holds {destination.Length} bytes but {byteCount} are required.", nameof(destination));
            if (source.Slice(0, byteCount).Overlaps(destination.Slice(0, byteCount)))
                throw new ArgumentException("Source and destination must not overlap.", nameof(destination));
            var stride = width * bytesPerPixel;
            for (var row = 0; row < height; row++)
                source.Slice(row * stride, stride).CopyTo(destination.Slice((height - 1 - row) * stride, stride));
        }
    }
}
