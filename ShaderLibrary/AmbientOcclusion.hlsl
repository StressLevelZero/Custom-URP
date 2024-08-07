#ifndef AMBIENT_OCCLUSION_INCLUDED
#define AMBIENT_OCCLUSION_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SurfaceData.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SLZExtentions.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/GlobalSamplers.hlsl"

// SLZ MODIFIED // Handle shaders with non-dynamic _SCREEN_SPACE_OCCLUSION

//#if defined(DYNAMIC_SCREEN_SPACE_OCCLUSION)
//	#define BRANCH_SCREEN_SPACE_OCCLUSION _SCREEN_SPACE_OCCLUSION
//#else
//	#if defined(_SCREEN_SPACE_OCCLUSION)
//		#define BRANCH_SCREEN_SPACE_OCCLUSION true
//	#else
//		#define BRANCH_SCREEN_SPACE_OCCLUSION false
//	#endif
//#endif

// END SLZ MODIFIED

// Ambient occlusion
TEXTURE2D_X(_ScreenSpaceOcclusionTexture);
SAMPLER(sampler_ScreenSpaceOcclusionTexture);

struct AmbientOcclusionFactor
{
	half indirectAmbientOcclusion;
	half directAmbientOcclusion;
};

half SampleAmbientOcclusion(float2 normalizedScreenSpaceUV)
{
	float2 uv = UnityStereoTransformScreenSpaceTex(normalizedScreenSpaceUV);
	return half(SAMPLE_TEXTURE2D_X_LOD(_ScreenSpaceOcclusionTexture, sampler_LinearClamp, uv, 0).x);
}

AmbientOcclusionFactor GetScreenSpaceAmbientOcclusion(float2 normalizedScreenSpaceUV)
{
	AmbientOcclusionFactor aoFactor;
	aoFactor.directAmbientOcclusion = 1;
	aoFactor.indirectAmbientOcclusion = 1;
	#if !defined(_SURFACE_TYPE_TRANSPARENT) && defined(_SCREEN_SPACE_OCCLUSION)
	//UNITY_BRANCH if (BRANCH_SCREEN_SPACE_OCCLUSION)
	{
		float ssao = SampleAmbientOcclusion(normalizedScreenSpaceUV);
	
		aoFactor.indirectAmbientOcclusion = ssao;
		aoFactor.directAmbientOcclusion = lerp(half(1.0), ssao, _AmbientOcclusionParam.w);
	}
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
	aoFactor.directAmbientOcclusion = 1;
	aoFactor.indirectAmbientOcclusion = 1;
	#if !defined(_SURFACE_TYPE_TRANSPARENT) && defined(_SCREEN_SPACE_OCCLUSION)
    
	//if (BRANCH_SCREEN_SPACE_OCCLUSION)
    {
        float4 ssao = SampleAmbientOcclusion(inputData.normalizedScreenSpaceUV);
	
		#if defined(_BRDFMAP)
			ssao = SAMPLE_TEXTURE2D_LOD(g_tBRDFMap, sampler_LinearClamp, float2(saturate(.5*ssao.r+.5),dot(inputData.normalWS,inputData.viewDirectionWS) ) ,0 );
		#endif
        aoFactor.indirectAmbientOcclusion = ssao.x;
        aoFactor.directAmbientOcclusion = lerp(half(1.0), ssao.x, _AmbientOcclusionParam.w);
    }

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
