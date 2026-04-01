// SLZ_AtmosphereShared.hlsl

#ifndef SLZ_ATMOSPHERE_SHARED_INCLUDED
#define SLZ_ATMOSPHERE_SHARED_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

#define ATMOSPHERE_LOCAL_SUN_SAMPLE_COUNT 8


float4 _AtmosphereLocalSunTau[ATMOSPHERE_LOCAL_SUN_SAMPLE_COUNT];
float4 _AtmosphereLocalSunParams; // x=minAlt, y=invAltRange, z=sampleCountMinus1, w=cameraAlt
float4 _AtmosphereLocalOriginWS;
float4 _AtmosphereLocalUpWS;

static const float INV_4PI      = 0.07957747154594767;
static const float INV_16PI_3   = 0.05968310365946075;
static const float3 ATMO_LUMA   = float3(0.2126, 0.7152, 0.0722);

CBUFFER_START(AtmosphereParams)
float4 _AtmoPlanet;        // x=planetRadius, y=atmosphereRadius, z=cameraAltitude, w=thickness
float4 _AtmoScaleHeights;  // x=rayleighScaleHeight, y=mieScaleHeight, z=mieG, w=groundIntensity
float4 _AtmoBetaRayleigh;  // xyz=betaRayleigh
float4 _AtmoBetaMie;       // xyz=betaMie
float4 _AtmoSunDir;        // xyz=sunDir
float4 _AtmoSunColor;      // xyz=sunColor
float4 _AtmoGroundAlbedo;  // xyz=groundAlbedo
float4 _AtmoPlanetCenterWS; // xyz=planet center in world space
CBUFFER_END

#define _PlanetRadius        _AtmoPlanet.x
#define _AtmosphereRadius    _AtmoPlanet.y
#define _CameraAltitude      _AtmoPlanet.z
#define _Thickness           _AtmoPlanet.w

#define _RayleighScaleHeight _AtmoScaleHeights.x
#define _MieScaleHeight      _AtmoScaleHeights.y
#define _MieG                _AtmoScaleHeights.z
#define _GroundIntensity     _AtmoScaleHeights.w

#define _BetaRayleigh        _AtmoBetaRayleigh.xyz
#define _BetaMie             _AtmoBetaMie.xyz
#define _SunDir              _AtmoSunDir.xyz
#define _SunColor            _AtmoSunColor.xyz
#define _GroundAlbedo        _AtmoGroundAlbedo.xyz
#define _PlanetCenterWS      _AtmoPlanetCenterWS.xyz

float ThicknessMul()
{
    float thick01 = saturate(_Thickness / 5.0);
    return lerp(0.25, 2.5, thick01);
}   

void Densities(float height, out float dR, out float dM)
{
    float h = max(height, 0.0);
    dR = exp(-h / max(_RayleighScaleHeight, 1.0));
    dM = exp(-h / max(_MieScaleHeight, 1.0));
}

float3 SigmaS_R(float dR, float thickMul) { return _BetaRayleigh * dR * thickMul; }
float3 SigmaS_M(float dM, float thickMul) { return _BetaMie       * dM * thickMul; }
float3 SigmaS(float dR, float dM, float thickMul) { return SigmaS_R(dR, thickMul) + SigmaS_M(dM, thickMul); }
float3 SigmaT(float dR, float dM, float thickMul) { return SigmaS(dR, dM, thickMul); } // no extra absorption yet

float PhaseRayleigh(float mu)
{
    return INV_16PI_3 * (1.0 + mu * mu);
}

float PhaseHG(float mu, float g)
{
    g = clamp(g, -0.99, 0.99);
    float gg = g * g;
    float denom = max(1e-4, 1.0 + gg - 2.0 * g * mu);
    return INV_4PI * (1.0 - gg) / (denom * sqrt(denom));
}

struct AtmosphereSample
{
    float3 up;
    float   r;
    float   height;

    float   dR;
    float   dM;

    float3 sigmaS_R;
    float3 sigmaS_M;
    float3 sigmaS;
    float3 sigmaT;

    float sigmaSScalar;
    float sigmaTScalar;
};

AtmosphereSample SampleAtmosphereAtWS(float3 ws)
{
    AtmosphereSample s;
    float3 rel = ws - _PlanetCenterWS;
    s.r = length(rel);
    s.up = rel / max(s.r, 1e-5);
    s.height = s.r - _PlanetRadius;

    Densities(s.height, s.dR, s.dM);

    float thickMul = ThicknessMul();

    s.sigmaS_R = SigmaS_R(s.dR, thickMul);
    s.sigmaS_M = SigmaS_M(s.dM, thickMul);
    s.sigmaS   = s.sigmaS_R + s.sigmaS_M;
    s.sigmaT   = s.sigmaS;

    s.sigmaSScalar = dot(s.sigmaS, ATMO_LUMA);
    s.sigmaTScalar = dot(s.sigmaT, ATMO_LUMA);

    return s;
}

float GetLocalAtmosphereAltitudeMeters(float3 positionWS)
{
    return _AtmosphereLocalSunParams.w
         + dot(positionWS - _AtmosphereLocalOriginWS.xyz, _AtmosphereLocalUpWS.xyz);
}

float3 SampleLocalAtmosphereSunTau(float altitudeMeters)
{
    float u = saturate((altitudeMeters - _AtmosphereLocalSunParams.x) * _AtmosphereLocalSunParams.y);
    float x = u * _AtmosphereLocalSunParams.z;

    int i0 = min((int)x, ATMOSPHERE_LOCAL_SUN_SAMPLE_COUNT - 2);
    int i1 = i0 + 1;
    float f = frac(x);

    return lerp(_AtmosphereLocalSunTau[i0].rgb, _AtmosphereLocalSunTau[i1].rgb, f);
}

float3 SampleLocalAtmosphereSunTransmittance(float3 positionWS)
{
    float altitudeMeters = GetLocalAtmosphereAltitudeMeters(positionWS);
    float3 tau = SampleLocalAtmosphereSunTau(altitudeMeters);
    return exp(-tau);
}
#endif