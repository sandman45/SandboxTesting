// Stars and the moon, for DayNightCycle.
//
// Drawn on an inside-out sphere pinned to the camera, after the procedural skybox and before
// the clouds (Transparent-105), so cloud and overcast cover it the way they'd cover the real
// thing. Additive: by day it adds nothing.
//
// The stars are procedural — a hashed grid over the view direction — so there's no texture
// to export or keep. The grid is turned with the hour (_StarRotation) so the sky wheels
// overhead through the night. The moon sits exactly where the night's light comes from.
Shader "WowSandbox/NightSky"
{
    Properties
    {
        _StarDensity ("Star density", Range(40, 400)) = 160
        _StarSize ("Star size", Range(0.05, 0.5)) = 0.2
        _StarBrightness ("Star brightness", Range(0, 4)) = 1.4
        _MoonSize ("Moon radius (radians)", Range(0.005, 0.1)) = 0.032
        _MoonBrightness ("Moon brightness", Range(0, 4)) = 1.1
        _MoonGlow ("Moon glow", Range(0, 1)) = 0.12
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Transparent-120"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "NightSky"
            Cull Front       // seen from inside the sphere
            ZWrite Off
            ZTest LEqual
            Blend One One

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 directionWS : TEXCOORD0;
            };

            CBUFFER_START(UnityPerMaterial)
                float _StarDensity;
                float _StarSize;
                float _StarBrightness;
                float _MoonSize;
                float _MoonBrightness;
                float _MoonGlow;
            CBUFFER_END

            // Set by DayNightCycle each frame.
            float4x4 _StarRotation;
            float4 _MoonDirection;       // toward the moon
            float4 _MoonColor;
            float  _StarVisibility;      // 0 by day, 1 in the dark
            float  _MoonVisibility;
            // StormWeather's: overcast hides the lot.
            float  _WeatherOvercast;

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                float3 positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionCS = TransformWorldToHClip(positionWS);
                // The sphere is centred on the camera, so this is the view direction.
                OUT.directionWS = positionWS - GetCameraPositionWS();
                return OUT;
            }

            float3 Hash3(float3 p)
            {
                p = frac(p * float3(0.1031, 0.1030, 0.0973));
                p += dot(p, p.yxz + 33.33);
                return frac((p.xxy + p.yxx) * p.zyx);
            }

            float Hash1(float3 p)
            {
                return Hash3(p).x;
            }

            // One star in some of the grid's cells, at a random spot within it.
            float StarLayer(float3 direction, float density, float share, float time)
            {
                float3 p = direction * density;
                float3 cell = floor(p);
                float3 random = Hash3(cell);
                if (random.x > share)
                    return 0;
                float3 centre = cell + 0.25 + Hash3(cell + 17.0) * 0.5;
                float offset = length(p - centre);
                float star = saturate(1.0 - offset / _StarSize);
                star *= star;
                // Each its own brightness, and a slow twinkle.
                float twinkle = 0.75 + 0.25 * sin(time * (1.5 + random.y * 3.0) + random.z * 40.0);
                return star * (0.3 + 0.7 * random.y) * twinkle;
            }

            float Noise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = Hash1(float3(i, 0));
                float b = Hash1(float3(i + float2(1, 0), 0));
                float c = Hash1(float3(i + float2(0, 1), 0));
                float d = Hash1(float3(i + float2(1, 1), 0));
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            half4 frag (Varyings IN) : SV_Target
            {
                float3 direction = normalize(IN.directionWS);
                // Thicker air low down washes the stars out before the horizon.
                float altitude = saturate(direction.y / 0.25);
                float clear = saturate(1.0 - _WeatherOvercast);

                float3 starDirection = mul((float3x3)_StarRotation, direction);
                float stars = StarLayer(starDirection, _StarDensity, 0.35, _Time.y)
                            + StarLayer(starDirection, _StarDensity * 2.3, 0.5, _Time.y) * 0.5;
                half3 color = half3(0.85, 0.9, 1.0) * stars * _StarBrightness * _StarVisibility * altitude;

                // The moon: a disc, darker seas on it, and a little glow round it.
                float3 moon = normalize(_MoonDirection.xyz);
                float angle = acos(clamp(dot(direction, moon), -1.0, 1.0));
                float disc = 1.0 - smoothstep(_MoonSize * 0.85, _MoonSize, angle);
                if (disc > 0)
                {
                    // Flat coordinates across the disc, for the markings.
                    float3 right = normalize(cross(moon, abs(moon.y) < 0.99 ? float3(0, 1, 0) : float3(1, 0, 0)));
                    float3 up = cross(right, moon);
                    float2 onDisc = float2(dot(direction, right), dot(direction, up)) / _MoonSize;
                    float maria = Noise(onDisc * 2.2 + 3.1) * 0.6 + Noise(onDisc * 5.0) * 0.4;
                    disc *= lerp(1.0, 0.68, smoothstep(0.45, 0.75, maria));
                    // Stars don't show through it.
                    color *= 1.0 - saturate(disc * 4.0);
                }
                float glow = exp(-angle / 0.12) * _MoonGlow;
                color += _MoonColor.rgb * (disc * _MoonBrightness + glow) * _MoonVisibility
                         * saturate(direction.y * 8.0 + 0.5);

                return half4(color * clear, 1);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
