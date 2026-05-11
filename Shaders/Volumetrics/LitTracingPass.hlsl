#ifndef RAYTRACING_META_PASS
#define RAYTRACING_META_PASS

#include "Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl"

#if !defined(DYNAMIC_EMISSION)
    #if !defined(_EMISSION)
        #define _EMISSION false
    #else
        #undef _EMISSION
        #define _EMISSION true
    #endif
#endif

#define MATERIAL_PROVIDES_EVALUATE
void EvaluateMaterial(float2 hitUV, out float3 albedo, out float3 emission)
{
    float2 uv = hitUV * _BaseMap_ST.xy + _BaseMap_ST.zw;
    float3 baseRGB = _BaseMap.SampleLevel(sampler_BaseMap, uv, 0).rgb * _BaseColor.rgb;
    albedo = baseRGB;
    if (_EMISSION)    {
        float4 em = _EmissionMap.SampleLevel(sampler_EmissionMap, uv, 0) * _EmissionColor;
        emission = em.rgb;}
    else   emission = 0;
}
#include "Packages/com.unity.render-pipelines.universal/Shaders/Volumetrics/BakedRaytracePass.hlsl"

#endif
