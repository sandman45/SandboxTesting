// A flat overcast laid over the procedural sky, for storms.
//
// Skybox/Procedural can't go grey: it's a scattering model, and however it's tinted, thinned
// or dimmed it scatters blue — a storm just made it a darker blue under grey clouds. This
// covers it in the same colour the sky dome's cloud layers are pulled toward, faded in by the
// same _WeatherOvercast value, so the sky and the clouds grey over (and clear) together.
//
// Drawn on an inside-out sphere StormWeather pins to the camera, after the skybox and before
// the cloud dome (Transparent-100), so the clouds still sit on top of it.
Shader "WowSandbox/OvercastSky"
{
    Properties
    {
        _Brightness ("Brightness vs. the clouds", Range(0.3, 1.5)) = 0.85
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Transparent-110"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "OvercastSky"
            Cull Front
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
            };

            CBUFFER_START(UnityPerMaterial)
                float _Brightness;
            CBUFFER_END

            // Same globals the sky dome and water read.
            float4 _WeatherOvercastColor;
            float  _WeatherOvercast;
            float  _WeatherFlash;

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                half3 color = _WeatherOvercastColor.rgb * _Brightness;
                color += _WeatherFlash * half3(0.75, 0.8, 0.95);
                return half4(color, saturate(_WeatherOvercast));
            }
            ENDHLSL
        }
    }

    Fallback Off
}
