// The sky's clouds: a layer of them, raymarched, for CloudLayer.
//
// The view ray is marched through a slab of sky between two heights (_CloudHeight), curved
// like a planet's so it meets the horizon instead of running off to infinity. Where clouds
// are comes from a tileable 2D coverage map (_WeatherMap), thresholded by _CloudCoverage, so
// one slider runs from a clear sky through scattered puffs to solid overcast; their edges are
// eaten away by 3D noise (_DetailNoise) for the billowing shape. Both textures are built by
// CloudLayer at Play, so there's nothing to export or keep.
//
// Lighting is the real sun or moon (_CloudSunDirection/_CloudSunColor, the scene's main
// light) and the scene's ambient above and below (_CloudAmbientTop/Bottom), so the time of
// day, the storm's gloom and the night all reach the clouds with no colours of their own:
// Beer-Lambert absorption toward the light, a "powder" term that darkens thick cores, and a
// forward-scattering phase that rims clouds near the sun in silver (_CloudSilverLining).
// Lightning (_WeatherFlash) lights them from inside.
//
// It also greys the sky behind the clouds in a storm (_WeatherOvercast), which the procedural
// skybox can't do: it scatters blue however it's tinted.
//
// Drawn on an inside-out sphere pinned to the camera: after the stars (Transparent-120), so
// cloud covers them; before the horizon fog band (Transparent-90), which hides the far edge.
Shader "WowSandbox/Clouds"
{
    Properties
    {
        _Steps ("March steps", Range(8, 64)) = 24
        // The layer meets the horizon about 4000 out (at a 420 base, on CloudLayer's planet);
        // fading from just short of that keeps the far edge from shimmering.
        _FadeDistance ("Fade out by (distance)", Float) = 8000
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Transparent-105"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "Clouds"
            Cull Front       // seen from inside the sphere
            ZWrite Off
            ZTest LEqual
            // Premultiplied: the colour carries its own coverage.
            Blend One OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5

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
                float _Steps;
                float _FadeDistance;
            CBUFFER_END

            TEXTURE2D(_WeatherMap);
            SAMPLER(sampler_WeatherMap);
            TEXTURE3D(_DetailNoise);
            SAMPLER(sampler_DetailNoise);

            // Set by CloudLayer each frame.
            float  _CloudCoverage;        // 0 clear .. 1 solid
            float  _CloudDensity;         // how thick (and dark) they are
            float  _CloudScale;           // coverage map repeats per world unit
            float4 _CloudHeight;          // x base, y top (world units above sea level), z planet radius
            float  _CloudSoftness;        // 0 crisp edges .. 1 wispy
            float4 _CloudWind;            // xy coverage map offset (map units), zw detail offset
            float4 _CloudSunDirection;    // toward the light
            float4 _CloudSunColor;        // its colour times intensity
            float4 _CloudAmbientTop;
            float4 _CloudAmbientBottom;
            float  _CloudSunlight;        // how much of the light the clouds take
            float  _CloudSilverLining;    // forward scattering near the sun, 0..1
            float  _CloudOvercastBrightness; // the grey behind the clouds, vs. the storm's overcast colour
            // StormWeather's.
            float4 _WeatherOvercastColor;
            float  _WeatherOvercast;
            float  _WeatherFlash;
            float4 _DayNightTint;         // DayNightCycle; alpha 0 = no cycle

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                float3 positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionCS = TransformWorldToHClip(positionWS);
                // The sphere is centred on the camera, so this is the view direction.
                OUT.directionWS = positionWS - GetCameraPositionWS();
                return OUT;
            }

            // Distance along a ray from inside a sphere of radius r (centred below the camera at
            // the planet's centre) to where it leaves it.
            float ExitSphere(float cameraRadius, float directionY, float r)
            {
                float b = cameraRadius * directionY;
                float c = cameraRadius * cameraRadius - r * r;
                return -b + sqrt(max(b * b - c, 0.0));
            }

            float Remap(float value, float from, float to)
            {
                return saturate((value - from) / max(to - from, 1e-4));
            }

            // Cloud density at a point, given its height through the layer (0 base, 1 top).
            float Density(float3 position, float height01)
            {
                float2 uv = position.xz * _CloudScale + _CloudWind.xy;
                float2 map = SAMPLE_TEXTURE2D_LOD(_WeatherMap, sampler_WeatherMap, uv, 0).rg;
                // Where the clouds are: the coverage map, thresholded. A second, finer look at the
                // same map breaks up the big shapes so they aren't all blobs of one size.
                float field = map.r * 0.8 + SAMPLE_TEXTURE2D_LOD(_WeatherMap, sampler_WeatherMap, uv * 3.1 + 0.37, 0).r * 0.2;
                float cover = Remap(field, 1.0 - _CloudCoverage, 1.0);
                if (cover <= 0.0)
                    return 0.0;

                // Flat bottoms and rounded tops; thicker cover grows taller (map.g varies it).
                float top = lerp(0.35, 1.0, saturate(cover * 1.4) * lerp(0.7, 1.0, map.g));
                float profile = Remap(height01, 0.0, 0.08) * (1.0 - Remap(height01, top * 0.55, top));
                float shape = cover * profile;
                if (shape <= 0.0)
                    return 0.0;

                // Eaten away at the edges by the billowing detail noise.
                float3 detailPosition = position * (_CloudScale * 9.0) + float3(_CloudWind.z, height01 * 0.3, _CloudWind.w);
                float detail = SAMPLE_TEXTURE3D_LOD(_DetailNoise, sampler_DetailNoise, detailPosition, 0).r;
                float erosion = lerp(0.25, 0.65, _CloudSoftness) * (1.0 - shape * 0.5);
                return Remap(shape, detail * erosion, 1.0) * _CloudDensity;
            }

            // How far up through the layer a point is, 0 base to 1 top, on the curved layer: the
            // planet's centre is straight below the camera.
            float HeightInLayer(float3 position, float3 camera, float planet, float base, float thickness)
            {
                return saturate((length(position - float3(camera.x, -planet, camera.z)) - planet - base) / thickness);
            }

            // Henyey-Greenstein: how much light scatters toward the eye at this angle to the sun.
            float Phase(float cosAngle, float g)
            {
                float g2 = g * g;
                return (1.0 - g2) / (4.0 * PI * pow(max(1.0 + g2 - 2.0 * g * cosAngle, 1e-4), 1.5));
            }

            // Interleaved gradient noise: a fixed per-pixel jitter on the march, which trades the
            // banding of few steps for a fine grain.
            float Dither(float2 pixel)
            {
                return frac(52.9829189 * frac(dot(pixel, float2(0.06711056, 0.00583715))));
            }

            half4 frag (Varyings IN) : SV_Target
            {
                float3 direction = normalize(IN.directionWS);
                float3 camera = GetCameraPositionWS();
                float planet = _CloudHeight.z;
                float base = _CloudHeight.x;
                float top = max(_CloudHeight.y, base + 1.0);
                float thickness = top - base;

                // Behind the clouds, in a storm, the sky itself greys over.
                float overcast = saturate(_WeatherOvercast);
                half3 overcastColor = _WeatherOvercastColor.rgb * _CloudOvercastBrightness
                                    * lerp(half3(1, 1, 1), _DayNightTint.rgb, _DayNightTint.a)
                                    + _WeatherFlash * half3(0.75, 0.8, 0.95);

                float transmittance = 1.0;
                float3 light = 0.0;

                // Only from below the layer, looking at it: past the horizon a curved layer is
                // always met, so the clouds run down to it.
                float cameraRadius = planet + camera.y;
                float start = ExitSphere(cameraRadius, direction.y, planet + base);
                float end = ExitSphere(cameraRadius, direction.y, planet + top);
                float fade = 1.0 - smoothstep(_FadeDistance * 0.45, _FadeDistance, start);

                if (_CloudCoverage > 0.001 && camera.y < base && fade > 0.0)
                {
                    int steps = (int)_Steps;
                    float stepLength = (end - start) / steps;
                    float3 sun = normalize(_CloudSunDirection.xyz);
                    float cosAngle = dot(direction, sun);
                    // Mostly forward scattering (the silver lining), a little back.
                    float phase = lerp(Phase(cosAngle, -0.2), Phase(cosAngle, 0.7), 0.5 + 0.4 * _CloudSilverLining)
                                * (1.0 + 3.0 * _CloudSilverLining * pow(saturate(cosAngle), 8.0));
                    float3 sunLight = _CloudSunColor.rgb * _CloudSunlight;
                    float lightStep = thickness * 0.18;
                    float extinction = 0.012;
                    float t = start + stepLength * Dither(IN.positionCS.xy);

                    [loop]
                    for (int i = 0; i < steps; i++)
                    {
                        float3 position = camera + direction * t;
                        float height01 = HeightInLayer(position, camera, planet, base, thickness);
                        float density = Density(position, height01);
                        if (density > 0.001)
                        {
                            // Toward the light: how much cloud is in the way.
                            float toLight = 0.0;
                            [unroll]
                            for (int j = 1; j <= 3; j++)
                            {
                                float3 lightPosition = position + sun * (lightStep * j * j * 0.5);
                                float lightHeight = HeightInLayer(lightPosition, camera, planet, base, thickness);
                                toLight += Density(lightPosition, lightHeight) * lightStep * j;
                            }
                            float sunReach = exp(-toLight * extinction * 0.6);
                            float powder = 1.0 - exp(-density * 2.0);
                            float3 ambient = lerp(_CloudAmbientBottom.rgb, _CloudAmbientTop.rgb, height01);
                            float3 scattered = sunLight * sunReach * phase * lerp(1.0, powder, 0.6) * 4.0 * PI
                                             + ambient
                                             + _WeatherFlash * float3(0.8, 0.85, 1.0) * 1.5;

                            // Energy-conserving step: what this slice adds, dimmed by what's in front.
                            float sigma = density * extinction;
                            float stepTransmittance = exp(-sigma * stepLength);
                            light += transmittance * scattered * (1.0 - stepTransmittance);
                            transmittance *= stepTransmittance;
                            if (transmittance < 0.01)
                                break;
                        }
                        t += stepLength;
                    }

                    // Far off they fade, rather than shimmering into a band at the horizon.
                    light *= fade;
                    transmittance = lerp(1.0, transmittance, fade);
                }

                half3 color = light + transmittance * overcastColor * overcast;
                half alpha = 1.0 - transmittance * (1.0 - overcast);
                return half4(color, saturate(alpha));
            }
            ENDHLSL
        }
    }

    Fallback Off
}
