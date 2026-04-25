/*-----------------------------------------------------------------------------------------------------*
 *-----------------------------------------------------------------------------------------------------*
 * WARNING: THIS FILE WAS CREATED WITH SHADERINJECTOR, AND SHOULD NOT BE EDITED DIRECTLY. MODIFY THE   *
 * BASE INCLUDE AND INJECTED FILES INSTEAD, AND REGENERATE!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!   *
 *-----------------------------------------------------------------------------------------------------*
 *-----------------------------------------------------------------------------------------------------*/


#define SHADERPASS SHADERPASS_RAYTRACE

#include "UnityRaytracingMeshUtils.cginc"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"


// Unity Tries to define half as min16float, which isn't handled by unity's interface with the shader compiler for raytracing.

#ifdef half
#undef half
#define half float
#endif

#ifdef half2
#undef half2
#define half2 float2
#endif

#ifdef half3
#undef half3
#define half3 float3
#endif

#ifdef half4
#undef half4
#define half4 float4
#endif

#pragma raytracing BakeHit

struct RayPayload
{
    float4 color;
	float3 dir;
};
  
struct AttributeData
{
    float2 barycentrics;
};

struct Vertex
{
    float2 texcoord;
    float3 normal;
};

// Begin Injection UNIFORMS from Injection_Emission_BakedRT.hlsl ----------------------------------------------------------
Texture2D<float4> _BaseMap;
SamplerState sampler_BaseMap;
Texture2D<float4> _EmissionMap;
SamplerState sampler_EmissionMap;
// End Injection UNIFORMS from Injection_Emission_BakedRT.hlsl ----------------------------------------------------------

#if defined(CBUFFER_PATH)
#include CBUFFER_PATH
#endif

  
//https://coty.tips/raytracing-in-unity/
[shader("closesthit")]
void MyClosestHit(inout RayPayload payload, AttributeData attributes : SV_IntersectionAttributes) {

	payload.color = float4(0,0,0,1); //Intializing
	payload.dir = float3(1,0,0);

// Begin Injection CLOSEST_HIT from Injection_Emission_BakedRT.hlsl ----------------------------------------------------------

//#ifndef _DOUBLE_SIDED_EMISSION
// Backface: occluded but no emission. Ray is done.
if (HitKind() == HIT_KIND_TRIANGLE_BACK_FACE)
{
	payload.color = float4(0, 0, 0, 1);
	return;
}
//#endif

uint2 launchIdx = DispatchRaysIndex();

uint primitiveIndex = PrimitiveIndex();
uint3 triangleIndicies = UnityRayTracingFetchTriangleIndices(primitiveIndex);
Vertex v0, v1, v2;

//uint3 tri = UnityRayTracingFetchTriangleIndices(PrimitiveIndex());
//float3 n0 = UnityRayTracingFetchVertexAttribute3(tri.x, kVertexAttributeNormal);
//float3 n1 = UnityRayTracingFetchVertexAttribute3(tri.y, kVertexAttributeNormal);
//float3 n2 = UnityRayTracingFetchVertexAttribute3(tri.z, kVertexAttributeNormal);

float3 bary = float3(1.0 - attributes.barycentrics.x - attributes.barycentrics.y,
                     attributes.barycentrics.x, attributes.barycentrics.y);
//float3 nOS = normalize(n0 * bary.x + n1 * bary.y + n2 * bary.z);
//float3 nWS = normalize(TransformObjectToWorldNormal(nOS));
	
//#ifndef _DOUBLE_SIDED_EMISSION
// Backface: occluded but no emission. Ray is done.
if (HitKind() == HIT_KIND_TRIANGLE_BACK_FACE)
{
    payload.color = float4(0, 0, 0, 1);
    return;
}
//#endif

v0.texcoord = UnityRayTracingFetchVertexAttribute2(triangleIndicies.x, kVertexAttributeTexCoord0);
v1.texcoord = UnityRayTracingFetchVertexAttribute2(triangleIndicies.y, kVertexAttributeTexCoord0);
v2.texcoord = UnityRayTracingFetchVertexAttribute2(triangleIndicies.z, kVertexAttributeTexCoord0);
		
Vertex vInterpolated;
vInterpolated.texcoord = v0.texcoord * bary.x + v1.texcoord * bary.y + v2.texcoord * bary.z;
float4 albedo = float4(_BaseMap.SampleLevel(sampler_BaseMap, vInterpolated.texcoord.xy * _BaseMap_ST.xy + _BaseMap_ST.zw, 0).rgb, 1) * _BaseColor;

float4 emission = _Emission * _EmissionMap.SampleLevel(sampler_EmissionMap, vInterpolated.texcoord * _BaseMap_ST.xy + _BaseMap_ST.zw, 0) * _EmissionColor;

emission.rgb *= lerp(albedo.rgb, 1, emission.a);
emission = max(emission * _BakedMutiplier,0);
payload.color.rgb = emission.rgb ;
// End Injection CLOSEST_HIT from Injection_Emission_BakedRT.hlsl ----------------------------------------------------------

}