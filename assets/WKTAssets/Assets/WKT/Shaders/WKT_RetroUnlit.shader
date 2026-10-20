// A flat color with the game's look: vertex snapping, the world warp and the game's fog (WKT_Retro.cginc), but unlit, so
// it reads in the game's dark levels. For other players' bodies, heads and hands. Opaque by default; the blend and depth
// write are properties, so the same shader draws see-through ghosts (alpha from the color's alpha).
Shader "WKT/Retro Unlit" {
    Properties {
        _Color ("Color", Color) = (1, 1, 1, 1)
        _SnapMultiplier ("Snap Multiplier", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Source Blend", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Destination Blend", Float) = 0
        [Toggle] _ZWrite ("Depth Write", Float) = 1
    }
    SubShader {
        Tags { "Queue" = "Geometry" "RenderType" = "Opaque" }
        Blend [_SrcBlend] [_DstBlend]
        ZWrite [_ZWrite]

        Pass {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "WKT_Retro.cginc"

            fixed4 _Color;
            float _SnapMultiplier;

            struct appdata {
                float4 vertex : POSITION;
            };

            struct v2f {
                float4 pos : SV_POSITION;
                float3 worldPos : TEXCOORD0;
            };

            v2f vert(appdata v) {
                v2f o;
                float3 worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.pos = WKT_WorldToClip(worldPos, _SnapMultiplier);
                o.worldPos = worldPos;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target {
                return fixed4(WKT_ApplyFog(_Color.rgb, i.worldPos), _Color.a);
            }
            ENDCG
        }
    }
    Fallback Off
}
