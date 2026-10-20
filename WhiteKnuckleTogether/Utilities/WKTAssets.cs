using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// The mod's own Unity assets, such as shaders, built into <see cref="BUNDLE_FILE"/> next to the mod's DLL by
    /// assets/WKTAssets (see its README). The bundle is loaded once, on first use. Callers fall back to built-in content when
    /// an asset is missing, so a missing bundle never breaks a feature outright.
    /// </summary>
    internal static class WKTAssets {
        public const string BUNDLE_FILE = "wkt.bundle";
        private static readonly Dictionary<string, Object> s_cache = new();
        private static AssetBundle? s_bundle;
        private static bool s_triedLoading;

        /// <summary>
        /// Loads the asset named <paramref name="name"/> (its file name without extension, or its full path in the asset
        /// project). False, with an error logged once per name, if the bundle or the asset is missing.
        /// </summary>
        public static bool TryLoad<T>(string name, out T asset) where T : Object {
            if (s_cache.TryGetValue(name, out var cached) && cached is T typed) {
                asset = typed;
                return true;
            }
            asset = null!;
            var bundle = GetBundle();
            if (bundle == null) return false;
            var loaded = bundle.LoadAsset<T>(name) ?? LoadByFileName<T>(bundle, name);
            if (loaded == null) {
                WKT.Instance.Logger.LogError($"The mod's asset bundle has no {typeof(T).Name} named '{name}'.");
                return false;
            }
            s_cache[name] = loaded;
            asset = loaded;
            return true;
        }

        /// <summary>Loads a shader from the bundle, and checks that this machine's graphics can run it.</summary>
        public static bool TryLoadShader(string name, out Shader shader) {
            if (!TryLoad(name, out shader)) return false;
            if (shader.isSupported) return true;
            WKT.Instance.Logger.LogError($"The mod's shader '{name}' is not supported by this graphics device.");
            return false;
        }

        /// <summary>Finds an asset by its file name without extension among the bundle's full asset paths.</summary>
        private static T? LoadByFileName<T>(AssetBundle bundle, string name) where T : Object {
            foreach (var path in bundle.GetAllAssetNames()) {
                if (string.Equals(Path.GetFileNameWithoutExtension(path), name, System.StringComparison.OrdinalIgnoreCase))
                    return bundle.LoadAsset<T>(path);
            }
            return null;
        }

        /// <summary>Unloads the bundle and everything loaded from it.</summary>
        public static void Unload() {
            s_cache.Clear();
            if (s_bundle != null)
                s_bundle.Unload(true);
            s_bundle = null;
            s_triedLoading = false;
        }

        private static AssetBundle? GetBundle() {
            if (s_bundle != null || s_triedLoading) return s_bundle;
            s_triedLoading = true;
            var directory = Path.GetDirectoryName(typeof(WKTAssets).Assembly.Location) ?? string.Empty;
            var path = Path.Combine(directory, BUNDLE_FILE);
            if (!File.Exists(path)) {
                WKT.Instance.Logger.LogError($"The mod's asset bundle is missing: {path}");
                return null;
            }
            s_bundle = AssetBundle.LoadFromFile(path);
            if (s_bundle == null)
                WKT.Instance.Logger.LogError($"The mod's asset bundle could not be loaded: {path}");
            else
                WKT.Instance.Logger.LogInfo($"Loaded the mod's asset bundle: {path}");
            return s_bundle;
        }
    }
}
