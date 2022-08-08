#ifndef UNITY_DECLARE_DEPTH_TEXTURE_INCLUDED
#define UNITY_DECLARE_DEPTH_TEXTURE_INCLUDED
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

TEXTURE2D_X_FLOAT(_CameraHiZDepthTexture);
SAMPLER(sampler_CameraHiZDepthTexture);

CBUFFER_START(HiZDimBuffer)
float2 _HiZMipDim[16];
CBUFFER_END

float SampleHiZDepth(float2 uv)
{
    return SAMPLE_TEXTURE2D_X(_CameraHiZDepthTexture, sampler_CameraHiZDepthTexture, UnityStereoTransformScreenSpaceTex(uv)).r;
}

float SampleHiZDepthLOD(float3 uv)
{
    return SAMPLE_TEXTURE2D_X_LOD(_CameraHiZDepthTexture, sampler_CameraHiZDepthTexture, UnityStereoTransformScreenSpaceTex(uv.xy), uv.z).r;
}

float LoadHiZDepth(int3 uv)
{
    return LOAD_TEXTURE2D_X_LOD(_CameraHiZDepthTexture, uv.xy, uv.z).r;
}
#endif
