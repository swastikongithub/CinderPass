// Cinder Pass - soft camera-facing halo around beacon glass (additive, depth-softened, distance-scaled).
// The quad is billboarded in the vertex shader around the object's pivot, so the prefab stays static.
Shader "CinderPass/BeaconHalo"
{
    Properties
    {
        _GlowColor ("Glow Colour", Color) = (0.42, 0.95, 0.86, 1)
        _CoreTint ("Core Tint", Color) = (0.9, 1, 0.97, 1)
        _GlowIntensity ("Glow Intensity", Float) = 5.5
        _PulseAmount ("Pulse Amount", Range(0, 1)) = 0.35
        _PulseSpeed ("Pulse Speed (Hz)", Float) = 0.55
        _FresnelPower ("(unused) Fresnel Power", Float) = 2.2
        _HaloStrength ("Halo Strength", Float) = 0.12
        _HaloSize ("Halo Size (m)", Float) = 1.6
        _SoftDepth ("Soft Depth (m)", Float) = 0.6
        _NearFade ("Near Fade (m)", Float) = 4
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Transparent" "Queue" = "Transparent" "IgnoreProjector" = "True" }
        Pass
        {
            Name "Halo"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "CinderAtmosphere.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _GlowColor, _CoreTint;
                half _GlowIntensity, _PulseAmount, _PulseSpeed, _FresnelPower, _HaloStrength;
                float _HaloSize, _SoftDepth, _NearFade;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half intensity : TEXCOORD1;
                float eyeDepth : TEXCOORD2;
                float3 positionWS : TEXCOORD3;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float3 origin = float3(UNITY_MATRIX_M._m03, UNITY_MATRIX_M._m13, UNITY_MATRIX_M._m23);
                float3 right = UNITY_MATRIX_V[0].xyz;
                float3 up = UNITY_MATRIX_V[1].xyz;
                float2 corner = (v.uv - 0.5) * 2.0;
                float3 positionWS = origin + (right * corner.x + up * corner.y) * _HaloSize;
                o.positionCS = TransformWorldToHClip(positionWS);
                o.uv = corner;
                float dist = distance(GetCameraPositionWS(), origin);
                float h = sin(origin.x * 0.1291 + origin.z * 0.7823) * 437.585;
                half pulse = 1.0 - _PulseAmount * (0.5 + 0.5 * sin(_Time.y * _PulseSpeed * 6.2831853 + frac(h) * 6.2831853));
                o.intensity = pulse * saturate((dist - 1.0) / _NearFade);
                o.eyeDepth = -TransformWorldToView(positionWS).z;
                o.positionWS = positionWS;
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                half r = length(i.uv);
                half falloff = saturate(1.0 - r);
                falloff = falloff * falloff * (0.35 + 0.65 * falloff);
                float2 suv = GetNormalizedScreenSpaceUV(i.positionCS);
                float scene = LinearEyeDepth(SampleSceneDepth(suv), _ZBufferParams);
                half soft = saturate((scene - i.eyeDepth) / _SoftDepth);
                half3 c = lerp(_GlowColor.rgb, _CoreTint.rgb, falloff * falloff) * _GlowIntensity * _HaloStrength * falloff * soft * i.intensity;
                c *= CP_AtmosTransmittanceWS(i.positionWS);
                return half4(c, 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
