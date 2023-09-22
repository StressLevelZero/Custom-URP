/*-----------------------------------------------------------------------------------------------------*
 *-----------------------------------------------------------------------------------------------------*
 * WARNING: THIS FILE WAS CREATED WITH SHADERINJECTOR, AND SHOULD NOT BE EDITED DIRECTLY. MODIFY THE   *
 * BASE INCLUDE AND INJECTED FILES INSTEAD, AND REGENERATE!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!   *
 *-----------------------------------------------------------------------------------------------------*
 *-----------------------------------------------------------------------------------------------------*/


#define SHADERPASS SHADERPASS_RAYTRACE

#include "UnityRaytracingMeshUtils.cginc"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"


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

CBUFFER_START( UnityPerMaterial )
	float4 _BaseMap_ST;
	half4 _BaseColor;
// Begin Injection MATERIAL_CBUFFER from Injection_NormalMap_CBuffer.hlsl ----------------------------------------------------------
float4 _DetailMap_ST;
half  _Details;
half  _Normals;
// End Injection MATERIAL_CBUFFER from Injection_NormalMap_CBuffer.hlsl ----------------------------------------------------------
// Begin Injection MATERIAL_CBUFFER from Injection_SSR_CBuffer.hlsl ----------------------------------------------------------
	float _SSRTemporalMul;
// End Injection MATERIAL_CBUFFER from Injection_SSR_CBuffer.hlsl ----------------------------------------------------------
// Begin Injection MATERIAL_CBUFFER from Injection_Emission_CBuffer.hlsl ----------------------------------------------------------
	half  _Emission;
	half4 _EmissionColor;
	half  _EmissionFalloff;
	half  _BakedMutiplier;
// End Injection MATERIAL_CBUFFER from Injection_Emission_CBuffer.hlsl ----------------------------------------------------------
CBUFFER_END


  
//https://coty.tips/raytracing-in-unity/
[shader("closesthit")]
void MyClosestHit(inout RayPayload payload, AttributeData attributes : SV_IntersectionAttributes) {

	payload.color = float4(0,0,0,1); //Intializing
	payload.dir = float3(1,0,0);

// Begin Injection CLOSEST_HIT from Injection_Emission_BakedRT.hlsl ----------------------------------------------------------
uint2 launchIdx = DispatchRaysIndex();

uint primitiveIndex = PrimitiveIndex();
uint3 triangleIndicies = UnityRayTracingFetchTriangleIndices(primitiveIndex);
Vertex v0, v1, v2;

v0.texcoord = UnityRayTracingFetchVertexAttribute2(triangleIndicies.x, kVertexAttributeTexCoord0);
v1.texcoord = UnityRayTracingFetchVertexAttribute2(triangleIndicies.y, kVertexAttributeTexCoord0);
v2.texcoord = UnityRayTracingFetchVertexAttribute2(triangleIndicies.z, kVertexAttributeTexCoord0);

// v0.normal = UnityRayTracingFetchVertexAttribute3(triangleIndicies.x, kVertexAttributeNormal);
// v1.normal = UnityRayTracingFetchVertexAttribute3(triangleIndicies.y, kVertexAttributeNormal);
// v2.normal = UnityRayTracingFetchVertexAttribute3(triangleIndicies.z, kVertexAttributeNormal);

float3 barycentrics = float3(1.0 - attributes.barycentrics.x - attributes.barycentrics.y, attributes.barycentrics.x, attributes.barycentrics.y);

Vertex vInterpolated;
vInterpolated.texcoord = v0.texcoord * barycentrics.x + v1.texcoord * barycentrics.y + v2.texcoord * barycentrics.z;
//TODO: Extract normal direction to ignore the backside of emissive objects
//vInterpolated.normal = v0.normal * barycentrics.x + v1.normal * barycentrics.y + v2.normal * barycentrics.z;
// if ( dot(vInterpolated.normal, float3(1,0,0) < 0) ) payload.color =  float4(0,10,0,1) ;
// else payload.color =  float4(10,0,0,1) ;


float4 albedo = float4(_BaseMap.SampleLevel(sampler_BaseMap, vInterpolated.texcoord.xy * _BaseMap_ST.xy + _BaseMap_ST.zw, 0).rgb, 1) * _BaseColor;

float4 emission = _Emission * _EmissionMap.SampleLevel(sampler_EmissionMap, vInterpolated.texcoord * _BaseMap_ST.xy + _BaseMap_ST.zw, 0) * _EmissionColor;

emission.rgb *= lerp(albedo.rgb, 1, emission.a);

payload.color.rgb = emission.rgb * _BakedMutiplier;
// End Injection CLOSEST_HIT from Injection_Emission_BakedRT.hlsl ----------------------------------------------------------

}