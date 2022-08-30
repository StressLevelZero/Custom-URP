#ifndef UNITY_DECLARE_DEPTH_TEXTURE_INCLUDED
#define UNITY_DECLARE_DEPTH_TEXTURE_INCLUDED
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

TEXTURE2D_X_FLOAT(_CameraHiZDepthTexture);
SAMPLER(sampler_CameraHiZDepthTexture);

struct HiZDim
{
    float4 dim;// XY: dimensions of mip X, ZW: dimensions of mip X divided by mip 0
};

StructuredBuffer<HiZDim> HiZDimBuffer;
uint _HiZHighestMip;
/*
CBUFFER_START(HiZDimBuffer)
float4 _HiZMipDim[15];

CBUFFER_END
*/

float2 SampleHiZDepth(float2 uv)
{
    return SAMPLE_TEXTURE2D_X(_CameraHiZDepthTexture, sampler_CameraHiZDepthTexture, UnityStereoTransformScreenSpaceTex(uv)).rg;
}

float2 SampleHiZDepthLOD(float3 uv)
{
    return SAMPLE_TEXTURE2D_X_LOD(_CameraHiZDepthTexture, sampler_CameraHiZDepthTexture, UnityStereoTransformScreenSpaceTex(uv.xy), uv.z).rg;
}

float2 LoadHiZDepth(int3 uv)
{
    return LOAD_TEXTURE2D_X_LOD(_CameraHiZDepthTexture, uv.xy, uv.z).rg;
}
#endif
