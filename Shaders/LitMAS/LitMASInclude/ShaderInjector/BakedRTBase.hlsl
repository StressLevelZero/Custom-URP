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


#include "Packages/com.unity.render-pipelines.universal/Shaders/Volumetrics/BakedRaytraceParts.hlsl"



#if defined(CBUFFER_PATH)
#include CBUFFER_PATH
#endif

  
//https://coty.tips/raytracing-in-unity/
[shader("closesthit")]
void MyClosestHit(inout RayPayload payload, AttributeData attributes : SV_IntersectionAttributes) {

	EARLY_OUT_ON_BACKFACE(payload);	
	InterpolationData interpData;	
	InterpolatedSurface(attributes, interpData, payload);
	float4 albedo = 0;
	float4 emission = 0;
	WritePayloads(payload,albedo,emission);

}