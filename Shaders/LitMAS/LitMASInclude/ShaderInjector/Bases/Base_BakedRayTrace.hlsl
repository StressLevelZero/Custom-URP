
#define SHADERPASS SHADERPASS_RAYTRACE

#include "UnityRaytracingMeshUtils.cginc"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"

//#!INJECT_POINT INCLUDES

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

//#!INJECT_POINT UNIFORMS

CBUFFER_START( UnityPerMaterial )
	float4 _BaseMap_ST;
	half4 _BaseColor;
	//#!INJECT_POINT MATERIAL_CBUFFER
CBUFFER_END


  
//https://coty.tips/raytracing-in-unity/
[shader("closesthit")]
void MyClosestHit(inout RayPayload payload, AttributeData attributes : SV_IntersectionAttributes) {

	payload.color = float4(0,0,0,1); //Intializing
	payload.dir = float3(1,0,0);

	//#!INJECT_POINT CLOSEST_HIT

}