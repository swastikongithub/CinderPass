#ifndef CINDER_TERRAIN_PASSES_INCLUDED
#define CINDER_TERRAIN_PASSES_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DBuffer.hlsl"

struct Attributes
{
    float4 positionOS : POSITION;
    float3 normalOS   : NORMAL;
    float2 texcoord   : TEXCOORD0;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct Varyings
{
    float4 positionCS : SV_POSITION;
    float2 uv         : TEXCOORD0;   // terrain 0..1
    float3 positionWS : TEXCOORD1;
    half3  normalWS   : TEXCOORD2;
    half   fogFactor  : TEXCOORD3;
    UNITY_VERTEX_OUTPUT_STEREO
};

Varyings TerrainVert(Attributes v)
{
    Varyings o = (Varyings)0;
    UNITY_SETUP_INSTANCE_ID(v);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
    TerrainInstancing(v.positionOS, v.normalOS, v.texcoord);
    VertexPositionInputs pos = GetVertexPositionInputs(v.positionOS.xyz);
    o.positionCS = pos.positionCS;
    o.positionWS = pos.positionWS;
    o.uv = v.texcoord;
    o.normalWS = TransformObjectToWorldNormal(v.normalOS);
    o.fogFactor = ComputeFogFactor(pos.positionCS.z);
    return o;
}

// ---------------------------------------------------------------------------------------------------------
// Layer sampling: world-space projections (triplanar on slopes), distance anti-tiling, whiteout normals.

static const float2 CP_Rot = float2(0.956, 0.292); // cos/sin of ~17 degrees

float2 CP_Rotate(float2 v) { return float2(v.x * CP_Rot.x - v.y * CP_Rot.y, v.x * CP_Rot.y + v.y * CP_Rot.x); }

struct CPLayer
{
    half4 albedo;
    half3 normalWS;
    half4 mask;     // r metallic, g occlusion, b height, a smoothness
};

void CP_SamplePlanar(float layer, float2 uv, float2 dx, float2 dy, half normalScale, out half4 a, out half3 tn, out half4 m)
{
    a  = SAMPLE_TEXTURE2D_ARRAY_GRAD(_CP_TerrainAlbedo, sampler_CP_TerrainAlbedo, uv, layer, dx, dy);
    tn = UnpackNormalScale(SAMPLE_TEXTURE2D_ARRAY_GRAD(_CP_TerrainNormal, sampler_CP_TerrainAlbedo, uv, layer, dx, dy), normalScale);
    m  = SAMPLE_TEXTURE2D_ARRAY_GRAD(_CP_TerrainMask, sampler_CP_TerrainAlbedo, uv, layer, dx, dy);
}

// One planar projection. At range a rotated copy at ~3.4x the scale is mixed in, which removes the visible
// grid of the near tile without softening the ground under the car.
void CP_SampleProjection(float layer, float2 uv, float2 dx, float2 dy, half normalScale, half farK, out half4 a, out half3 tn, out half4 m)
{
    CP_SamplePlanar(layer, uv, dx, dy, normalScale, a, tn, m);
    UNITY_BRANCH
    if (farK > 0.01)
    {
        const float s = 0.29;
        half4 a2; half3 tn2; half4 m2;
        CP_SamplePlanar(layer, CP_Rotate(uv) * s + 0.37, CP_Rotate(dx) * s, CP_Rotate(dy) * s, normalScale, a2, tn2, m2);
        // The far copy is rotated in UV space, so rotate its tangent-space normal back (transpose rotation).
        tn2.xy = half2(tn2.x * CP_Rot.x + tn2.y * CP_Rot.y, -tn2.x * CP_Rot.y + tn2.y * CP_Rot.x);
        a = lerp(a, a2, farK);
        tn = normalize(lerp(tn, tn2, farK));
        m = lerp(m, m2, farK);
    }
}

CPLayer CP_SampleLayer(uint layer, float3 positionWS, float3 dPdx, float3 dPdy, half3 N, half3 tw, half farK)
{
    float4 st = _CP_TerrainLayerST[layer];
    float k = st.x;
    half normalScale = st.y;
    half4 a; half3 tn; half4 m;

    // Top projection (the terrain's native mapping, in world space so neighbouring tiles line up).
    CP_SampleProjection(layer, positionWS.xz * k, dPdx.xz * k, dPdy.xz * k, normalScale, farK, a, tn, m);
    CPLayer o;
    o.albedo = a * tw.y;
    o.mask = m * tw.y;
    half3 n = half3(tn.x + N.x, abs(tn.z) * N.y, tn.y + N.z) * tw.y;

    // Side projections only where the slope needs them (cliffs, canyon walls, the volcano cone).
    UNITY_BRANCH
    if (tw.x > 0.001)
    {
        half sx = N.x < 0 ? -1.0h : 1.0h;
        float2 uv = positionWS.zy * k;
        uv.x *= sx;
        CP_SampleProjection(layer, uv, float2(dPdx.z * sx, dPdx.y) * k, float2(dPdy.z * sx, dPdy.y) * k, normalScale, farK, a, tn, m);
        tn.x *= sx;
        half3 t = half3(tn.xy + N.zy, abs(tn.z) * N.x);
        o.albedo += a * tw.x;
        o.mask += m * tw.x;
        n += t.zyx * tw.x;
    }
    UNITY_BRANCH
    if (tw.z > 0.001)
    {
        half sz = N.z < 0 ? 1.0h : -1.0h;
        float2 uv = positionWS.xy * k;
        uv.x *= sz;
        CP_SampleProjection(layer, uv, float2(dPdx.x * sz, dPdx.y) * k, float2(dPdy.x * sz, dPdy.y) * k, normalScale, farK, a, tn, m);
        tn.x *= sz;
        half3 t = half3(tn.xy + N.xy, abs(tn.z) * N.z);
        o.albedo += a * tw.z;
        o.mask += m * tw.z;
        n += t * tw.z;
    }
    o.normalWS = normalize(n);
    return o;
}

// Cubic B-spline filtering from four bilinear taps. Splat maps are ~1 texel per metre; bilinear weights have
// creases along the texel grid that height blending would sharpen into visible steps.
half4 CP_SampleBicubic(TEXTURE2D_PARAM(tex, smp), float2 uv, float4 texelSize)
{
    float2 st = uv * texelSize.zw - 0.5;
    float2 i = floor(st);
    float2 f = st - i;
    float2 f2 = f * f, f3 = f2 * f;
    float2 w0 = (-f3 + 3.0 * f2 - 3.0 * f + 1.0) / 6.0;
    float2 w1 = (3.0 * f3 - 6.0 * f2 + 4.0) / 6.0;
    float2 w2 = (-3.0 * f3 + 3.0 * f2 + 3.0 * f + 1.0) / 6.0;
    float2 w3 = f3 / 6.0;
    float2 g0 = w0 + w1, g1 = w2 + w3;
    float2 p0 = (i - 0.5 + w1 / g0) * texelSize.xy;
    float2 p1 = (i + 1.5 + w3 / g1) * texelSize.xy;
    return g0.y * (g0.x * SAMPLE_TEXTURE2D(tex, smp, float2(p0.x, p0.y)) + g1.x * SAMPLE_TEXTURE2D(tex, smp, float2(p1.x, p0.y)))
         + g1.y * (g0.x * SAMPLE_TEXTURE2D(tex, smp, float2(p0.x, p1.y)) + g1.x * SAMPLE_TEXTURE2D(tex, smp, float2(p1.x, p1.y)));
}

void CP_TerrainSurface(Varyings IN, out half3 albedo, out half3 normalWS, out half metallic, out half smoothness, out half occlusion)
{
    half3 N = TerrainGeometricNormalWS(IN.uv, IN.normalWS);

    // Splat lookup: a small noise warp (about a texel) turns layer boundaries into organic edges.
    float2 warp = float2(SAMPLE_TEXTURE2D(_CP_TerrainMacro, sampler_CP_TerrainMacro, IN.positionWS.xz * 0.11).r,
                         SAMPLE_TEXTURE2D(_CP_TerrainMacro, sampler_CP_TerrainMacro, IN.positionWS.xz * 0.11 + 0.5).r) - 0.5;
    float2 splatUV = (IN.uv * (_Control_TexelSize.zw - 1.0f) + 0.5f) * _Control_TexelSize.xy + warp * _Control_TexelSize.xy * 2.2;
    half4 c0 = CP_SampleBicubic(TEXTURE2D_ARGS(_Control, sampler_Control), splatUV, _Control_TexelSize);
    half4 c1 = CP_SampleBicubic(TEXTURE2D_ARGS(_Control1, sampler_Control), splatUV, _Control_TexelSize);
    half w[CP_TERRAIN_LAYERS] = { c0.r, c0.g, c0.b, c0.a, c1.r, c1.g, c1.b, c1.a };

    // Projection weights: flat ground uses only the top projection; slopes blend in the sides.
    half3 tw = max(abs(N) - _TriplanarSharpness, 0.0h);
    tw *= tw;
    tw /= (tw.x + tw.y + tw.z);

    float3 dPdx = ddx(IN.positionWS);
    float3 dPdy = ddy(IN.positionWS);
    float dist = distance(IN.positionWS, GetCameraPositionWS());
    half farK = saturate((dist - _FarStart) / max(_FarRange, 1.0)) * _FarBlend;

    CPLayer L[CP_TERRAIN_LAYERS];
    half h[CP_TERRAIN_LAYERS];
    half hMax = 0;
    UNITY_UNROLL
    for (uint i = 0; i < CP_TERRAIN_LAYERS; i++)
    {
        L[i] = (CPLayer)0;
        h[i] = 0;
        UNITY_BRANCH
        if (w[i] > 0.004h)
        {
            L[i] = CP_SampleLayer(i, IN.positionWS, dPdx, dPdy, N, tw, farK);
            h[i] = saturate(L[i].mask.b + _CP_TerrainLayerST[i].w) * w[i];
        }
        hMax = max(hMax, h[i]);
    }

    // Height-based blend: where layers meet, the taller detail (stones, rock crests) wins over the lower
    // (dust, grass) instead of cross-fading.
    half4 a = 0;
    half3 n = 0;
    half3 msk = 0;  // metallic, occlusion, smoothness
    half total = 0;
    half transition = max(_HeightTransition, 0.001h);
    UNITY_UNROLL
    for (uint j = 0; j < CP_TERRAIN_LAYERS; j++)
    {
        half bw = w[j] > 0.004h ? (max(h[j] + transition - hMax, 0.0h) + 1e-4h) * w[j] : 0.0h;
        a += L[j].albedo * half4(_CP_TerrainLayerTint[j].rgb, 1.0h) * bw;
        n += L[j].normalWS * bw;
        msk += half3(L[j].mask.r, L[j].mask.g, L[j].mask.a * _CP_TerrainLayerST[j].z) * bw;
        total += bw;
    }
    total = max(total, 1e-4h);
    albedo = a.rgb / total;
    normalWS = normalize(n + N * 1e-3h);
    metallic = msk.x / total;
    occlusion = msk.y / total;
    smoothness = msk.z / total;

    // Macro variation: two octaves of world-space noise modulate brightness and temperature so large
    // areas of one layer never read as a repeated pattern from the overlooks.
    half m1 = SAMPLE_TEXTURE2D(_CP_TerrainMacro, sampler_CP_TerrainMacro, IN.positionWS.xz / _MacroScale).r;
    half m2 = SAMPLE_TEXTURE2D(_CP_TerrainMacro, sampler_CP_TerrainMacro, IN.positionWS.xz / (_MacroScale * 0.27) + 0.43).r;
    half macro = smoothstep(0.2h, 0.8h, m1 * 0.65h + m2 * 0.35h);
    albedo *= lerp(1.0h - _MacroStrength, 1.0h + _MacroStrength * 0.5h, macro);
    albedo *= lerp(half3(1.0h, 1.0h, 1.0h), lerp(half3(1.04h, 1.0h, 0.94h), half3(0.96h, 0.99h, 1.05h), smoothstep(0.3h, 0.7h, m2)), _MacroStrength);
}

void TerrainFrag(
    Varyings IN
    , out half4 outColor : SV_Target0
#ifdef _WRITE_RENDERING_LAYERS
    , out uint outRenderingLayers : SV_Target1
#endif
    )
{
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);
    half3 albedo, normalWS;
    half metallic, smoothness, occlusion;
    CP_TerrainSurface(IN, albedo, normalWS, metallic, smoothness, occlusion);

    InputData inputData = (InputData)0;
    inputData.positionWS = IN.positionWS;
    inputData.positionCS = IN.positionCS;
    inputData.normalWS = normalWS;
    inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(IN.positionWS);
    inputData.shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
    inputData.fogCoord = InitializeInputDataFog(float4(IN.positionWS, 1.0), IN.fogFactor);
    inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(IN.positionCS);
    inputData.shadowMask = half4(1, 1, 1, 1);

#if defined(_DBUFFER)
    half3 specular = 0;
    ApplyDecal(IN.positionCS, albedo, specular, inputData.normalWS, metallic, occlusion, smoothness);
#endif
    inputData.bakedGI = SampleSH(inputData.normalWS);

    half4 color = UniversalFragmentPBR(inputData, albedo, metallic, half3(0, 0, 0), smoothness, occlusion, half3(0, 0, 0), 1.0h);
    color.rgb = MixFog(color.rgb, inputData.fogCoord);
    outColor = half4(color.rgb, 1.0h);
#ifdef _WRITE_RENDERING_LAYERS
    outRenderingLayers = EncodeMeshRenderingLayer();
#endif
}

// ---------------------------------------------------------------------------------------------------------
// Shadow, depth and depth-normals passes (geometry only).

float3 _LightDirection;
float3 _LightPosition;

struct AttributesLean
{
    float4 positionOS : POSITION;
    float3 normalOS   : NORMAL;
    float2 texcoord   : TEXCOORD0;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct VaryingsLean
{
    float4 positionCS : SV_POSITION;
    float2 uv         : TEXCOORD0;
    half3  normalWS   : TEXCOORD1;
    UNITY_VERTEX_OUTPUT_STEREO
};

VaryingsLean ShadowPassVertex(AttributesLean v)
{
    VaryingsLean o = (VaryingsLean)0;
    UNITY_SETUP_INSTANCE_ID(v);
    TerrainInstancing(v.positionOS, v.normalOS, v.texcoord);
    float3 positionWS = TransformObjectToWorld(v.positionOS.xyz);
    float3 normalWS = TransformObjectToWorldNormal(v.normalOS);
#if _CASTING_PUNCTUAL_LIGHT_SHADOW
    float3 lightDirectionWS = normalize(_LightPosition - positionWS);
#else
    float3 lightDirectionWS = _LightDirection;
#endif
    float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
#if UNITY_REVERSED_Z
    positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
#else
    positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
#endif
    o.positionCS = positionCS;
    return o;
}

half4 ShadowPassFragment(VaryingsLean IN) : SV_TARGET { return 0; }

VaryingsLean DepthVertex(AttributesLean v)
{
    VaryingsLean o = (VaryingsLean)0;
    UNITY_SETUP_INSTANCE_ID(v);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
    TerrainInstancing(v.positionOS, v.normalOS, v.texcoord);
    o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
    o.uv = v.texcoord;
    o.normalWS = TransformObjectToWorldNormal(v.normalOS);
    return o;
}

half4 DepthOnlyFragment(VaryingsLean IN) : SV_TARGET
{
#ifdef SCENESELECTIONPASS
    return half4(_ObjectId, _PassValue, 1.0, 1.0);
#endif
    return IN.positionCS.z;
}

void DepthNormalsFragment(
    VaryingsLean IN
    , out half4 outNormalWS : SV_Target0
#ifdef _WRITE_RENDERING_LAYERS
    , out uint outRenderingLayers : SV_Target1
#endif
    )
{
    outNormalWS = half4(NormalizeNormalPerPixel(TerrainGeometricNormalWS(IN.uv, IN.normalWS)), 0.0);
#ifdef _WRITE_RENDERING_LAYERS
    outRenderingLayers = EncodeMeshRenderingLayer();
#endif
}

#endif
