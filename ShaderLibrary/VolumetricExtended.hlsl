#ifndef VOLUMETRIC_SG_INCLUDED
#define VOLUMETRIC_SG_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/VolumetricCore.hlsl"

TEXTURE3D(InLightingTexture);

///Use this is to sample the pre-integrated volumetric lighting
void volumetricLighting_float(half3 positionWS, out float4 outputVolumetric)
{
        half4 ls = half4(positionWS - _VolCameraPos, -1); //_WorldSpaceCameraPos
        ls = mul(ls, TransposedCameraProjectionMatrix);
        ls.xyz = ls.xyz / ls.w;
        float vdistance = distance(positionWS, _VolCameraPos);
        float W = EncodeLogarithmicDepthGeneralized(vdistance, _VBufferDistanceEncodingParams);
        // Convert linear UV -> froxel grid UV (inverse of the warp used during froxel rendering)
        float2 uvGrid = LinearUV_To_FroxelGridUV(ls.xy, _FoveaCenterUV, _FoveaStrength); 
        //Sampling pre-integrated volume.
 #if VOLUMETRICS_HQ_ENABLED
    outputVolumetric = SampleTricubicLevel(InLightingTexture, sampler_LinearClamp,float3 (uvGrid.xy,W) , 0);  
 #else
            outputVolumetric = SAMPLE_TEXTURE3D_LOD(InLightingTexture, sampler_LinearClamp,float3 (uvGrid.xy,W) , 0);
 #endif
 
}

void volumetrics_additiveBlend_float(in half4 color, in float3 positionWS, out half4 outColor) {

       // #if defined(_VOLUMETRICS_ENABLED)

        half4 FroxelColor = GetVolumetricColor(positionWS);
       outColor.rgb = color.rgb * FroxelColor.a;
       outColor.a = FroxelColor.a;
       // // outColor.rgb = FroxelColor.rgb + (color.rgb * FroxelColor.a);
      //  outColor = FroxelColor;
       // #endif
      //  outColor = color;
}

// float IGN_Temporal(float2 pixelXY, uint frameIndex)
// {
//  float n = InterleavedGradientNoise(pixelXY,frameIndex);
//  // Cranley-Patterson rotation
//  return frac(n + frameIndex * 0.61803398875);
// }

// Inputs you’ll want available:
//  - positionWS: surface world position
//  - normalWS: surface normal (world)
//  - pixelXY: pixel coordinate (SV_Position.xy or equivalent)
//  - frameIndex: frame counter (optional, for temporal decorrelation)

void volumetricLightingFrosted_float(
    half3 positionWS,
    half3 normalWS,
    float2 pixelXY,
    uint frameIndex,
    half _FrostThickness,
    half _FrostMaxThickness,
    half _FrostStartBias,
    half _FrostSigmaT, // If you don’t want this, set w = 1.
    out float4 outputVolumetric)
{
 float3 toCamera = normalize(_VolCameraPos - positionWS);   // surface -> camera
 float3 rayIntoScene = -toCamera;                           // camera ray direction past the surface

 // View-dependent thickness (frosted slab approximation)
 float NdV = abs(dot(normalWS, toCamera));
 float thickness = _FrostThickness / max(NdV, 0.15);        // avoid exploding at grazing angles
 thickness = min(thickness, _FrostMaxThickness);

 // Small bias so first tap isn't exactly on the surface
 float startBias = _FrostStartBias; // e.g. 0.001 to 0.01 meters depending on scale

 // Stochastic stratification
 float jitter = InterleavedGradientNoise(pixelXY, frameIndex);

 // Small loop is enough (2–6 taps usually)
 const int SAMPLE_COUNT = 4;

 float4 accum = 0;
 float weightSum = 0;

 [unroll]
 for (int i = 0; i < SAMPLE_COUNT; i++)
 {
  // Stratified [0,1) sample with jitter
  float u = (i + jitter) / SAMPLE_COUNT;

  // Distance along the short integration segment
  float s = startBias + u * thickness;

  float3 samplePosWS = positionWS + rayIntoScene * s;

  float4 vol;
  volumetricLighting_float(samplePosWS, vol);

  // Optional extinction weighting (helps feel more "material-like")
  // If you don’t want this, set w = 1.
  //half _FrostSigmaT = .001;
  float w = exp(-_FrostSigmaT * s);

  accum += vol * w;
  weightSum += w;
 }

 outputVolumetric = accum / max(weightSum, 1e-5);
}

float Hash11(float x)
{
    x = frac(x * 0.1031);
    x *= x + 33.33;
    x *= x + x;
    return frac(x);
}

float2 Hash22(float x)
{
    return float2(Hash11(x + 1.23), Hash11(x + 4.56));
}

// Concentric disk mapping (better than polar for uniform disk samples)
float2 SampleDiskConcentric(float2 u)
{
    float2 p = 2.0 * u - 1.0;

    if (p.x == 0 && p.y == 0)
        return 0;

    float r, theta;
    if (abs(p.x) > abs(p.y))
    {
        r = p.x;
        theta = (3.14159265 / 4.0) * (p.y / p.x);
    }
    else
    {
        r = p.y;
        theta = (3.14159265 / 2.0) - (3.14159265 / 4.0) * (p.x / p.y);
    }

    return r * float2(cos(theta), sin(theta));
}

// Build an orthonormal basis around direction D
void BuildOrthonormalBasis(float3 D, out float3 B1, out float3 B2)
{
    float sign_ = (D.z >= 0.0) ? 1.0 : -1.0;
    float a = -1.0 / (sign_ + D.z);
    float b = D.x * D.y * a;
    B1 = float3(1.0 + sign_ * D.x * D.x * a, sign_ * b, -sign_ * D.x);
    B2 = float3(b, sign_ + D.y * D.y * a, -D.y);
}
//
// void volumetricLightingFrosted_float(
//     half3 positionWS,
//     half3 normalWS,
//     float2 pixelXY,
//     uint frameIndex,
//     half _FrostThickness,
//     half _FrostMaxThickness,
//     half _FrostStartBias,
//     half _FrostSigmaT, // If you don’t want this, set w = 1.
//     out float4 outputVolumetric)
// {
//
//     //_FrostThickness = 0.02;
//
//     //_FrostMaxThickness = 0.08;
//
//     //_FrostStartBias = 0.002;
//
//     //_FrostSigmaT = 8.0;
//
//     half _FrostScatterRadiusNear = 0.0005;
//
//     half _FrostScatterRadiusFar = .50;
//
//     half _FrostScatterDepthPower = 10.5;
//     half _FrostScatterConeSlope = .05;
//     half _FrostShadowScatterBoost = .5;
//     half _FrostCenterSampleWeight = 1;
//
//     half _FrostOccLumLow = 0.002;
//
//     half _FrostOccLumHigh = 0.03;
//
//     half _FrostOccStrength = 20.0;// (this is scene-scale dependent)
//
//     half _FrostOccUseAlpha = 0.0;// (flip to 1 if alpha is meaningful)
//
//     half _FrostOccAlphaScale = 1.0;
//
//     
//     // Camera/view directions
//     float3 toCamera = normalize(_VolCameraPos - positionWS); // surface -> camera
//     float3 rayDir   = -toCamera;                             // into scene (cheap transmitted dir)
//
//     // View-dependent effective thickness (slab approximation)
//     float NdV = abs(dot(normalWS, toCamera));
//     float thickness = _FrostThickness / max(NdV, 0.15);
//     thickness = min(thickness, _FrostMaxThickness);
//
//     float startBias = _FrostStartBias;
//     float jitterDepth = InterleavedGradientNoise(pixelXY, frameIndex);
//
//     // Basis for disk offsets around the ray
//     float3 T, B;
//     BuildOrthonormalBasis(rayDir, T, B);
//
//     // Integration settings
//     const int DEPTH_SAMPLES = 4;     // 4 is a good start
//     const int RAY_SAMPLES   = 2;     // 1-2 if temporal, 3-4 if no temporal accumulation
//
//     float ds = thickness / DEPTH_SAMPLES;
//
//     float4 accum = 0.0;
//     float weightSum = 0.0;
//
//     // Heuristic transmittance from near-dark layers
//     float shadowT = 1.0;
//
//     [unroll]
//     for (int i = 0; i < DEPTH_SAMPLES; i++)
//     {
//         // Stratified depth sample with jitter
//         float uDepth = (i + jitterDepth) / DEPTH_SAMPLES; // [0,1)
//         float s = startBias + uDepth * thickness;
//
//         float3 centerPos = positionWS + rayDir * s;
//
//         // Distance-dependent diffusion radius:
//         // either world-space radius ramp, or angular cone radius ~ s * tan(theta)
//         float radiusByDepth = lerp(_FrostScatterRadiusNear, _FrostScatterRadiusFar,
//                                    pow(saturate(uDepth), _FrostScatterDepthPower));
//
//         float radiusByAngle = s * _FrostScatterConeSlope; // e.g. tan(theta) approx
//         float scatterRadius = radiusByDepth + radiusByAngle;
//
//         // Optional extra diffusion in shadowed regions
//         scatterRadius *= (1.0 + _FrostShadowScatterBoost * (1.0 - shadowT));
//
//         float4 layerAccum = 0.0;
//         float layerWeight = 0.0;
//
//         // Center sample (helps preserve structure)
//         {
//             float4 volCenter;
//             volumetricLighting_float(centerPos, volCenter);
//
//             float wCenter = _FrostCenterSampleWeight;
//             layerAccum += volCenter * wCenter;
//             layerWeight += wCenter;
//         }
//
//         // Off-axis scattered samples
//         [unroll]
//         for (int j = 0; j < RAY_SAMPLES; j++)
//         {
//             // Stable-ish random seed per pixel/depth/sample/frame
//             float seed = dot(pixelXY, float2(1.0, 173.0))
//                        + frameIndex * 23.0
//                        + i * 37.0
//                        + j * 59.0;
//
//             float2 ru = Hash22(seed);
//             float2 d  = SampleDiskConcentric(ru); // unit disk
//
//             float2 offset2D = d * scatterRadius;
//             float3 samplePos = centerPos + T * offset2D.x + B * offset2D.y;
//
//             float4 vol;
//             volumetricLighting_float(samplePos, vol);
//
//             // Slightly lower weight than center sample
//             float w = 1.0;
//             layerAccum += vol * w;
//             layerWeight += w;
//         }
//
//         float4 layerVol = layerAccum / max(layerWeight, 1e-5);
//
//         // Material extinction along slab path
//         float materialT = exp(-_FrostSigmaT * s);
//
//         // Front-to-back attenuation for deeper layers
//         float wLayer = materialT * shadowT;
//
//         accum += layerVol * wLayer;
//         weightSum += wLayer;
//
//         // --- Darkness-as-occlusion heuristic for future layers ---
//         float lum = dot(layerVol.rgb, float3(0.2126, 0.7152, 0.0722));
//         float darkMask = 1.0 - smoothstep(_FrostOccLumLow, _FrostOccLumHigh, lum);
//
//         float occConfidence = 1.0;
//         if (_FrostOccUseAlpha > 0.5)
//             occConfidence = saturate(layerVol.a * _FrostOccAlphaScale);
//
//         float occStep = darkMask * occConfidence * _FrostOccStrength * ds;
//         shadowT *= exp(-occStep);
//
//         if (shadowT < 0.02)
//             break;
//     }
//
//     outputVolumetric = accum / max(weightSum, 1e-5);
// }


#endif
