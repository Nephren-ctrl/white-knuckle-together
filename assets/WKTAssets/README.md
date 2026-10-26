# WKT Assets

The mod's own Unity content (shaders, materials, meshes, textures, prefabs, sounds). It is built into one asset bundle,
`WhiteKnuckleTogether/Bundles/wkt.bundle`, which ships next to the mod DLL and is loaded at runtime by `WKTAssets`.

The project must be opened with **Unity 6000.3.9f1**, the game's version, on the built-in render pipeline. A bundle
built with any other version may fail to load in the game. Bundles are built with the Scriptable Build Pipeline package,
like the game's own Addressables: the game refuses bundles from the editor's legacy `BuildPipeline.BuildAssetBundles`
("not compatible with this newer version of the Unity runtime"), which leave out the bundle's AssetBundle object.

## Adding content

1. Put the asset anywhere under `Assets/WKT/`. Everything in that folder goes into the bundle and nothing outside it
   does, so there is no need to set bundle labels by hand. Editor scripts belong in `Assets/Editor/`.
2. Build the bundle, either from the editor's **WKT > Build Bundles** menu or headless with
   `tools/build-bundles.ps1` (close the project in the editor first). Both copy the result to
   `WhiteKnuckleTogether/Bundles/wkt.bundle`.
3. Load it in the mod by its file name without extension, for example
   `WKTAssets.TryLoadShader("WKT_RaceOrb", out var shader)` or `WKTAssets.TryLoad<GameObject>("MyPrefab", out var prefab)`.
   Keep names unique across folders. Always handle a false result, so the feature falls back gracefully when the bundle
   is missing.
4. Rebuild the mod. Its build copies the bundle next to the DLL, and the `export-mod` skill packages it.

Shaders use the built-in pipeline (`CGPROGRAM`, `UnityCG.cginc`). Scripts (`MonoBehaviour`s) cannot be put in a bundle:
write them in the mod and add them at runtime.
