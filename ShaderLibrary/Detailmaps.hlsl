#if !defined(SLZ_DETAILMAP)
#define SLZ_DETAILMAP

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/FractalSampling.hlsl"

half3 OverlayBlendDetail(half source, half3 destination)
{
    half3 switch0 = round(destination); // if destination >= 0.5 then 1, else 0 assuming 0-1 input
    half3 blendGreater = mad(mad(2.0, destination, -2.0), 1.0 - source, 1.0); // (2.0 * destination - 2.0) * ( 1.0 - source) + 1.0
    half3 blendLesser = (2.0 * source) * destination;
    return mad(switch0, blendGreater, mad(-switch0, blendLesser, blendLesser)); // switch0 * blendGreater + (1 - switch0) * blendLesser 
    //return half3(destination.r > 0.5 ? blendGreater.r : blendLesser.r,
    //             destination.g > 0.5 ? blendGreater.g : blendLesser.g,
    //             destination.b > 0.5 ? blendGreater.b : blendLesser.b
    //            );
}

half OverlayBlendDetail(half source, half destination)
{
    half switch0 = round(destination); // if destination >= 0.5 then 1, else 0 assuming 0-1 input
    half blendGreater = mad(mad(2.0, destination, -2.0), 1.0 - source, 1.0); // (2.0 * destination - 2.0) * ( 1.0 - source) + 1.0
    half blendLesser = (2.0 * source) * destination;
    return mad(switch0, blendGreater, mad(-switch0, blendLesser, blendLesser)); // switch0 * blendGreater + (1 - switch0) * blendLesser 
    //return half3(destination.r > 0.5 ? blendGreater.r : blendLesser.r,
    //             destination.g > 0.5 ? blendGreater.g : blendLesser.g,
    //             destination.b > 0.5 ? blendGreater.b : blendLesser.b
    //            );
}

/// Automatically accounts for texture scaling 
void DetailMap_fractal_blend_float(Texture2D _DetailMap, SamplerState sampler_DetailMap, float2 uv_detail,
                            inout half3 albedo, inout half smoothness, inout half3 normalTS  )
{
    half4 detailMap = SAMPLE_TEXTURE2D_FRACTAL(_DetailMap, sampler_DetailMap, uv_detail);
    half3 detailTS = UnpackNormalAG(detailMap);
    normalTS = SafeNormalize(BlendNormalRNM(normalTS, detailTS));       
    smoothness = OverlayBlendDetail(detailMap.b, smoothness);
    albedo = OverlayBlendDetail(detailMap.r, albedo);
}


/// Standard UV texture behavior
void DetailMap_UV_blend_float(Texture2D _DetailMap, SamplerState sampler_DetailMap, float2 uv_detail,
                            inout half3 albedo, inout half smoothness, inout half3 normalTS  )
{
    half4 detailMap = SAMPLE_TEXTURE2D(_DetailMap, sampler_DetailMap, uv_detail);
    half3 detailTS = UnpackNormalAG(detailMap);
    normalTS = SafeNormalize(BlendNormalRNM(normalTS, detailTS));       
    smoothness = OverlayBlendDetail(detailMap.b, smoothness);
    albedo = OverlayBlendDetail(detailMap.r, albedo);
}

#endif