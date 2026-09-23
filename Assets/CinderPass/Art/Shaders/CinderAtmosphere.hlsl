#ifndef CINDER_ATMOSPHERE_INCLUDED
#define CINDER_ATMOSPHERE_INCLUDED

// Exponential height fog with directional sun scattering. The same function is applied to opaque surfaces
// and the sky by the full-screen AtmosphereFeature pass and to transparent effects by their own shaders, so
// everything in the scene sits in one consistent atmosphere. Parameters are globals set by the feature from
// the AtmosphereSettings volume component (and zero - no fog - when it is inactive).

float4 _CP_AtmosParams0;   // x: density at base height (1/m), y: height falloff (1/m), z: base height (m), w: start distance (m)
float4 _CP_AtmosParams1;   // x: sun scatter exponent, y: sun scatter strength, z: max opacity, w: sky distance (m)
float4 _CP_AtmosFogColor;  // rgb: in-scattered ambient colour (HDR)
float4 _CP_AtmosSunColor;  // rgb: in-scattered sun colour (HDR)
float4 _CP_AtmosSunDir;    // xyz: direction towards the sun

// Optical depth of the height fog along a view ray, integrated analytically.
float CP_FogOpticalDepth(float3 origin, float3 dir, float dist)
{
    float density = _CP_AtmosParams0.x;
    float falloff = _CP_AtmosParams0.y;
    float start = min(dist, _CP_AtmosParams0.w);
    float len = dist - start;
    float startY = origin.y + dir.y * start;
    float k = falloff * dir.y * len;
    float integral = abs(k) > 1e-3 ? (1.0 - exp(-k)) / k : 1.0 - 0.5 * k;
    return density * exp(-falloff * (startY - _CP_AtmosParams0.z)) * len * integral;
}

half CP_AtmosFogAmount(float3 dir, float dist)
{
    float od = CP_FogOpticalDepth(_WorldSpaceCameraPos, dir, dist);
    return (half)min(1.0 - exp(-od), _CP_AtmosParams1.z);
}

half3 CP_AtmosInscatter(float3 dir)
{
    half sun = pow(saturate(dot(dir, _CP_AtmosSunDir.xyz)), _CP_AtmosParams1.x) * _CP_AtmosParams1.y;
    return _CP_AtmosFogColor.rgb + _CP_AtmosSunColor.rgb * sun;
}

half3 CP_ApplyAtmosphere(half3 color, float3 dir, float dist)
{
    return lerp(color, CP_AtmosInscatter(dir), CP_AtmosFogAmount(dir, dist));
}

// Convenience for transparent shaders.
half3 CP_ApplyAtmosphereWS(half3 color, float3 positionWS)
{
    float3 ray = positionWS - _WorldSpaceCameraPos;
    float dist = length(ray);
    return CP_ApplyAtmosphere(color, ray / max(dist, 1e-4), dist);
}

// For additive effects: only the light reaching the eye (no in-scatter added).
half CP_AtmosTransmittanceWS(float3 positionWS)
{
    float3 ray = positionWS - _WorldSpaceCameraPos;
    float dist = length(ray);
    return 1.0h - CP_AtmosFogAmount(ray / max(dist, 1e-4), dist);
}

#endif
