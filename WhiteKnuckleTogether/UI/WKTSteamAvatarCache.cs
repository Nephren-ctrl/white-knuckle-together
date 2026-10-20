using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Steamworks;
using UnityEngine;
using Object = UnityEngine.Object;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// Downloads Steam avatars once per user and keeps them as GPU-only textures. Must be used from the main thread.
    /// </summary>
    internal static class WKTSteamAvatarCache {
        private const int c_bytes_per_pixel = 4;
        private static readonly Dictionary<ulong, Texture2D> s_textures = new();
        private static readonly Dictionary<ulong, Task<Texture2D?>> s_pending = new();
        private static byte[] s_scratch = Array.Empty<byte>();
        private static int s_generation;

        public static Task<Texture2D?> GetAsync(Friend friend) {
            var key = friend.Id.Value;
            if (s_textures.TryGetValue(key, out var texture) && texture != null)
                return Task.FromResult<Texture2D?>(texture);
            if (s_pending.TryGetValue(key, out var pending))
                return pending;
            pending = LoadAsync(friend, key, s_generation);
            // Loads that already finished synchronously have nothing left to dedupe.
            if (!pending.IsCompleted)
                s_pending[key] = pending;
            return pending;
        }

        /// <summary>
        /// Destroys every cached texture. Loads still in flight finish without being cached.
        /// </summary>
        public static void Clear() {
            foreach (var (_, texture) in s_textures) {
                if (texture != null)
                    Object.Destroy(texture);
            }
            s_textures.Clear();
            s_pending.Clear();
            s_scratch = Array.Empty<byte>();
            s_generation++;
        }

        private static async Task<Texture2D?> LoadAsync(Friend friend, ulong key, int generation) {
            try {
                var image = await friend.GetMediumAvatarAsync();
                if (generation != s_generation) return null;
                if (image == null) {
                    WKT.Instance.Logger.LogWarning($"Steam returned no avatar for {key}.");
                    return null;
                }
                var texture = CreateTexture(image.Value);
                if (texture != null)
                    s_textures[key] = texture;
                return texture;
            }
            catch (Exception e) {
                WKT.Instance.Logger.LogError($"Failed to load Steam avatar for {key}: {e}");
                return null;
            }
            finally {
                if (generation == s_generation)
                    s_pending.Remove(key);
            }
        }

        private static Texture2D? CreateTexture(Steamworks.Data.Image image) {
            var width = (int)image.Width;
            var height = (int)image.Height;
            var byteCount = ImageRowFlipper.GetByteCount(width, height, c_bytes_per_pixel);
            if (image.Data == null || image.Data.Length < byteCount) {
                WKT.Instance.Logger.LogError($"Steam avatar data is truncated ({image.Data?.Length ?? 0}/{byteCount} bytes).");
                return null;
            }
            // LoadRawTextureData requires the array to match the texture size exactly.
            if (s_scratch.Length != byteCount)
                s_scratch = new byte[byteCount];
            ImageRowFlipper.FlipRows(image.Data, s_scratch, width, height, c_bytes_per_pixel);
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false) {
                name = "WKT Steam Avatar",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            texture.LoadRawTextureData(s_scratch);
            texture.Apply(false, true);
            return texture;
        }
    }
}
