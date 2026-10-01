// Stylized water surface for the sandbox: depth-graded colour, shoreline foam, refraction,
// fresnel sky reflection, sun specular and rolling waves.
//
// The whole look hangs off one idea: water reads as water because of what's BEHIND it, not
// what's on it. So the two things doing the heavy lifting here are the scene depth sample
// (how deep is the water at this pixel -> colour ramp + foam) and the opaque-texture sample
// (what's under it, bent by the surface normal). Everything else is polish on top.
Shader "WowSandbox/Water"
{
    Properties
    {
        [Header(Colour)]
        _ShallowColor ("Shallow", Color) = (0.30, 0.68, 0.72, 0.55)
        _DeepColor ("Deep", Color) = (0.02, 0.16, 0.30, 0.95)
        _DepthMaxDistance ("Depth to full deep colour", Float) = 6.0

        [Header(Shoreline)]
        _FoamColor ("Foam", Color) = (1, 1, 1, 1)
        _FoamDistance ("Foam width", Float) = 0.7
        _FoamCutoff ("Foam noise cutoff", Range(0, 1)) = 0.55

        [Header(Surface Normals)]
        _NormalMapA ("Normal A", 2D) = "bump" {}
        _NormalMapB ("Normal B", 2D) = "bump" {}
        _NormalStrength ("Normal strength", Range(0, 2)) = 0.6
        // Two layers scrolling in different directions at different scales. One layer alone
        // reads as a sliding texture; two crossing layers read as moving water.
        _ScrollA ("Scroll A (xy) / tiling (zw)", Vector) = (0.03, 0.02, 12, 12)
        _ScrollB ("Scroll B (xy) / tiling (zw)", Vector) = (-0.02, 0.035, 25, 25)

        [Header(Refraction)]
        _RefractionStrength ("Refraction strength", Range(0, 0.2)) = 0.035

        [Header(Reflection)]
        _FresnelPower ("Fresnel power", Range(0.5, 8)) = 4.0
        _ReflectionStrength ("Reflection strength", Range(0, 1)) = 0.7
        _Roughness ("Reflection blur", Range(0, 1)) = 0.08

        [Header(Sun)]
        _SpecularColor ("Specular tint", Color) = (1, 0.97, 0.88, 1)
        _SpecularPower ("Specular tightness", Range(8, 512)) = 180
        _SpecularStrength ("Specular strength", Range(0, 4)) = 1.4

        // No wave properties here: the WaterWaves component owns the wave set and pushes it
        // through a MaterialPropertyBlock, because gameplay has to sample the exact same
        // surface on the CPU. Without that component the water is simply flat.

        [Header(Underside)]
        _UnderwaterTint ("Seen from below", Color) = (0.08, 0.28, 0.34, 0.85)

        [Toggle(_REFRACTION_ON)] _RefractionOn ("Enable refraction", Float) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "WaterSurface"
            Tags { "LightMode" = "UniversalForward" }

            // Cull Off is not an optimisation oversight -- you can swim under this surface,
            // and a one-sided plane would vanish the moment the camera dips below it. The
            // fragment shader flips the normal for back faces so fresnel and specular stay
            // correct from underneath.
            Cull Off
            ZWrite Off       // transparent: never occlude, and never hide what's below it
            ZTest LEqual
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            // Refraction needs the opaque texture, which the Mobile URP asset doesn't render.
            // Keeping it a keyword means the same material degrades instead of sampling garbage.
            #pragma shader_feature_local_fragment _REFRACTION_ON

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float2 uv         : TEXCOORD2;
                float  fogFactor  : TEXCOORD3;
            };

            TEXTURE2D(_NormalMapA);  SAMPLER(sampler_NormalMapA);
            TEXTURE2D(_NormalMapB);  SAMPLER(sampler_NormalMapB);

            CBUFFER_START(UnityPerMaterial)
                float4 _ShallowColor;
                float4 _DeepColor;
                float  _DepthMaxDistance;
                float4 _FoamColor;
                float  _FoamDistance;
                float  _FoamCutoff;
                float4 _NormalMapA_ST;
                float4 _NormalMapB_ST;
                float  _NormalStrength;
                float4 _ScrollA;
                float4 _ScrollB;
                float  _RefractionStrength;
                float  _FresnelPower;
                float  _ReflectionStrength;
                float  _Roughness;
                float4 _SpecularColor;
                float  _SpecularPower;
                float  _SpecularStrength;
                float4 _UnderwaterTint;
            CBUFFER_END

            // Set per renderer by WaterWaves, outside the material cbuffer because they're
            // not material properties. All zero (flat water) when that component is absent.
            #define MAX_WAVES 16
            float4 _WaveA[MAX_WAVES];   // xy = direction, z = wavenumber k, w = angular frequency
            float4 _WaveB[MAX_WAVES];   // x = amplitude, y = phase, z = 1 if displaced by the mesh
            float  _WaveCount;
            float  _WaveTime;
            float  _WaveSharpness;      // 1 = sine; higher = peaked crests, flat troughs
            float  _WaveMean;           // mean of the reshaped wave, subtracted to keep sea level put
            float  _WaveHeight;
            float  _Whitecaps;

            // Sum of crossing waves in world XZ, each a sine reshaped to ((sin+1)/2)^sharpness
            // so storms get sharp crests. Displacement is vertical only, so the surface can be
            // queried at a point -- WaterWaves.HeightAt mirrors this line for line, and that's
            // what keeps swimming and breath in agreement with what's drawn. Change one,
            // change the other.
            //
            // displacedOnly: the vertex shader skips waves too short for the grid to show (they
            // would alias); the fragment shader keeps them all, as lighting detail.
            float WaveHeight(float2 positionXZ, bool displacedOnly, out float2 slope)
            {
                float height = 0.0;
                slope = float2(0.0, 0.0);
                int count = (int)_WaveCount;

                for (int i = 0; i < MAX_WAVES; i++)
                {
                    if (i >= count)
                        break;

                    float4 a = _WaveA[i];
                    float4 b = _WaveB[i];
                    if (displacedOnly && b.z < 0.5)
                        continue;

                    float theta = dot(a.xy, positionXZ) * a.z - a.w * _WaveTime + b.y;
                    float s, c;
                    sincos(theta, s, c);

                    float u = max(s * 0.5 + 0.5, 1e-4);
                    float shaped = pow(u, _WaveSharpness);
                    height += 2.0 * b.x * (shaped - _WaveMean);

                    // d/dtheta of 2A*u^p is A*p*u^(p-1)*cos; chain through theta for XZ.
                    float dTheta = b.x * _WaveSharpness * (shaped / u) * c;
                    slope += dTheta * a.z * a.xy;
                }

                return height;
            }

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);

                float3 positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                float2 unusedSlope;
                // Vertical only, so the XZ the fragment shader gets is the undisplaced XZ.
                positionWS.y += WaveHeight(positionWS.xz, true, unusedSlope);

                OUT.positionWS = positionWS;
                OUT.positionCS = TransformWorldToHClip(positionWS);
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                OUT.uv = IN.uv;
                OUT.fogFactor = ComputeFogFactor(OUT.positionCS.z);
                return OUT;
            }

            half3 SampleWaterNormal(float2 positionXZ)
            {
                float2 uvA = positionXZ / max(_ScrollA.zw, 0.001) + _ScrollA.xy * _Time.y;
                float2 uvB = positionXZ / max(_ScrollB.zw, 0.001) + _ScrollB.xy * _Time.y;

                // UnpackNormalRGB, not UnpackNormal. WaterSetup builds these maps in code as
                // Texture2D assets, which never pass through a TextureImporter and so can't
                // be tagged as normal maps -- UnpackNormal would read them as DXT5nm on
                // desktop (x from alpha, y from green) and the ripples would come out wrong.
                half3 nA = UnpackNormalRGB(SAMPLE_TEXTURE2D(_NormalMapA, sampler_NormalMapA, uvA), 1.0);
                half3 nB = UnpackNormalRGB(SAMPLE_TEXTURE2D(_NormalMapB, sampler_NormalMapB, uvB), 1.0);

                // Whiteout blend: add the tangents, keep the z. Cheaper than a full RNM and
                // it doesn't flatten the detail the way a plain lerp does.
                half3 blended = normalize(half3(nA.xy + nB.xy, nA.z * nB.z));
                return normalize(lerp(half3(0, 0, 1), blended, _NormalStrength));
            }

            half4 frag (Varyings IN, FRONT_FACE_TYPE facing : FRONT_FACE_SEMANTIC) : SV_Target
            {
                bool isTopSide = IS_FRONT_VFACE(facing, true, false);

                // The wave normal, evaluated per pixel from the analytic slope rather than
                // interpolated from vertices -- so the short waves the grid can't displace
                // still catch the light. Flipped when looking up from underneath so every
                // lighting term below keeps the right sign.
                float2 slope;
                float waveHeight = WaveHeight(IN.positionWS.xz, false, slope);
                float3 waveNormal = normalize(float3(-slope.x, 1.0, -slope.y));
                float3 geometricNormal = waveNormal * (isTopSide ? 1.0 : -1.0);

                // Perturb around the geometric normal. The normal map is authored in tangent
                // space on a flat XZ plane, so its tangent basis is just world X/Z -- no
                // interpolated tangents needed.
                half3 tangentNormal = SampleWaterNormal(IN.positionWS.xz);
                float3 normalWS = normalize(float3(
                    geometricNormal.x + tangentNormal.x,
                    geometricNormal.y,
                    geometricNormal.z + tangentNormal.y));

                float3 viewDirWS = normalize(GetWorldSpaceViewDir(IN.positionWS));
                float2 screenUV = GetNormalizedScreenSpaceUV(IN.positionCS);

                // --- How deep is the water here? ------------------------------------
                // Eye depth of whatever opaque geometry sits behind this pixel, minus the
                // eye depth of the water surface itself. This is the whole trick.
                float sceneDepth = LinearEyeDepth(SampleSceneDepth(screenUV), _ZBufferParams);
                float surfaceDepth = IN.positionCS.w;
                float waterDepth = max(sceneDepth - surfaceDepth, 0.0);

                float depthT = saturate(waterDepth / max(_DepthMaxDistance, 0.001));
                half4 water = lerp(_ShallowColor, _DeepColor, depthT);

                // --- Refraction ------------------------------------------------------
                half3 behind = half3(0, 0, 0);
                half refractionMask = 0;

            #ifdef _REFRACTION_ON
                // Offset by the ripples only. The big wave slopes would shove the sample half
                // a screen away in a storm and smear the lake bed across the surface.
                float2 offsetUV = screenUV + tangentNormal.xy * _RefractionStrength;

                // Guard against the classic bleed: if the offset lands on geometry that is
                // actually IN FRONT of the water (a rock at the shoreline, the player's own
                // legs), we'd smear that object across the surface. Detect it by depth and
                // fall back to the unoffset sample for those pixels.
                float offsetSceneDepth = LinearEyeDepth(SampleSceneDepth(offsetUV), _ZBufferParams);
                offsetUV = offsetSceneDepth < surfaceDepth ? screenUV : offsetUV;

                behind = SampleSceneColor(offsetUV);
                // Deep water hides what's under it; shallow water shows the bed through.
                refractionMask = 1.0 - depthT;
            #endif

                // --- Fresnel sky reflection -----------------------------------------
                half fresnel = pow(1.0 - saturate(dot(normalWS, viewDirWS)), _FresnelPower);

                half3 reflection = half3(0, 0, 0);
                if (isTopSide)
                {
                    float3 reflectVector = reflect(-viewDirWS, normalWS);
                    half mip = PerceptualRoughnessToMipmapLevel(_Roughness);
                    half4 encoded = SAMPLE_TEXTURECUBE_LOD(unity_SpecCube0, samplerunity_SpecCube0,
                                                           reflectVector, mip);
                    // Sampling the probe directly rather than going through
                    // GlossyEnvironmentReflection: we only ever want the sky/probe here, and
                    // the direct call is stable across URP versions.
                    reflection = DecodeHDREnvironment(encoded, unity_SpecCube0_HDR) * _ReflectionStrength;
                }

                // --- Sun specular ----------------------------------------------------
                Light mainLight = GetMainLight();
                float3 halfDir = normalize(mainLight.direction + viewDirWS);
                half specular = pow(saturate(dot(normalWS, halfDir)), _SpecularPower);
                half3 sun = specular * _SpecularStrength * _SpecularColor.rgb * mainLight.color;

                // --- Shoreline foam ---------------------------------------------------
                // A band where the water is shallow. The normal map doubles as the noise
                // source so the edge wobbles with the surface instead of drawing a hard ring.
                half foamEdge = 1.0 - saturate(waterDepth / max(_FoamDistance, 0.001));
                half foamNoise = saturate(tangentNormal.x + tangentNormal.y + 0.5);
                half foam = step(_FoamCutoff, foamEdge * foamEdge + foamNoise * foamEdge * 0.5);

                // --- Whitecaps --------------------------------------------------------
                // Foam on whatever stands highest relative to the current sea state, broken up
                // by the same ripple noise. More whitecaps lowers the bar for "high enough".
                half crest = waveHeight / max(_WaveHeight * 0.5, 0.001) + (foamNoise - 0.5) * 0.4;
                half threshold = 1.0 - _Whitecaps * 0.9;
                half whitecap = _Whitecaps > 0.001 ? smoothstep(threshold, threshold + 0.15, crest) : 0.0;
                foam = max(foam, whitecap);

                // --- Composite --------------------------------------------------------
                half3 color;
                half alpha;

                if (isTopSide)
                {
                    color = lerp(water.rgb, behind, refractionMask * 0.6);
                    color = lerp(color, reflection, fresnel);
                    color += sun;
                    color = lerp(color, _FoamColor.rgb, foam);
                    alpha = lerp(water.a, 1.0, foam);
                }
                else
                {
                    // From below there's no sky to reflect -- looking up you see the murk of
                    // the water column and a bent view of the world above the surface.
                    color = lerp(_UnderwaterTint.rgb, behind, refractionMask * 0.5);
                    color += sun * fresnel;
                    alpha = _UnderwaterTint.a;
                }

                color = MixFog(color, IN.fogFactor);
                return half4(color, saturate(alpha));
            }
            ENDHLSL
        }
    }

    Fallback Off
}
