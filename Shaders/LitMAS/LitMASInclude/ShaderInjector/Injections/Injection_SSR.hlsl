//#!INJECT_BEGIN STANDALONE_DEFINES 0
#pragma multi_compile _ _SLZ_SSR_ENABLED
#pragma shader_feature_local _ _NO_SSR
#if defined(_SLZ_SSR_ENABLED) && !defined(_NO_SSR) && !defined(SHADER_API_MOBILE)
	#define _SSR_ENABLED
#endif
//#!INJECT_END

//#!INJECT_BEGIN INCLUDES 0
#if !defined(SHADER_API_MOBILE)
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SLZLightingSSR.hlsl"
#endif
//#!INJECT_END

//#!INJECT_BEGIN INTERPOLATORS 1
	//#!TEXCOORD float4 lastVertex 1
//#!INJECT_END

//#!INJECT_BEGIN VERTEX_END 0
	//#if defined(_SSR_ENABLED)
	//	float4 lastWPos = mul(GetPrevObjectToWorldMatrix(), v.vertex);
	//	o.lastVertex = mul(prevVP, lastWPos);
	//#endif
//#!INJECT_END

//#!INJECT_BEGIN LIGHTING_CALC 0
	#if defined(_SSR_ENABLED)
		half4 noiseRGBA = GetScreenNoiseRGBA(fragData.screenUV);

		SSRExtraData ssrExtra;
		ssrExtra.meshNormal = UNPACK_NORMAL(i);
		//ssrExtra.lastClipPos = i.lastVertex;
		ssrExtra.temporalWeight = _SSRTemporalMul;
		ssrExtra.depthDerivativeSum = 0;
		ssrExtra.noise = noiseRGBA;
		ssrExtra.fogFactor = UNPACK_FOG(i);

		color = SLZPBRFragmentSSR(fragData, surfData, ssrExtra, _Surface);
		color.rgb = max(0, color.rgb);
	#else
		color = SLZPBRFragment(fragData, surfData, _Surface);
	#endif
//#!INJECT_END

//#!INJECT_BEGIN VOLUMETRIC_FOG 0
	#if !defined(_SSR_ENABLED)
		color = MixFogSurf(color, -fragData.viewDir, UNPACK_FOG(i), _Surface);
		
		color = VolumetricsSurf(color, fragData.position, _Surface);
	#endif
//#!INJECT_END