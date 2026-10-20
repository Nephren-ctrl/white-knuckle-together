using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Content;
using UnityEditor.Build.Pipeline;
using UnityEditor.Build.Pipeline.Interfaces;
using UnityEngine;

/// <summary>
/// Builds every asset under <see cref="CONTENT_ROOT"/> into the mod's asset bundle and copies it next to the mod's source,
/// where the mod's build picks it up. Run it from the "WKT" menu, or headless with tools/build-bundles.ps1.
/// <para>
/// Uses the Scriptable Build Pipeline, as the game's own Addressables bundles do: the game's runtime refuses bundles from
/// the legacy <c>BuildPipeline.BuildAssetBundles</c> of this editor version.
/// </para>
/// </summary>
public static class WKTBundleBuilder {
    public const string CONTENT_ROOT = "Assets/WKT";
    public const string BUNDLE_NAME = "wkt";
    public const string BUNDLE_FILE = "wkt.bundle";
    private const string c_build_dir = "Build/Bundles";

    /// <summary>The mod project's Bundles folder: three levels up from Assets is the repository root.</summary>
    private static string OutputDir => Path.GetFullPath(Path.Combine(Application.dataPath, "../../../WhiteKnuckleTogether/Bundles"));

    [MenuItem("WKT/Build Bundles")]
    public static void BuildAll() {
        var assets = FindContent();
        if (assets.Length == 0)
            throw new IOException($"There is nothing under {CONTENT_ROOT} to build.");
        var builds = new[] { new AssetBundleBuild { assetBundleName = BUNDLE_NAME, assetNames = assets } };
        Directory.CreateDirectory(c_build_dir);
        var parameters = new BundleBuildParameters(BuildTarget.StandaloneWindows64, BuildTargetGroup.Standalone, c_build_dir) {
            BundleCompression = BuildCompression.LZ4
        };
        var code = ContentPipeline.BuildAssetBundles(parameters, new BundleBuildContent(builds), out IBundleBuildResults _);
        if (code < ReturnCode.Success)
            throw new IOException($"The asset bundle build failed ({code}). See the editor log.");
        Directory.CreateDirectory(OutputDir);
        var output = Path.Combine(OutputDir, BUNDLE_FILE);
        File.Copy(Path.Combine(c_build_dir, BUNDLE_NAME), output, true);
        Debug.Log($"[WKT] Built {assets.Length} assets into {output}");
    }

    /// <summary>Every asset under <see cref="CONTENT_ROOT"/>, so adding content is just dropping it into that folder.</summary>
    private static string[] FindContent() {
        var assets = new List<string>();
        foreach (var guid in AssetDatabase.FindAssets(string.Empty, new[] { CONTENT_ROOT })) {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            // Scripts cannot be bundled, and shader includes are compiled into the shaders that use them.
            if (!AssetDatabase.IsValidFolder(path) && !path.EndsWith(".cs") && !path.EndsWith(".cginc") && !path.EndsWith(".hlsl"))
                assets.Add(path);
        }
        return assets.ToArray();
    }
}
