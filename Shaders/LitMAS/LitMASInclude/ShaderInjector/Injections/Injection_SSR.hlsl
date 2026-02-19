//#!INJECT_BEGIN STANDALONE_DEFINES 0


// Previously was _SLZ_SSR_ENABLED, had to be inverted to support disabling SSR as a material property without
// needing the local _SSR_DISABLED shader feature keyword.
//
// Unity will not allow an enabled global keyword to be disabled by the material's local keyword state.
// This means that in order to disable SSR on a specific material another local keyword is neccessary,
// potentially doubling the shader size with an unnecessary duplicates of the programs for the global SSR
// off state.
//
// However, unity will override the local keyword state with the global state if the global is enabled but
// the local is not Thus, if we have SSR enabled be the default state, the material can enable the disabled
// keyword regardless of the global state

#pragma multi_compile _ _SLZ_SSR_DISABLED

#if !defined(_SLZ_SSR_DISABLED) && !defined(SHADER_API_MOBILE)
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

//#!INJECT_BEGIN FRAG_PARAMETERS 0
#if defined(SLZ_VK_EXT_ENABLED) && defined(_SSR_ENABLED)
SLZ_DECLARE_FRAG_SIZE
#endif
//#!INJECT_END

//#!INJECT_BEGIN LIGHTING_CALC 0
    #if defined(_SSR_ENABLED)
        float2 noiseScreenCoords = i.vertex.xy;
        #if defined(SLZ_VK_EXT_ENABLED)
        RequestFragmentDensityEXT();
        SLZ_SETUP_FRAG_SIZE
        noiseScreenCoords = noiseScreenCoords / float2(SLZ_FRAG_SIZE);
        #endif
        //half4 noiseRGBA = GetScreenNoiseRGBASlice(fragData.screenUV, 0);
        half4 noiseRGBA = SSRGetInterleavedGradientNoise(i.vertex.xy, _BlueNoise_Frame);

        SSRExtraData ssrExtra;
        ssrExtra.meshNormal = UNPACK_NORMAL(i);
        //ssrExtra.lastClipPos = i.lastVertex;
        //ssrExtra.temporalWeight = _SSRTemporalMul;
        ssrExtra.depthDerivativeSum = 0;
        ssrExtra.noise = noiseRGBA;
        ssrExtra.fogFactor = UNPACK_FOG(i);
        ssrExtra.roughnessRange = half2(1.0 - _SSRSmoothnessRange.y, 1.0 - _SSRSmoothnessRange.x);
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