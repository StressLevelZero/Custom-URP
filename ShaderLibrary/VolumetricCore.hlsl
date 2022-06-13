#ifndef VOLUMETRIC_CORE_INCLUDED
#define VOLUMETRIC_CORE_INCLUDED

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/VolumeRendering.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"

TEXTURECUBE(_SkyTexture);
SAMPLER(sampler_SkyTexture);
int _SkyMipCount;

float4x4 TransposedCameraProjectionMatrix;
float4x4 CameraProjectionMatrix;
float4 _VolumePlaneSettings;
float3 _VolCameraPos;
TEXTURE3D(_VolumetricResult); SAMPLER(sampler_linear_clamp);
float4 _VBufferDistanceEncodingParams;

half4 GetVolumetricColor(float3 positionWS)
{
    //float2 positionNDC = ComputeNormalizedDeviceCoordinates(positionWS, _PrevViewProjMatrix);//viewProjMatrix

    half4 ls = half4(positionWS - _VolCameraPos, -1); //_WorldSpaceCameraPos

    ls = mul(ls, TransposedCameraProjectionMatrix);
    ls.xyz = ls.xyz / ls.w;

    float vdistance = distance(positionWS, _VolCameraPos);

    // vdistance = LinearEyeDepth(vdistance, GetWorldToViewMatrix());

    float W = EncodeLogarithmicDepthGeneralized(vdistance, _VBufferDistanceEncodingParams);

    half halfU = ls.x * 0.5;
    // half halfU = positionNDC.x * 0.5;

     //Figuring out both sides at once and zeroing out the other when blending. 
     //Is this better than branching with an if statement? Andorid doesn't like if statements anyway.
    half3 LUV = half3 (halfU.x, ls.y, W) * (1 - unity_StereoEyeIndex); //Left UV
    half3 RUV = half3(halfU + 0.5, ls.y, W) * (unity_StereoEyeIndex); //Right UV
    half3 DoubleUV = LUV + RUV; // Combined

    //TODO: Make sampling calulations run or not if they are inside or out of the clipped area
    //float ClipUVW =
    //    step(DoubleUV.x, 1) * step(0, DoubleUV.x) *
    //    step(DoubleUV.y, 1) * step(0, DoubleUV.y) ;

//    float random = GenerateHashedRandomFloat(DoubleUV * 4000) * 0.003;
    return SAMPLE_TEXTURE3D_LOD(_VolumetricResult, sampler_linear_clamp, DoubleUV, 0);
}

half4 Volumetrics(half4 color, float3 positionWS) {

#if defined(_VOLUMETRICS_ENABLED)

    half4 FroxelColor = GetVolumetricColor(positionWS);
    color.rgb = FroxelColor.rgb + (color.rgb * FroxelColor.a);

#endif
    return color;
}

half4 VolumetricsAlphaBlend(half4 color, half3 positionWS)
{

#if defined(_VOLUMETRICS_ENABLED)

    half4 FroxelColor = GetVolumetricColor(positionWS);
    //color.rgb = FroxelColor.rgb + (color.rgb * FroxelColor.a);
    color.a = color.a * FroxelColor.a;
#endif
    return color;
}

half4 VolumetricsAdditive(half4 color, float3 positionWS)
{

#if defined(_VOLUMETRICS_ENABLED)

    half4 FroxelColor = GetVolumetricColor(positionWS);
    color.rgb = color.rgb * (1.0h - FroxelColor.a);

#endif
    return color;
}


half4 VolumetricsMultiplicative(half4 color, float3 positionWS)
{

#if defined(_VOLUMETRICS_ENABLED)

    half4 FroxelColor = GetVolumetricColor(positionWS);
    color.rgb = lerp(color.rgb, 1.0h, FroxelColor.a);

#endif
    return color;
}

half4 VolumetricsMultiplicative2(half4 color, float3 positionWS)
{

#if defined(_VOLUMETRICS_ENABLED)

    half4 FroxelColor = GetVolumetricColor(positionWS);
    color.rgb = lerp(color.rgb, 0.5h, FroxelColor.a);

#endif
    return color;
}

//Mip fog

float4 _MipFogParameters = float4(0,5,0.5,0);

//Cloning function for now
real3 DecodeHDREnvironmentMip(real4 encodedIrradiance, real4 decodeInstructions)
{
    // Take into account texture alpha if decodeInstructions.w is true(the alpha value affects the RGB channels)
    real alpha = max(decodeInstructions.w * (encodedIrradiance.a - 1.0) + 1.0, 0.0);

    // If Linear mode is not supported we can skip exponent part
    return (decodeInstructions.x * PositivePow(alpha, decodeInstructions.y)) * encodedIrradiance.rgb;
}

// Based on Uncharted 4 "Mip Sky Fog" trick: http://advances.realtimerendering.com/other/2016/naughty_dog/NaughtyDog_TechArt_Final.pdf
half3 MipFog(float3 viewDirectionWS, float depth, float numMipLevels) {

    float nearParam = _MipFogParameters.x;
    float farParam = _MipFogParameters.y;

#if defined(FOG_LINEAR)
    float mipLevel = ((depth )) * _SkyMipCount;
#else
    float mipLevel = ((1 -  (_MipFogParameters.z * saturate((depth - nearParam) / (farParam - nearParam)))  ) )  * _SkyMipCount;

#endif

//#if defined(REFLECTIONFOG)
  //  return DecodeHDREnvironmentMip(SAMPLE_TEXTURECUBE_LOD(unity_SpecCube0, samplerunity_SpecCube0, viewDirectionWS, mipLevel), unity_SpecCube0_HDR);
  //  return DecodeHDREnvironmentMip(SAMPLE_TEXTURECUBE_LOD(_SkyTexture, samplerunity_SpecCube0, viewDirectionWS, mipLevel), unity_SpecCube0_HDR);
    return (SAMPLE_TEXTURECUBE_LOD(_SkyTexture, sampler_SkyTexture, viewDirectionWS, mipLevel)).rgb;




    //viewDirectionWS
}


#endif
