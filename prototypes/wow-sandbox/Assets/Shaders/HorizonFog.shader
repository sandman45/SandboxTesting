// A fog-coloured band around the horizon, for weather.
//
// Neither the procedural skybox nor the WoW sky dome takes fog, and the dome doesn't reach
// down to the horizon. So when storm fog closes in, the hills fade to grey while a strip of
// clear bright sky stays lit all the way round below the clouds. This paints that strip in
// the current fog colour: solid at and below the horizon, fading out a little way up.
//
// Drawn on an inside-out sphere that StormWeather pins to the camera just inside the far
// clip. Terrain nearer than that is in front of it and fogs normally; anything beyond it is
// already fully fogged, so the band meets the terrain at the same colour with no seam.
Shader "WowSandbox/HorizonFog"
{
    Properties
    {
        _Strength ("Strength", Range(0, 1)) = 1
        _FadeStart ("Solid up to (elevation)", Range(-0.2, 0.5)) = 0.02
        _FadeEnd ("Gone by (elevation)", Range(0, 0.8)) = 0.28
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Transparent"
            // After the skybox and after the sky dome (Transparent-100), so it covers the
            // dome's lower edge too; before the water (Transparent), which fogs itself.
            "Queue" = "Transparent-90"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "HorizonFog"
            Cull Front       // seen from inside the sphere
            ZWrite Off
            ZTest LEqual
            Blend SrcAlpha OneMinusSrcAlpha

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
                float3 positionWS : TEXCOORD0;
            };

            CBUFFER_START(UnityPerMaterial)
                float _Strength;
                float _FadeStart;
                float _FadeEnd;
            CBUFFER_END

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                OUT.positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionCS = TransformWorldToHClip(OUT.positionWS);
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                // Elevation of the view ray: 0 at the horizon, 1 straight up.
                float elevation = normalize(IN.positionWS - _WorldSpaceCameraPos).y;
                half alpha = 1.0 - smoothstep(_FadeStart, max(_FadeEnd, _FadeStart + 0.001), elevation);

                // unity_FogColor is whatever the fog is right now -- storm grey, the lightning
                // flash, or the underwater green -- so the band always matches it.
                return half4(unity_FogColor.rgb, alpha * _Strength);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
