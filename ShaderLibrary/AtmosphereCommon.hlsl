// AtmosphereCommon.hlsl
// Units assume: world units = meters, scale heights in meters, beta in 1/m.

CBUFFER_START(AtmosphereParams)
float  _SeaLevel;              // WS y at sea level (or your baseline)
float  _RayleighScaleHeight;   // e.g. 8000.0
float  _MieScaleHeight;        // e.g. 1200.0
float3 _BetaRayleigh;          // 1/m (RGB)
float3 _BetaMie;               // 1/m (RGB)
float  _AtmosphereThickness;   // same control you use in sky
float  _AtmoExtinctionMul;     // extra global multiplier (artist knob)
CBUFFER_END

float ThickMul()
{
    float thickness01 = saturate(_AtmosphereThickness / 5.0);
    return lerp(0.25, 2.5, thickness01);
}

void GetDensities(float heightMeters, out float dR, out float dM)
{
    // Match your sky shader’s intent: exponential falloff with altitude.
    // If you allow underground volumes, remove the max().
    heightMeters = max(heightMeters, 0.0);

    float invHR = rcp(max(_RayleighScaleHeight, 1e-3));
    float invHM = rcp(max(_MieScaleHeight,      1e-3));

    dR = exp(-heightMeters * invHR);
    dM = exp(-heightMeters * invHM);
}

float3 SigmaT_RGB(float heightMeters)
{
    float dR, dM; GetDensities(heightMeters, dR, dM);
    float tm = ThickMul();
    return ((_BetaRayleigh * tm) * dR + (_BetaMie * tm) * dM) * _AtmoExtinctionMul; // 1/m, RGB
}

// Convert RGB extinction to your scalar alpha (grey extinction).
// Luma tends to “feel” most consistent.
float SigmaT_Scalar(float3 sigmaT_rgb)
{
    return dot(sigmaT_rgb, float3(0.2126, 0.7152, 0.0722)); // 1/m
}

// Cheap analytic approx for exp atmosphere transmittance toward sun (flat-earth approx).
// This won’t match your spherical RaySphere exactly, but it matches the same exponentials
// and usually tracks your sky very well near the ground.
float3 SunTransmittance_ExpAtmo(float heightMeters, float3 sunDirWS)
{
    float dR, dM; GetDensities(heightMeters, dR, dM);

    // sun elevation relative to +Y up
    float muUp = max(0.02, sunDirWS.y);

    float tm = ThickMul();

    // ∫ exp(-(h + s*muUp)/H) ds = exp(-h/H) * H / muUp
    float odR = dR * (_RayleighScaleHeight / muUp);
    float odM = dM * (_MieScaleHeight      / muUp);

    float3 tau = ((_BetaRayleigh * tm) * odR + (_BetaMie * tm) * odM) * _AtmoExtinctionMul;
    return exp(-tau);
}