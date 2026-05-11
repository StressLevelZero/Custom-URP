
#define SHADERPASS SHADERPASS_RAYTRACE

//#include "UnityRaytracingMeshUtils.cginc"

#pragma raytracing BakeHit
//#include "Packages/com.unity.render-pipelines.universal/Shaders/Volumetrics/BakedRaytraceParts.hlsl"

//float4 _BaseColor;
Texture2D<float4> _BaseMap;
//float4 _BaseMap_ST;
SamplerState sampler_BaseMap;
Texture2D<float4> _EmissionMap;
SamplerState sampler_EmissionMap;

#if defined(CBUFFER_PATH)
#include CBUFFER_PATH
#endif
  
//https://coty.tips/raytracing-in-unity/
// [shader("closesthit")]
// void MyClosestHit(inout RayPayload payload, AttributeData attributes : SV_IntersectionAttributes) {
//
// 	EARLY_OUT_ON_BACKFACE(payload);	
// 	InterpolationData interpData;	
// 	InterpolatedSurface(attributes, interpData, payload);
// 	float4 albedo = 0;
// 	float4 emission = 0;
// 	// Begin Injection CLOSEST_HIT from Injection_Emission_BakedRT.hlsl ----------------------------------------------------------
// 	
// 	albedo = float4(_BaseMap.SampleLevel(sampler_BaseMap, interpData.vertex.texcoord.xy * _BaseMap_ST.xy + _BaseMap_ST.zw, 0).rgb, 1) * _BaseColor;
// 		emission = _Emission * _EmissionMap.SampleLevel(sampler_EmissionMap, interpData.vertex.texcoord * _BaseMap_ST.xy + _BaseMap_ST.zw, 0) * _EmissionColor;
// 	emission.rgb *= lerp(albedo.rgb, 1, emission.a);
// 	emission = max(emission * _BakedMutiplier,0);
// 	
// 	// End Injection CLOSEST_HIT from Injection_Emission_BakedRT.hlsl ----------------------------------------------------------
// 	WritePayloads(payload,albedo,emission);
//
// }

#define MATERIAL_PROVIDES_EVALUATE
void EvaluateMaterial(float2 hitUV, out float3 albedo, out float3 emission)
{
    float2 uv = hitUV * _BaseMap_ST.xy + _BaseMap_ST.zw;
    float3 baseRGB = _BaseMap.SampleLevel(sampler_BaseMap, uv, 0).rgb * _BaseColor.rgb;
    albedo = baseRGB;
    float4 em = _Emission * _EmissionMap.SampleLevel(sampler_EmissionMap, uv, 0) * _EmissionColor;
    em.rgb  *= lerp(baseRGB, 1.0, em.a);   // alpha = how "untinted" the emission reads
    emission = em.rgb * _BakedMutiplier;
}
#include "Packages/com.unity.render-pipelines.universal/Shaders/Volumetrics/BakedRaytracePass.hlsl"
