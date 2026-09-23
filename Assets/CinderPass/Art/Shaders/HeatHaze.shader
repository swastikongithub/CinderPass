// Cinder Pass - heat shimmer. Used on particles rising over lava: refracts the opaque scene colour with
// rising noise. Strength comes from particle vertex alpha, softened against geometry and faded with distance.
Shader "CinderPass/HeatHaze"
{
    Properties
    {
        [NoScaleOffset] _NoiseTex ("Noise", 2D) = "gray" {}
        _Strength ("Distortion Strength", Float) = 0.012
        _NoiseScale ("Noise Scale", Float) = 1.3
        _RiseSpeed ("Rise Speed", Float) = 0.35
        _SoftDepth ("Soft Depth (m)", Float) = 1
        _FadeDistance ("Max Distance (m)", Float) = 140
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Transparent" "Queue" = "Transparent+10" "IgnoreProjector" = "True" }
        Pass
        {
            Name "HeatHaze"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            TEXTURE2D(_NoiseTex); SAMPLER(sampler_NoiseTex);
            CBUFFER_START(UnityPerMaterial)
                float _Strength, _NoiseScale, _RiseSpeed, _SoftDepth, _FadeDistance;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                half4 color : COLOR;
                float eyeDepth : TEXCOORD2;
            };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.uv = v.uv;
                o.color = v.color;
                o.eyeDepth = -TransformWorldToView(o.positionWS).z;
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float2 suv = GetNormalizedScreenSpaceUV(i.positionCS);
                float2 centered = i.uv * 2.0 - 1.0;
                half mask = saturate(1.0 - dot(centered, centered));
                float scene = LinearEyeDepth(SampleSceneDepth(suv), _ZBufferParams);
                half soft = saturate((scene - i.eyeDepth) / _SoftDepth);
                half distanceFade = 1.0 - saturate(i.eyeDepth / _FadeDistance);
                float2 nuv = i.positionWS.xz * 0.05 * _NoiseScale + float2(0, -_Time.y * _RiseSpeed) + i.uv * _NoiseScale;
                half2 n = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, nuv).rg + SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, nuv * 2.3 + 0.4).gr * 0.5;
                float2 offset = (n - 0.75) * _Strength * mask * soft * distanceFade * i.color.a;
                half3 col = SampleSceneColor(suv + offset);
                return half4(col, saturate(mask * soft * i.color.a * 4.0));
            }
            ENDHLSL
        }
    }
    FallBack Off
}
