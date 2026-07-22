
#define SHADERPASS SHADERPASS_RAYTRACE

#include "UnityRaytracingMeshUtils.cginc"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

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


#include "Packages/com.unity.render-pipelines.universal/Shaders/Volumetrics/BakedRaytraceParts.hlsl"


//#!INJECT_POINT UNIFORMS

#if defined(CBUFFER_PATH)
#include CBUFFER_PATH
#endif

//#!INJECT_POINT FUNCTIONS
  
//https://coty.tips/raytracing-in-unity/
[shader("closesthit")]
void MyClosestHit(inout RayPayload payload, AttributeData attributes : SV_IntersectionAttributes) {

	EARLY_OUT_ON_BACKFACE(payload);	
	InterpolationData interpData;	
	InterpolatedSurface(attributes, interpData, payload);
	float4 albedo = 0;
	float4 emission = 0;
	//#!INJECT_POINT CLOSEST_HIT
	WritePayloads(payload,albedo,emission);

}