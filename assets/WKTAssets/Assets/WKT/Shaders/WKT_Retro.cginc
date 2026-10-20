// The look of White Knuckle's own world shader ("Dark Machine/SHDR_Base"), for the mod's shaders: vertex snapping, the
// world warp and the game's fog. It reads the same globals the game sets every frame (FXManager.FXRender and
// SettingsManager), so mod content snaps, warps and fades exactly like the level around it and follows the player's
// "vertex jitter" setting. The maths was read from the game's compiled shader; see GAME_INTERNALS.md, "Shaders".
// Outside a level the globals are unset (zero), which turns every effect here off.
#ifndef WKT_RETRO_INCLUDED
#define WKT_RETRO_INCLUDED

#include "UnityCG.cginc"

// Snap resolution (FXManager fxData.roundVerts, 160 by default). Declared as an int, as the game's shader does.
int _ROUND;
// 1 while the "vertex jitter" setting is on, 0 off.
float _USEJITTER;
// x: warp amplitude, z: how much it grows per meter beyond 6 m from the camera.
float4 _WORLDWARP;
float _FOGMULT;
float _FOGTONEDIST;
float _FOGTONEOFFSET;
float _NOFOG;
float4 _FOGCOLORTOP;
float4 _FOGCOLORBOTTOM;

// The game's world warp: a slow wobble of world positions that grows with distance from the camera.
float3 WKT_WorldWarp(float3 worldPos) {
    float3 phase = (worldPos.zyz + worldPos.xxy) * 0.8 + _Time.x * 37.81;
    float3 warp = float3(sin(phase.x), cos(phase.y), sin(phase.z)) * _WORLDWARP.x;
    float distance = length(_WorldSpaceCameraPos - worldPos);
    return worldPos + warp * max(distance - 6.0, 0.0) * _WORLDWARP.z;
}

// The game's vertex snapping, in clip space before the perspective divide. snapMultiplier matches the game's per-material
// "_ROUNDMULT" (1 snaps like the world; lower is coarser).
float4 WKT_Snap(float4 clipPos, float snapMultiplier) {
    float resolution = (float)_ROUND * snapMultiplier;
    if (resolution <= 0.0) return clipPos;
    float2 snapped = round(clipPos.xy * resolution) / resolution;
    clipPos.xy = lerp(clipPos.xy, snapped, _USEJITTER);
    return clipPos;
}

// World position to snapped clip position, with the world warp: the whole vertex stage of the game's look.
float4 WKT_WorldToClip(float3 worldPos, float snapMultiplier) {
    return WKT_Snap(mul(UNITY_MATRIX_VP, float4(WKT_WorldWarp(worldPos), 1.0)), snapMultiplier);
}

// How much fog covers a point, 0 to 1: it thickens with the square of distance.
float WKT_FogAmount(float3 worldPos) {
    float fog = saturate(length(worldPos - _WorldSpaceCameraPos) * _FOGMULT);
    return fog * fog * (1.0 - _NOFOG);
}

// The fog's color toward a point: the game blends a bottom and a top color by how steeply the view looks up or down.
float3 WKT_FogColor(float3 worldPos) {
    float3 view = normalize(worldPos - _WorldSpaceCameraPos);
    float tone = saturate((view.y * _FOGTONEDIST + _FOGTONEOFFSET) * 0.5 + 0.5);
    tone = tone * tone * (3.0 - 2.0 * tone);
    return lerp(_FOGCOLORBOTTOM.rgb, _FOGCOLORTOP.rgb, tone);
}

// Fogs a color like the game's world.
float3 WKT_ApplyFog(float3 color, float3 worldPos) {
    return lerp(color, WKT_FogColor(worldPos), WKT_FogAmount(worldPos));
}

#endif
