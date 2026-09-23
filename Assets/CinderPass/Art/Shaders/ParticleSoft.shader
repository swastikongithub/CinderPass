// Cinder Pass - lightweight particle shader for smoke, ash, steam, embers and dust.
// Soft (depth-faded) particles with vertex colour, optional cheap lighting for volumes (wrap-lit by the
// sun plus ambient) and an HDR emission multiplier for glowing particles. Blend mode set per material.
Shader "CinderPass/ParticleSoft"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)
        _Emission ("Emission Multiplier", Float) = 1
        _Lighting ("Lighting Amount (0 = unlit)", Range(0, 1)) = 0
        _SoftDepth ("Soft Depth (m)", Float) = 1.5
        _CameraFade ("Camera Near Fade (m)", Float) = 2
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src Blend", Float) = 5
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst Blend", Float) = 10
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Transparent" "Queue" = "Transparent" "IgnoreProjector" = "True" "PreviewType" = "Plane" }
        Pass
        {
            Name "Particle"
            Tags { "LightMode" = "UniversalForward" }
            Blend [_SrcBlend] [_DstBlend]
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "CinderAtmosphere.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _Color;
                half _Emission, _Lighting;
                float _SoftDepth, _CameraFade, _SrcBlend, _DstBlend;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; half4 color : COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
                float eyeDepth : TEXCOORD1;
                half3 light : TEXCOORD2;
                float3 positionWS : TEXCOORD3;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float3 positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(positionWS);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.color = v.color * _Color;
                o.eyeDepth = -TransformWorldToView(positionWS).z;
                // Volumetric-ish lighting: wrap the sun around the puff, fill with ambient SH.
                Light sun = GetMainLight();
                half3 up = half3(0, 1, 0);
                half wrap = saturate(dot(up, sun.direction) * 0.5 + 0.6);
                half3 lit = sun.color * wrap * 0.8 + SampleSH(up) * 1.1;
                o.light = lerp(half3(1, 1, 1), lit, _Lighting);
                o.positionWS = positionWS;
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                half4 c = tex * i.color;
                c.rgb *= i.light * _Emission;
                float2 suv = GetNormalizedScreenSpaceUV(i.positionCS);
                float scene = LinearEyeDepth(SampleSceneDepth(suv), _ZBufferParams);
                half soft = saturate((scene - i.eyeDepth) / _SoftDepth);
                half nearFade = saturate((i.eyeDepth - 0.3) / _CameraFade);
                c.a *= soft * nearFade;
                // Additive materials (dst = One) fade by colour; alpha-blended ones by alpha.
                if (_DstBlend == 1) c.rgb *= c.a * CP_AtmosTransmittanceWS(i.positionWS);
                else c.rgb = CP_ApplyAtmosphereWS(c.rgb, i.positionWS);
                return c;
            }
            ENDHLSL
        }
    }
    FallBack Off
}
