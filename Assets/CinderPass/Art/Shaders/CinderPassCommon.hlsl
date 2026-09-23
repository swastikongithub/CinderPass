#ifndef CINDERPASS_COMMON_INCLUDED
#define CINDERPASS_COMMON_INCLUDED

// Global wind published by FoliageWind.cs
// x = strength, y = speed, z = 1 / gust scale (m), w = gust strength
float4 _CP_Wind;
float4 _CP_WindDirection;

float CP_Hash11(float p)
{
    p = frac(p * 0.1031);
    p *= p + 33.33;
    p *= p + p;
    return frac(p);
}

float CP_Hash21(float2 p)
{
    float3 p3 = frac(float3(p.xyx) * 0.1031);
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.x + p3.y) * p3.z);
}

float3 CP_ObjectPositionWS()
{
    return float3(UNITY_MATRIX_M._m03, UNITY_MATRIX_M._m13, UNITY_MATRIX_M._m23);
}

// Displaces a foliage vertex (world space).
// sway   : 0 at the ground/trunk base, 1 at the top (bends the whole plant)
// flutter: 0 at a branch's attachment, 1 at its tip (fast leaf/needle motion)
// phase  : per-branch random 0..1
float3 CP_ApplyWind(float3 positionWS, float3 normalWS, float sway, float flutter, float phase, float windWeight)
{
    float strength = _CP_Wind.x * windWeight;
    if (strength <= 0.0001) return positionWS;
    float speed = _CP_Wind.y;
    float2 dir = _CP_WindDirection.xz;
    float3 origin = CP_ObjectPositionWS();
    float t = _Time.y * speed;

    // Travelling gust waves across the landscape.
    float gustWave = sin(dot(origin.xz, dir) * _CP_Wind.z * 6.2831 - t * 1.3) * 0.5 + 0.5;
    float gust = lerp(1.0 - _CP_Wind.w, 1.0, gustWave * gustWave);
    float objPhase = CP_Hash21(origin.xz) * 6.2831;

    float bend = strength * gust * (0.65 + 0.35 * sin(t * 1.7 + objPhase));
    float3 offset = float3(dir.x, 0, dir.y) * bend * sway * sway;
    offset.y -= bend * sway * sway * 0.15; // bending lowers the tip slightly

    float flick = sin(t * 7.3 + phase * 6.2831 + positionWS.x * 0.8 + positionWS.z * 0.6);
    offset += normalWS * flick * 0.045 * flutter * strength * gust;
    offset.y += flick * 0.02 * flutter * strength;
    return positionWS + offset;
}

#endif
