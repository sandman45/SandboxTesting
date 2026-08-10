// Unlit, inside-out, depth-less material for WoW sky dome models.
//
// A WoW skybox is a stack of dome meshes with cloud textures, meant to be viewed from
// inside with the layers blending over each other. That needs four things the standard
// URP shaders don't give together: front-face culling (we're inside the dome), no depth
// write, the Background queue so all real geometry paints over it, and no lighting.
Shader "WowSandbox/SkyDome"
{
    Properties
    {
        _BaseMap ("Cloud Layer", 2D) = "white" {}
        _BaseColor ("Tint", Color) = (1, 1, 1, 1)
        _ScrollSpeed ("Scroll Speed (XY)", Vector) = (0.004, 0.0, 0, 0)
        [Toggle(_ADDITIVE)] _Additive ("Additive Blend", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Transparent"
            // Transparent, not Background. These meshes are cloud layers, not a sky: their
            // texture is mostly transparent, so drawing them before the skybox left the gaps
            // showing the bare camera clear colour. Drawing after the skybox lets the
            // procedural sky supply the blue behind them, which is how WoW composites it too.
            "Queue" = "Transparent-100"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "SkyDome"

            // These domes are authored inside-out already, so the faces visible from within
            // are the front ones -- Cull Front hid the sky entirely, and Cull Off drew each
            // shell twice (near side and far side at once), which looks like overlapping
            // bands of cloud. Culling the back faces leaves exactly the interior surface.
            Cull Back
            ZWrite Off       // never occlude real geometry
            ZTest LEqual
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _BaseColor;
                float4 _ScrollSpeed;
            CBUFFER_END

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);

                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                // Drift the clouds. The M2's own bone animation also moves them, but this
                // works whether or not the animation is playing.
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap) + _ScrollSpeed.xy * _Time.y;
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                return SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv) * _BaseColor;
            }
            ENDHLSL
        }
    }

    Fallback Off
}
