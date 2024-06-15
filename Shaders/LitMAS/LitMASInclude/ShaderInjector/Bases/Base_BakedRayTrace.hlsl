
#define SHADERPASS SHADERPASS_RAYTRACE

#include "UnityRaytracingMeshUtils.cginc"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"

//#!INJECT_POINT INCLUDES

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

//#!INJECT_POINT UNIFORMS

CBUFFER_START( UnityPerMaterial )
	//#!INJECT_POINT MATERIAL_CBUFFER_EARLY
	float4 _BaseMap_ST;
	half4 _BaseColor;
	//#!INJECT_POINT MATERIAL_CBUFFER
	int _AlphaPreMult;
CBUFFER_END


  
//https://coty.tips/raytracing-in-unity/
[shader("closesthit")]
void MyClosestHit(inout RayPayload payload, AttributeData attributes : SV_IntersectionAttributes) {

	payload.color = float4(0,0,0,1); //Intializing
	payload.dir = float3(1,0,0);

	//#!INJECT_POINT CLOSEST_HIT

}