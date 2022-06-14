#ifndef AMBIENT_OCCLUSION_INCLUDED
#define AMBIENT_OCCLUSION_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SurfaceData.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SLZExtentions.hlsl"


// Ambient occlusion
TEXTURE2D_X(_ScreenSpaceOcclusionTexture);
SAMPLER(sampler_ScreenSpaceOcclusionTexture);

struct AmbientOcclusionFactor
{
    #if defined(_BRDFMAP)
    half4 indirectAmbientOcclusion;
    half4 directAmbientOcclusion;
    #else
    half indirectAmbientOcclusion;
    half directAmbientOcclusion;
    #endif
};

half SampleAmbientOcclusion(float2 normalizedScreenSpaceUV)
{
    float2 uv = UnityStereoTransformScreenSpaceTex(normalizedScreenSpaceUV);
    return half(SAMPLE_TEXTURE2D_X(_ScreenSpaceOcclusionTexture, sampler_ScreenSpaceOcclusionTexture, uv).x);
}

AmbientOcclusionFactor GetScreenSpaceAmbientOcclusion(float2 normalizedScreenSpaceUV)
{
    AmbientOcclusionFactor aoFactor;

    #if defined(_SCREEN_SPACE_OCCLUSION) && !defined(_SURFACE_TYPE_TRANSPARENT)
    float4 ssao = SampleAmbientOcclusion(normalizedScreenSpaceUV);
    #if defined(_BRDFMAP)
    ssao = SAMPLE_TEXTURE2D_LOD(g_tBRDFMap, BRDF_linear_clamp_sampler, float2(saturate(.5*ssao.r+.5),1) ,0 );
    #endif
    aoFactor.indirectAmbientOcclusion = ssao;
    aoFactor.directAmbientOcclusion = lerp(half(1.0), ssao, _AmbientOcclusionParam.w);
    #else
    aoFactor.directAmbientOcclusion = 1;
    aoFactor.indirectAmbientOcclusion = 1;
    #endif

    #if defined(DEBUG_DISPLAY)
    switch(_DebugLightingMode)
    {
        case DEBUGLIGHTINGMODE_LIGHTING_WITHOUT_NORMAL_MAPS:
            aoFactor.directAmbientOcclusion = 0.5;
            aoFactor.indirectAmbientOcclusion = 0.5;
            break;

        case DEBUGLIGHTINGMODE_LIGHTING_WITH_NORMAL_MAPS:
            aoFactor.directAmbientOcclusion *= 0.5;
            aoFactor.indirectAmbientOcclusion *= 0.5;
            break;
    }
    #endif

    return aoFactor;
}

AmbientOcclusionFactor GetScreenSpaceAmbientOcclusionDir(InputData inputData)
{
    AmbientOcclusionFactor aoFactor;

    #if defined(_SCREEN_SPACE_OCCLUSION) && !defined(_SURFACE_TYPE_TRANSPARENT)
    float4 ssao = SampleAmbientOcclusion(inputData.normalizedScreenSpaceUV);
    #if defined(_BRDFMAP)
    ssao = SAMPLE_TEXTURE2D_LOD(g_tBRDFMap, BRDF_linear_clamp_sampler, float2(saturate(.5*ssao.r+.5),dot(inputData.normalWS,inputData.viewDirectionWS) ) ,0 );
    #endif
    aoFactor.indirectAmbientOcclusion = ssao;
    aoFactor.directAmbientOcclusion = lerp(half(1.0), ssao, _AmbientOcclusionParam.w);
    #else
    aoFactor.directAmbientOcclusion = 1;
    aoFactor.indirectAmbientOcclusion = 1;
    #endif

    #if defined(DEBUG_DISPLAY)
    switch(_DebugLightingMode)
    {
    case DEBUGLIGHTINGMODE_LIGHTING_WITHOUT_NORMAL_MAPS:
        aoFactor.directAmbientOcclusion = 0.5;
        aoFactor.indirectAmbientOcclusion = 0.5;
        break;

    case DEBUGLIGHTINGMODE_LIGHTING_WITH_NORMAL_MAPS:
        aoFactor.directAmbientOcclusion *= 0.5;
        aoFactor.indirectAmbientOcclusion *= 0.5;
        break;
    }
    #endif

    return aoFactor;
}
AmbientOcclusionFactor CreateAmbientOcclusionFactor(float2 normalizedScreenSpaceUV, half occlusion)
{
    AmbientOcclusionFactor aoFactor = GetScreenSpaceAmbientOcclusion(normalizedScreenSpaceUV);

    aoFactor.indirectAmbientOcclusion = min(aoFactor.indirectAmbientOcclusion, occlusion);
    aoFactor.directAmbientOcclusion = min(aoFactor.directAmbientOcclusion, occlusion);
    return aoFactor;
}

//
AmbientOcclusionFactor CreateAmbientOcclusionFactorDir(InputData inputData, SurfaceData surfaceData)
{
    AmbientOcclusionFactor aoFactor = GetScreenSpaceAmbientOcclusionDir(inputData);

    aoFactor.indirectAmbientOcclusion = min(aoFactor.indirectAmbientOcclusion, surfaceData.occlusion);
    aoFactor.directAmbientOcclusion = min(aoFactor.directAmbientOcclusion, surfaceData.occlusion);
    return aoFactor;
}

AmbientOcclusionFactor CreateAmbientOcclusionFactor(InputData inputData, SurfaceData surfaceData)
{
    #if defined(_BRDFMAP)
    return CreateAmbientOcclusionFactorDir(inputData, surfaceData);
    #else
    return CreateAmbientOcclusionFactor(inputData.normalizedScreenSpaceUV, surfaceData.occlusion);
    #endif
}

#endif
