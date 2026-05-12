//#!INJECT_BEGIN UNIVERSAL_DEFINES 0
#pragma shader_feature_local_fragment _ _DETAILS_ON


#if defined(NO_FRACTAL_DETAILS)
    #if defined(_DETAILS_ON)
        #define _FRACTAL_DETAILS_OFF
    #endif
#else
    // phrase fractal details keyword as a negative so it can be disabled both locally and globally
    #pragma multi_compile_fragment _ _FRACTAL_DETAILS_OFF
#endif
//#!INJECT_END

//#!INJECT_BEGIN INCLUDES 0
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Detailmaps.hlsl"
//#!INJECT_END

//#!INJECT_BEGIN UNIFORMS 0
TEXTURE2D(_DetailMap);
#if defined(_FRACTAL_DETAILS_OFF)
    SAMPLER(sampler_DetailMap);
#else
    #define sampler_DetailMap sampler_TrilinearRepeat
#endif
//#!INJECT_END

//#!INJECT_BEGIN FRAG_POST_READ 0
    float2 uv_detail = mad(uv0, _DetailMap_ST.xy, _DetailMap_ST.zw);
//#!INJECT_END

//#!INJECT_BEGIN DETAIL_MAP 0
    #if defined(_DETAILS_ON) && defined(_FRACTAL_DETAILS_OFF)
        BlendDetailMap( _DetailMap, sampler_DetailMap, uv_detail, albedo.rgb, smoothness, normalTS, _DetailNormalScale);
    #elif defined(_DETAILS_ON)
        BlendDetailMapFractal( _DetailMap, _BaseMap,  sampler_DetailMap,  uv_detail, uv_main, albedo.rgb, smoothness, normalTS, _DetailNormalScale);
    #endif
//#!INJECT_END