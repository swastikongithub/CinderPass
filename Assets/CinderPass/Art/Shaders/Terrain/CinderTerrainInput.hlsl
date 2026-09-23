#ifndef CINDER_TERRAIN_INPUT_INCLUDED
#define CINDER_TERRAIN_INPUT_INCLUDED

// Inputs for the Cinder Pass terrain shader. The terrain engine supplies the first control map (_Control),
// the heightmap/normalmap and patch data; TerrainTextureArrays supplies the layer texture arrays, per-layer
// parameters and the second control map (_Control1) so all eight layers are shaded in a single pass.

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

#define CP_TERRAIN_LAYERS 8

CBUFFER_START(UnityPerMaterial)
    float4 _MainTex_ST;          // unused; keeps the terrain engine's base-map path well defined
    half _HeightTransition;      // height-based blend softness (0 = hard, 1 = plain splat blend)
    half _MacroStrength;         // large-scale albedo variation
    float _MacroScale;           // metres per macro-noise tile
    half _FarBlend;              // how much of the enlarged far sample is mixed in at distance
    float _FarStart;             // metres where the far blend begins
    float _FarRange;             // metres over which it reaches full strength
    half _TriplanarSharpness;    // slope (|n| component) below which a projection axis contributes
CBUFFER_END

CBUFFER_START(_Terrain)
    float4 _Control_ST;
    float4 _Control_TexelSize;
#ifdef UNITY_INSTANCING_ENABLED
    float4 _TerrainHeightmapRecipSize;   // (1/width, 1/height, 1/(width-1), 1/(height-1))
#endif
    float4 _TerrainHeightmapScale;
#ifdef SCENESELECTIONPASS
    int _ObjectId;
    int _PassValue;
#endif
CBUFFER_END

TEXTURE2D(_Control);    SAMPLER(sampler_Control);
TEXTURE2D(_Control1);

// Global, set by TerrainTextureArrays.
TEXTURE2D_ARRAY(_CP_TerrainAlbedo); SAMPLER(sampler_CP_TerrainAlbedo);
TEXTURE2D_ARRAY(_CP_TerrainNormal);
TEXTURE2D_ARRAY(_CP_TerrainMask);
TEXTURE2D(_CP_TerrainMacro);        SAMPLER(sampler_CP_TerrainMacro);
float4 _CP_TerrainLayerST[CP_TERRAIN_LAYERS];     // x: 1/tile size (m), y: normal scale, z: smoothness scale, w: height bias
float4 _CP_TerrainLayerTint[CP_TERRAIN_LAYERS];   // rgb: albedo tint

#ifdef UNITY_INSTANCING_ENABLED
    TEXTURE2D(_TerrainHeightmapTexture);
    TEXTURE2D(_TerrainNormalmapTexture);
    SAMPLER(sampler_TerrainNormalmapTexture);
#endif

UNITY_INSTANCING_BUFFER_START(Terrain)
    UNITY_DEFINE_INSTANCED_PROP(float4, _TerrainPatchInstanceData)  // (xBase, yBase, skipScale, ~)
UNITY_INSTANCING_BUFFER_END(Terrain)

void TerrainInstancing(inout float4 positionOS, inout float3 normal, inout float2 uv)
{
#ifdef UNITY_INSTANCING_ENABLED
    float2 patchVertex = positionOS.xy;
    float4 instanceData = UNITY_ACCESS_INSTANCED_PROP(Terrain, _TerrainPatchInstanceData);
    float2 sampleCoords = (patchVertex.xy + instanceData.xy) * instanceData.z;
    float height = UnpackHeightmap(_TerrainHeightmapTexture.Load(int3(sampleCoords, 0)));
    positionOS.xz = sampleCoords * _TerrainHeightmapScale.xz;
    positionOS.y = height * _TerrainHeightmapScale.y;
    normal = float3(0, 1, 0); // per-pixel normal from _TerrainNormalmapTexture
    uv = sampleCoords * _TerrainHeightmapRecipSize.zw;
#endif
}

/// Geometric terrain normal: per pixel from the terrain normal map when instanced, else interpolated.
half3 TerrainGeometricNormalWS(float2 uv, half3 interpolatedNormalWS)
{
#ifdef UNITY_INSTANCING_ENABLED
    float2 sampleCoords = (uv / _TerrainHeightmapRecipSize.zw + 0.5f) * _TerrainHeightmapRecipSize.xy;
    half3 n = SAMPLE_TEXTURE2D(_TerrainNormalmapTexture, sampler_TerrainNormalmapTexture, sampleCoords).rgb * 2 - 1;
    return normalize(TransformObjectToWorldNormal(normalize(n)));
#else
    return normalize(interpolatedNormalWS);
#endif
}

#endif
