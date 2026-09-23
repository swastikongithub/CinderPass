// Cinder Pass - animated lava surface.
// Layered, flow-mapped crust plates drift over a molten body. Cracks between plates glow, the hottest
// regions pulse between orange and yellow-white, and the lava cools and crusts over where it meets the
// shore (distance to the bank baked into vertex colour R, 0..4 m). Crust is lit as rock by the scene
// lighting; molten areas are emissive. Rendered in the opaque queue so it is part of the opaque colour and
// depth textures that heat haze, soft particles and SSAO read.
Shader "CinderPass/Lava"
{
    Properties
    {
        [NoScaleOffset] _CrustAlbedo ("Crust Albedo", 2D) = "gray" {}
        [NoScaleOffset][Normal] _CrustNormal ("Crust Normal", 2D) = "bump" {}
        [NoScaleOffset] _NoiseTex ("Noise (R fbm, G cells, B cell edges, A ridged)", 2D) = "gray" {}
        _CrustTiling ("Crust Tiling (m per repeat)", Float) = 7
        _NoiseTiling ("Noise Tiling (m per repeat)", Float) = 16

        [Toggle(_LAVA_UV_FLOW)] _UVFlow ("Flow Along Mesh UV (channels)", Float) = 0
        _FlowSpeed ("Flow Speed (m/s)", Float) = 0.25
        _FlowCycle ("Flow Cycle (s)", Float) = 9
        _Swirl ("Pool Swirl", Float) = 0.6
        _ChannelHalfWidth ("Channel Half Width (m)", Float) = 4

        _CrustColor ("Crust Tint", Color) = (0.22, 0.19, 0.18, 1)
        _CrustCoverage ("Crust Coverage", Range(0, 1)) = 0.55
        _CrustSoftness ("Crust Edge Softness", Range(0.01, 0.4)) = 0.09
        _CrustSmoothness ("Crust Smoothness", Range(0, 1)) = 0.18
        _NormalStrength ("Crust Normal Strength", Range(0, 2)) = 1

        [HDR] _MoltenColor ("Molten", Color) = (2.6, 0.55, 0.06, 1)
        [HDR] _CoreColor ("Core (hottest)", Color) = (5.2, 1.9, 0.35, 1)
        [HDR] _CrackColor ("Crack Glow", Color) = (2.4, 0.42, 0.04, 1)
        _CrackWidth ("Crack Width", Range(0.005, 0.25)) = 0.07
        _Activity ("Activity", Range(0, 2)) = 1
        _HeatScale ("Heat Variation Scale (m)", Float) = 60

        _ShoreDistance ("Shore Cooling Distance (m)", Float) = 1.6
        _ShoreGlow ("Shore Seam Glow", Float) = 1.6
        _WaveHeight ("Surface Undulation (m)", Float) = 0.05
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" "Queue" = "Geometry+20" "IgnoreProjector" = "True" }
        LOD 300

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        TEXTURE2D(_CrustAlbedo); SAMPLER(sampler_CrustAlbedo);
        TEXTURE2D(_CrustNormal); SAMPLER(sampler_CrustNormal);
        TEXTURE2D(_NoiseTex);    SAMPLER(sampler_NoiseTex);

        CBUFFER_START(UnityPerMaterial)
            float _CrustTiling, _NoiseTiling, _FlowSpeed, _FlowCycle, _Swirl, _ChannelHalfWidth;
            half4 _CrustColor;
            half _CrustCoverage, _CrustSoftness, _CrustSmoothness, _NormalStrength;
            half4 _MoltenColor, _CoreColor, _CrackColor;
            half _CrackWidth, _Activity;
            float _HeatScale, _ShoreDistance;
            half _ShoreGlow;
            float _WaveHeight;
            float _UVFlow;
        CBUFFER_END

        // Slow heave of the molten surface (shared by every pass so depth matches colour).
        float3 LavaDisplace(float3 positionWS)
        {
            float n = SAMPLE_TEXTURE2D_LOD(_NoiseTex, sampler_NoiseTex, positionWS.xz / (_NoiseTiling * 1.7) + _Time.y * 0.004, 0).r;
            positionWS.y += (n - 0.5) * 2.0 * _WaveHeight * (0.6 + 0.4 * sin(_Time.y * 0.8 + n * 6.2831));
            return positionWS;
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            ZWrite On
            Blend Off
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex LavaVertex
            #pragma fragment LavaFragment

            #pragma shader_feature_local _LAVA_UV_FLOW
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fragment _ _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 tangentOS  : TANGENT;
                float2 uv         : TEXCOORD0;
                half4  color      : COLOR;      // r: distance to the bank / 4 m
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3  normalWS   : TEXCOORD1;
                half4  tangentWS  : TEXCOORD2;
                float2 uv         : TEXCOORD3;
                half   fogFactor  : TEXCOORD4;
                half   shore      : TEXCOORD5;  // metres to the bank
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings LavaVertex(Attributes input)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                float3 positionWS = LavaDisplace(TransformObjectToWorld(input.positionOS.xyz));
                o.shore = input.color.r * 4.0;

                VertexNormalInputs nrm = GetVertexNormalInputs(input.normalOS, input.tangentOS);
                o.positionWS = positionWS;
                o.positionCS = TransformWorldToHClip(positionWS);
                o.normalWS = nrm.normalWS;
                o.tangentWS = half4(nrm.tangentWS, input.tangentOS.w * GetOddNegativeScale());
                o.uv = input.uv;
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            // Two-phase flow mapping: two samples offset along the flow, cross-faded so the texture
            // never visibly resets.
            void FlowPhases(float2 flowVector, out float2 offsetA, out float2 offsetB, out float wA, out float wB)
            {
                float t = _Time.y / max(_FlowCycle, 0.1);
                float pA = frac(t);
                float pB = frac(t + 0.5);
                wA = 1.0 - abs(1.0 - 2.0 * pA);
                wB = 1.0 - wA;
                float span = _FlowSpeed * _FlowCycle;
                offsetA = -flowVector * (pA * span);
                offsetB = -flowVector * (pB * span) + float2(0.37, 0.71) * _NoiseTiling;
            }

            half4 LavaFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float3 positionWS = input.positionWS;
                float time = _Time.y;

                // --- Flow field ---
                float2 baseUV;
                float2 flow;
                #if defined(_LAVA_UV_FLOW)
                    // Channels: uv.x = metres across (0 at centre), uv.y = metres downstream.
                    baseUV = input.uv;
                    // Shear: lava runs fastest mid-channel and drags against the banks.
                    float across = saturate(abs(input.uv.x) / max(_ChannelHalfWidth, 0.1));
                    flow = float2(0.0, 1.0 - across * across * 0.7);
                #else
                    // Pools: slow convective swirl around a drifting centre.
                    baseUV = positionWS.xz;
                    float2 low = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, positionWS.xz / (_NoiseTiling * 5.0) + time * 0.002).ra;
                    float angle = (low.x * 2.0 + low.y) * 6.2831 * _Swirl + time * 0.02;
                    flow = float2(cos(angle), sin(angle)) * 0.35;
                #endif

                float2 offA, offB;
                float wA, wB;
                FlowPhases(flow, offA, offB, wA, wB);

                float2 nuvA = (baseUV + offA) / _NoiseTiling;
                float2 nuvB = (baseUV + offB) / _NoiseTiling;
                half4 noise = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, nuvA) * wA
                            + SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, nuvB) * wB;
                half4 detail = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, nuvA * 3.1 + 0.19) * wA
                             + SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, nuvB * 3.1 + 0.53) * wB;

                // Large, slowly evolving heat field (keeps pools from looking uniform).
                half heat = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, positionWS.xz / _HeatScale + float2(time * 0.0031, -time * 0.0023)).r;
                heat = saturate((heat - 0.25) * 1.8) * _Activity;

                // --- Shoreline (baked distance to the bank) ---
                float2 screenUV = GetNormalizedScreenSpaceUV(input.positionCS);
                float bank = max(input.shore, 0.0);
                half shore = saturate(bank / max(_ShoreDistance, 0.01));
                half seam = 1.0 - saturate(bank / 0.3);

                // --- Crust plates ---
                half field = noise.r * 0.6 + noise.a * 0.25 + detail.r * 0.15;
                half coverage = saturate(_CrustCoverage + (1.0 - shore) * 0.4 - (heat - 0.5) * 0.35);
                half threshold = 1.0 - coverage;
                half crust = smoothstep(threshold - _CrustSoftness, threshold + _CrustSoftness, field);
                half molten = 1.0 - crust;

                // Glowing fractures between plates (cell borders), fading on thick cool crust.
                half crackLine = 1.0 - smoothstep(0.0, _CrackWidth, noise.b);
                half fineCrack = 1.0 - smoothstep(0.0, _CrackWidth * 0.6, detail.b);
                half thickness = saturate((field - threshold) * 4.0);
                half cracks = saturate(crackLine + fineCrack * 0.45) * crust * (1.0 - thickness * 0.75) * (0.35 + heat);

                // --- Surface (crust lit as rock) ---
                float2 cuvA = (baseUV + offA) / _CrustTiling;
                float2 cuvB = (baseUV + offB) / _CrustTiling;
                half3 crustAlbedo = (SAMPLE_TEXTURE2D(_CrustAlbedo, sampler_CrustAlbedo, cuvA).rgb * wA
                                  +  SAMPLE_TEXTURE2D(_CrustAlbedo, sampler_CrustAlbedo, cuvB).rgb * wB) * _CrustColor.rgb;
                half3 nA = UnpackNormal(SAMPLE_TEXTURE2D(_CrustNormal, sampler_CrustNormal, cuvA));
                half3 nB = UnpackNormal(SAMPLE_TEXTURE2D(_CrustNormal, sampler_CrustNormal, cuvB));
                half3 normalTS = normalize(lerp(half3(0, 0, 1), normalize(nA * wA + nB * wB), crust * _NormalStrength));

                half3 bitangent = input.tangentWS.w * cross(input.normalWS, input.tangentWS.xyz);
                half3 normalWS = normalize(TransformTangentToWorld(normalTS, half3x3(input.tangentWS.xyz, bitangent, input.normalWS)));

                // --- Emission ---
                half coreMask = saturate(pow(saturate(noise.g * 1.25 * (0.5 + heat)), 1.6) + detail.a * 0.25);
                half pulse = 0.85 + 0.15 * sin(time * 1.3 + noise.r * 12.0);
                half3 moltenGlow = lerp(_MoltenColor.rgb, _CoreColor.rgb, coreMask) * (0.55 + 0.75 * heat) * pulse;
                half edgeHeat = saturate(1.0 - abs(field - threshold) / (_CrustSoftness * 3.0)) * crust; // cooling rim of plates
                half3 emission = moltenGlow * molten
                               + _CrackColor.rgb * cracks
                               + _MoltenColor.rgb * edgeHeat * 0.35 * (0.4 + heat)
                               + _CrackColor.rgb * seam * _ShoreGlow;

                InputData inputData = (InputData)0;
                inputData.positionWS = positionWS;
                inputData.positionCS = input.positionCS;
                inputData.normalWS = normalWS;
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(positionWS);
                inputData.shadowCoord = TransformWorldToShadowCoord(positionWS);
                inputData.fogCoord = InitializeInputDataFog(float4(positionWS, 1.0), input.fogFactor);
                inputData.bakedGI = SampleSH(normalWS);
                inputData.normalizedScreenSpaceUV = screenUV;
                inputData.shadowMask = half4(1, 1, 1, 1);

                SurfaceData surface = (SurfaceData)0;
                surface.albedo = lerp(half3(0.05, 0.02, 0.015), crustAlbedo, crust);
                surface.metallic = 0;
                surface.specular = half3(0, 0, 0);
                surface.smoothness = lerp(0.62, _CrustSmoothness, crust);
                surface.normalTS = normalTS;
                surface.occlusion = 1;
                surface.emission = emission;
                surface.alpha = 1;

                half4 color = UniversalFragmentPBR(inputData, surface);
                color.rgb = MixFog(color.rgb, inputData.fogCoord);
                color.a = 1;
                return color;
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
            #pragma target 3.5
            #pragma vertex DepthVertex
            #pragma fragment DepthFragment
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
            struct A { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS : SV_POSITION; UNITY_VERTEX_OUTPUT_STEREO };
            V DepthVertex(A i)
            {
                V o = (V)0;
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformWorldToHClip(LavaDisplace(TransformObjectToWorld(i.positionOS.xyz)));
                return o;
            }
            half DepthFragment(V i) : SV_Target { return i.positionCS.z; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex DepthNormalsVertex
            #pragma fragment DepthNormalsFragment
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"
            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS : SV_POSITION; half3 normalWS : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };
            V DepthNormalsVertex(A i)
            {
                V o = (V)0;
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformWorldToHClip(LavaDisplace(TransformObjectToWorld(i.positionOS.xyz)));
                o.normalWS = TransformObjectToWorldNormal(i.normalOS);
                return o;
            }
            void DepthNormalsFragment(V i, out half4 outNormalWS : SV_Target0
            #ifdef _WRITE_RENDERING_LAYERS
                , out uint outRenderingLayers : SV_Target1
            #endif
            )
            {
                outNormalWS = half4(NormalizeNormalPerPixel(i.normalWS), 0.0);
            #ifdef _WRITE_RENDERING_LAYERS
                outRenderingLayers = EncodeMeshRenderingLayer();
            #endif
            }
            ENDHLSL
        }
    }
    FallBack Off
}
