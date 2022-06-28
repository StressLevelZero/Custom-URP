#if !defined(SLZ_SSR_GLOBALS)
#define SLZ_SSR_GLOBALS


#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SSR.hlsl"

CBUFFER_START(SSRConstants)
float _SSRHitRadius;
float _SSREdgeFade;
float _SSRSteps;
float _empty;
CBUFFER_END

SamplerState sampler_trilinear_clamp;

SSRData GetSSRDataWithGlobalSettings(
    float3	wPos,
    float3	viewDir,
    half3	rayDir,
    half3	faceNormal,
    half	perceptualRoughness,
    half4   noise)
{
    SSRData ssrData;
    ssrData.wPos = wPos;
    ssrData.viewDir = viewDir;
    ssrData.rayDir = rayDir;
    ssrData.faceNormal = faceNormal;
    ssrData.hitRadius = _SSRHitRadius;
    ssrData.maxSteps = _SSRSteps;
    ssrData.edgeFade = _SSREdgeFade;
    ssrData.perceptualRoughness = perceptualRoughness;
    ssrData.scrnParams = _ScreenParams.xy;
    ssrData.GrabTextureSSR = _CameraOpaqueTexture;
    ssrData.samplerGrabTextureSSR = sampler_trilinear_clamp;
    ssrData.noise = noise;
    return ssrData;
}

#endif