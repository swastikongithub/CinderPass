// Cinder Pass - route beacon glass.
// One shared material for every beacon. The pulse phase is derived from each instance's world position
// inside the shader, so beacons breathe out of sync without any per-instance scripts or material copies.
// Values are driven centrally by the BeaconStyle asset.
Shader "CinderPass/BeaconGlow"
{
    Properties
    {
        _GlowColor ("Glow Colour", Color) = (0.42, 0.95, 0.86, 1)
        _CoreTint ("Core Tint", Color) = (0.9, 1, 0.97, 1)
        _GlowIntensity ("Glow Intensity", Float) = 5.5
        _PulseAmount ("Pulse Amount", Range(0, 1)) = 0.35
        _PulseSpeed ("Pulse Speed (Hz)", Float) = 0.55
        _FresnelPower ("Fresnel Power", Float) = 2.2
        _GlassBase ("Glass Base Colour", Color) = (0.05, 0.08, 0.08, 1)
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _GlowColor, _CoreTint, _GlassBase;
            half _GlowIntensity, _PulseAmount, _PulseSpeed, _FresnelPower;
        CBUFFER_END

        // Must match BeaconStyle.EvaluatePulse on the CPU.
        half BeaconPulse(float3 objectPos)
        {
            float h = sin(objectPos.x * 0.1291 + objectPos.z * 0.7823) * 437.585;
            float phase = frac(h) * 6.2831853;
            half wave = 0.5 + 0.5 * sin(_Time.y * _PulseSpeed * 6.2831853 + phase);
            return 1.0 - _PulseAmount * wave;
        }
        ENDHLSL

        Pass
        {
            Name "ForwardUnlit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; half4 color : COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                half pulse : TEXCOORD2;
                half fog : TEXCOORD3;
                half core : TEXCOORD4;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                float3 origin = float3(UNITY_MATRIX_M._m03, UNITY_MATRIX_M._m13, UNITY_MATRIX_M._m23);
                o.pulse = BeaconPulse(origin);
                o.core = v.color.r; // vertex colour marks the brighter inner filament
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                half3 n = normalize(i.normalWS);
                half3 v = GetWorldSpaceNormalizeViewDir(i.positionWS);
                half ndv = saturate(dot(n, v));
                half fresnel = pow(1.0 - ndv, _FresnelPower);
                // Light seems to come from inside the glass: brightest face-on through the core, rim-lit at edges.
                half inner = lerp(0.55, 1.0, ndv) * (0.7 + 0.3 * i.core);
                half3 glow = _GlowColor.rgb * _GlowIntensity * i.pulse * (inner + fresnel * 0.9);
                glow = lerp(glow, _CoreTint.rgb * _GlowIntensity * 1.4 * i.pulse, i.core * ndv * 0.6);
                half3 color = _GlassBase.rgb + glow;
                color = MixFog(color, i.fog);
                return half4(color, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            HLSLPROGRAM
            #pragma vertex V
            #pragma fragment F
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
            struct A { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct O { float4 positionCS : SV_POSITION; };
            O V(A a) { O o; UNITY_SETUP_INSTANCE_ID(a); o.positionCS = TransformObjectToHClip(a.positionOS.xyz); return o; }
            half F(O o) : SV_Target { return o.positionCS.z; }
            ENDHLSL
        }
    }
    FallBack Off
}
