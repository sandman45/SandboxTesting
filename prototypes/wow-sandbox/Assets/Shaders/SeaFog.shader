// Sea fog for SeaFog: a bank of it standing on the water in a ring round the island, and a low
// mist over the water and the beach.
//
// Raymarched through real space, from the camera to whatever the ray meets: the scene's depth
// (_CameraDepthTexture: the PC renderer has Depth Texture on), the sea's surface (the water
// writes no depth, so it's found by height), or the far end of the fog. So it hides hulls and
// headlands inside the bank, not only the sky behind them, and nothing under the water.
//
// Where fog is: _FogBank — from x world units out past the island's edge (the terrain's rect,
// _FogIsland), y wide, z tall — plus a mist everywhere _FogMist.y tall, both thinning upward and
// broken up by the clouds' own noise (CloudLayer's _WeatherMap and _DetailNoise), drifting.
// Lit like the clouds: the scene's sun or moon and ambient (CloudLayer's _Cloud* lighting), so it
// glows gold at dawn and goes dark at night with everything else.
//
// Drawn on an inside-out sphere pinned to the camera, after the water (Transparent) so it fogs
// the sea, with ZTest Always: the march itself decides how far each pixel's fog reaches.
Shader "WowSandbox/SeaFog"
{
    Properties
    {
        _Steps ("March steps", Range(8, 48)) = 24
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Transparent+20"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "SeaFog"
            Cull Front       // seen from inside the sphere
            ZWrite Off
            ZTest Always
            Blend One OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

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
                float _Steps;
            CBUFFER_END

            TEXTURE2D(_WeatherMap);
            SAMPLER(sampler_WeatherMap);
            TEXTURE3D(_DetailNoise);
            SAMPLER(sampler_DetailNoise);

            // Set by SeaFog each frame.
            float4 _FogIsland;       // xy min corner, zw max corner (world XZ)
            float4 _FogBank;         // x start past the island's edge, y width, z height, w density
            float4 _FogMist;         // x density, y height
            float4 _FogShape;        // x water level, y how far the fog reaches, z visibility (extinction), w noise scale
            float4 _FogDrift;        // xy noise offset (map units), zw detail offset
            // CloudLayer's light, which is the scene's.
            float4 _CloudSunDirection;
            float4 _CloudSunColor;
            float4 _CloudAmbientTop;
            float4 _CloudAmbientBottom;
            float  _WeatherFlash;

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                float3 positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionCS = TransformWorldToHClip(positionWS);
                OUT.directionWS = positionWS - GetCameraPositionWS();
                return OUT;
            }

            // How far a point is outside the island's rect (0 inside it).
            float OutsideIsland(float2 p)
            {
                float2 centre = (_FogIsland.xy + _FogIsland.zw) * 0.5;
                float2 extent = (_FogIsland.zw - _FogIsland.xy) * 0.5;
                float2 d = abs(p - centre) - extent;
                return length(max(d, 0.0));
            }

            float Density(float3 position)
            {
                float above = max(position.y - _FogShape.x, 0.0);

                // The bank: a ring out past the island, soft at both edges, thinning upward.
                float outside = OutsideIsland(position.xz);
                float start = _FogBank.x, width = max(_FogBank.y, 1.0);
                float ring = smoothstep(start - width * 0.35, start + width * 0.15, outside)
                           * (1.0 - smoothstep(start + width * 0.7, start + width * 1.2, outside));
                float bank = ring * exp(-above / max(_FogBank.z, 0.5)) * _FogBank.w;

                // The mist: everywhere, low.
                float mist = exp(-above / max(_FogMist.y, 0.2)) * _FogMist.x;

                float fog = bank + mist;
                if (fog <= 0.0001)
                    return 0.0;

                // Billows and gaps, drifting.
                float2 uv = position.xz * _FogShape.w + _FogDrift.xy;
                float lumps = SAMPLE_TEXTURE2D_LOD(_WeatherMap, sampler_WeatherMap, uv, 0).r;
                float3 detailPosition = position * (_FogShape.w * 6.0) + float3(_FogDrift.z, 0.0, _FogDrift.w);
                float detail = SAMPLE_TEXTURE3D_LOD(_DetailNoise, sampler_DetailNoise, detailPosition, 0).r;
                float breakup = saturate(lumps * 1.6 - 0.3) * lerp(0.55, 1.0, detail);
                return fog * lerp(0.35, 1.6, breakup);
            }

            float Dither(float2 pixel)
            {
                return frac(52.9829189 * frac(dot(pixel, float2(0.06711056, 0.00583715))));
            }

            half4 frag (Varyings IN) : SV_Target
            {
                float3 direction = normalize(IN.directionWS);
                float3 camera = GetCameraPositionWS();

                // How far this pixel's ray goes: the scene's depth, along the ray, or the sea.
                float2 screenUV = IN.positionCS.xy / _ScaledScreenParams.xy;
                float rawDepth = SampleSceneDepth(screenUV);
                float3 forward = -UNITY_MATRIX_V[2].xyz;
                float sceneDistance = LinearEyeDepth(rawDepth, _ZBufferParams) / max(dot(direction, forward), 1e-3);
                float end = min(sceneDistance, _FogShape.y);
                if (direction.y < -1e-4 && camera.y > _FogShape.x)
                    end = min(end, (_FogShape.x - camera.y) / direction.y);
                if (end <= 0.0)
                    return 0;

                int steps = (int)_Steps;
                float3 sun = normalize(_CloudSunDirection.xyz);
                float cosAngle = dot(direction, sun);
                // A soft glow toward the light, as fog has.
                float forwardScatter = 0.6 + 1.4 * pow(saturate(cosAngle), 6.0);
                float3 ambient = (_CloudAmbientTop.rgb + _CloudAmbientBottom.rgb) * 0.5;
                float3 lit = ambient * 1.1 + _CloudSunColor.rgb * 0.35 * forwardScatter
                           + _WeatherFlash * float3(0.8, 0.85, 1.0);

                float transmittance = 1.0;
                float extinction = _FogShape.z;
                float jitter = Dither(IN.positionCS.xy);
                float previous = 0.0;

                // Steps bunched up near the camera, where the mist is, and spread out far off.
                [loop]
                for (int i = 1; i <= steps; i++)
                {
                    float f = (i - 1.0 + jitter) / steps;
                    float t = end * f * f;
                    float stepLength = t - previous;
                    previous = t;
                    float density = Density(camera + direction * t);
                    if (density > 0.0001)
                    {
                        float stepTransmittance = exp(-density * extinction * stepLength);
                        transmittance *= stepTransmittance;
                        if (transmittance < 0.01)
                            break;
                    }
                }
                // The last stretch, to the end.
                float lastDensity = Density(camera + direction * end);
                transmittance *= exp(-lastDensity * extinction * (end - previous));

                float alpha = 1.0 - transmittance;
                return half4(lit * alpha, alpha);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
