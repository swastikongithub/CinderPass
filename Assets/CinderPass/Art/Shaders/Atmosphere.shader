// Cinder Pass - full-screen atmosphere. Applied after opaques and the sky, before transparents: reconstructs
// each pixel's world position from depth and blends in exponential height fog with sun scattering. Sky pixels
// are treated as a far distance so the horizon haze and the terrain fog meet seamlessly.
Shader "Hidden/CinderPass/Atmosphere"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" }
        ZWrite Off ZTest Always Cull Off Blend Off

        Pass
        {
            Name "Atmosphere"
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "CinderAtmosphere.hlsl"

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                half4 color = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
                float rawDepth = SampleSceneDepth(uv);
            #if UNITY_REVERSED_Z
                bool sky = rawDepth <= 1e-6;
                float farDepth = 1e-6;
            #else
                bool sky = rawDepth >= 1.0 - 1e-6;
                float farDepth = 1.0 - 1e-6;
            #endif
                float3 positionWS = ComputeWorldSpacePosition(uv, sky ? farDepth : rawDepth, UNITY_MATRIX_I_VP);
                float3 ray = positionWS - _WorldSpaceCameraPos;
                float dist = length(ray);
                float3 dir = ray / max(dist, 1e-4);
                if (sky) dist = _CP_AtmosParams1.w;
                color.rgb = CP_ApplyAtmosphere(color.rgb, dir, dist);
                return color;
            }
            ENDHLSL
        }
    }
}
