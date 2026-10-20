// The race orb: a huge, mostly see-through sphere around the race leader, like a field of power. Two cloud textures
// drift across it in different directions and are multiplied together into its opacity, so it churns; its surface
// wiggles a little; the further a pixel of it is from the camera, the more opaque it is, so its distance can be judged at
// a glance; and a rim outlines it. It glows a little and is only partly fogged, so it stays visible in the game's dark,
// foggy levels. It snaps and warps like the game's world (WKT_Retro.cginc). Drawn from inside and outside.
Shader "WKT/Race Orb" {
    Properties {
        _Color ("Color", Color) = (0.55, 0.85, 1, 1)
        _NearDistance ("Fully Clear Within (m)", Float) = 4
        _FarDistance ("Most Opaque From (m)", Float) = 90
        _MaxAlpha ("Most Opacity", Range(0, 1)) = 0.6
        _RimPower ("Rim Sharpness", Range(0.5, 16)) = 3
        _RimAlpha ("Rim Opacity", Range(0, 1)) = 0.6
        _Glow ("Glow", Range(0, 3)) = 1.4
        _FogStrength ("Fog Strength", Range(0, 1)) = 0.4
        _CloudA ("Clouds A", 2D) = "white" {}
        _CloudB ("Clouds B", 2D) = "white" {}
        _Radius ("Radius (m)", Float) = 50
        _TexelsPerMeter ("Texels Per Meter", Float) = 16
        _CloudScale ("Cloud Scale (A, B)", Vector) = (1, 0.75, 0, 0)
        _CloudVelocityA ("Cloud A Drift (m/s)", Vector) = (0.8, 0.45, 0, 0)
        _CloudVelocityB ("Cloud B Drift (m/s)", Vector) = (-0.6, 0.9, 0, 0)
        _CloudStrength ("Cloud Strength", Range(0, 1)) = 0.6
        _CloudGlow ("Cloud Glow", Range(0, 2)) = 0.8
        _WiggleAmount ("Wiggle (fraction of radius)", Range(0, 0.05)) = 0.012
        _WiggleFrequency ("Wiggle Frequency", Float) = 9
        _WiggleSpeed ("Wiggle Speed", Float) = 1.3
        _SnapMultiplier ("Snap Multiplier", Float) = 1
    }
    SubShader {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Cull Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "WKT_Retro.cginc"

            fixed4 _Color;
            float _NearDistance;
            float _FarDistance;
            float _MaxAlpha;
            float _RimPower;
            float _RimAlpha;
            float _Glow;
            float _FogStrength;
            sampler2D _CloudA;
            sampler2D _CloudB;
            float4 _CloudA_TexelSize;
            float4 _CloudB_TexelSize;
            float _Radius;
            float _TexelsPerMeter;
            float4 _CloudScale;
            float4 _CloudVelocityA;
            float4 _CloudVelocityB;
            float _CloudStrength;
            float _CloudGlow;
            float _WiggleAmount;
            float _WiggleFrequency;
            float _WiggleSpeed;
            float _SnapMultiplier;

            struct appdata {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
            };

            struct v2f {
                float4 pos : SV_POSITION;
                float3 worldPos : TEXCOORD0;
                float3 worldNormal : TEXCOORD1;
                // Where on the sphere, as a direction from its center: the clouds stick to the orb as it moves.
                float3 sphereDir : TEXCOORD2;
            };

            v2f vert(appdata v) {
                v2f o;
                // Unity's sphere is 1 across, so 0.5 is its radius in object space.
                float3 dir = normalize(v.vertex.xyz);
                float wave = sin(dot(dir, float3(1.0, 1.3, 0.7)) * _WiggleFrequency + _Time.y * _WiggleSpeed)
                    * sin(dot(dir, float3(-0.8, 0.5, 1.2)) * _WiggleFrequency * 0.7 - _Time.y * _WiggleSpeed * 0.8);
                float3 local = v.vertex.xyz + dir * (wave * _WiggleAmount * 0.5);
                float3 worldPos = mul(unity_ObjectToWorld, float4(local, 1.0)).xyz;
                o.pos = WKT_WorldToClip(worldPos, _SnapMultiplier);
                o.worldPos = worldPos;
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                o.sphereDir = dir;
                return o;
            }

            // A texture wrapped around the sphere from three sides, blended by which way the surface faces. Sized in meters on
            // the orb's surface at the game's texel density (16 per meter), so its pixels match the world's; scale > 1
            // magnifies it. Drift is in meters per second.
            float SampleTriplanar(sampler2D tex, float4 texelSize, float3 dir, float scale, float2 drift) {
                float3 weights = pow(abs(dir), 4.0);
                weights /= weights.x + weights.y + weights.z;
                // Meters to uv: texels per meter over the texture's width in texels.
                float perMeter = _TexelsPerMeter * texelSize.x / max(scale, 0.0001);
                float3 p = dir * _Radius * perMeter;
                float2 offset = drift * perMeter;
                float x = tex2D(tex, p.zy + offset).r;
                float y = tex2D(tex, p.xz + offset).r;
                float z = tex2D(tex, p.xy + offset).r;
                return x * weights.x + y * weights.y + z * weights.z;
            }

            fixed4 frag(v2f i) : SV_Target {
                float3 toCamera = _WorldSpaceCameraPos - i.worldPos;
                float distance = length(toCamera);
                float3 view = toCamera / max(distance, 0.0001);
                float depth = saturate((distance - _NearDistance) / max(_FarDistance - _NearDistance, 0.0001));
                float3 dir = normalize(i.sphereDir);
                float cloudA = SampleTriplanar(_CloudA, _CloudA_TexelSize, dir, _CloudScale.x, _Time.y * _CloudVelocityA.xy);
                float cloudB = SampleTriplanar(_CloudB, _CloudB_TexelSize, dir, _CloudScale.y, _Time.y * _CloudVelocityB.xy);
                // Multiplied, then doubled back up, so the two only reinforce where they cross: a shifting, patchy field.
                float cloud = saturate(cloudA * cloudB * 2.0);
                float alpha = depth * _MaxAlpha;
                // abs: seen from inside, the normal points away from the camera.
                float facing = abs(dot(normalize(i.worldNormal), view));
                alpha += pow(1.0 - facing, _RimPower) * _RimAlpha;
                alpha *= lerp(1.0, cloud * 1.6, _CloudStrength);
                float3 color = _Color.rgb * _Glow * (1.0 + (cloud - 0.5) * _CloudGlow);
                // Only partly fogged: fully fogged, its far side turns the fog's color and disappears into it.
                color = lerp(color, WKT_ApplyFog(color, i.worldPos), _FogStrength);
                return fixed4(color, saturate(alpha) * _Color.a);
            }
            ENDCG
        }
    }
    Fallback Off
}
