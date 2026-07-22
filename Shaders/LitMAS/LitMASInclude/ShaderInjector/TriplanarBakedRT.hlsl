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

// Begin Injection INCLUDES from Injection_Emission_BakedRT_triplanar.hlsl ----------------------------------------------------------
#pragma multi_compile_local_fragment _ _EXPENSIVE_TP

#if defined(_EXPENSIVE_TP)
#define SLZ_SAMPLE_TP_MAIN(tex, sampl, uv) tex.SampleLevel( sampl, uv, 0)
#else	
#define SLZ_SAMPLE_TP_MAIN(tex, sampl, uv) tex.SampleLevel( sampl, uv, 0)
#endif

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SLZTriplanar.hlsl"
// End Injection INCLUDES from Injection_Emission_BakedRT_triplanar.hlsl ----------------------------------------------------------

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


// Begin Injection UNIFORMS from Injection_Emission_BakedRT_triplanar.hlsl ----------------------------------------------------------
Texture2D<float4> _BaseMap;
SamplerState sampler_BaseMap;
Texture2D<float4> _EmissionMap;
SamplerState sampler_EmissionMap;
// End Injection UNIFORMS from Injection_Emission_BakedRT_triplanar.hlsl ----------------------------------------------------------

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
// Begin Injection CLOSEST_HIT from Injection_Emission_BakedRT_triplanar.hlsl ----------------------------------------------------------
float2 uvTP;
half3x3 TStoWsTP;
half2 scale = 1.0/_UVScaler;
		
#if defined(_EXPENSIVE_TP)
tpDerivatives tpDD;
GetDirectionalDerivatives( payload.hitPos, tpDD);
half2 ddxTP, ddyTP;
GetTPUVExpensive(uvTP, ddxTP, ddyTP, TStoWsTP, payload.hitPos, normalize(UNPACK_NORMAL(i)), tpDD);
ddxTP = _RotateUVs ? half2(-ddxTP.y, ddxTP.x) : ddxTP;
ddyTP = _RotateUVs ? half2(-ddyTP.y, ddyTP.x) : ddyTP;
half2 ddxMain = ddxTP * scale;
half2 ddyMain = ddyTP * scale;
#else
GetTPUVCheap(uvTP, TStoWsTP, payload.hitPos, payload.worldNormal);
#endif
		
uvTP = _RotateUVs ? float2(-uvTP.y, uvTP.x) : uvTP;
float2 uv_main = mad(uvTP, scale, _BaseMap_ST.zw);
	albedo = SLZ_SAMPLE_TP_MAIN(_BaseMap, sampler_BaseMap, uv_main);
//albedo.a = _Surface == 0 ? half(1.0) : albedo.a;

emission = _Emission * SLZ_SAMPLE_TP_MAIN(_EmissionMap, sampler_EmissionMap, uv_main) * _EmissionColor;
emission.rgb *= lerp(albedo.rgb, 1, emission.a);
emission = max(emission * _BakedMutiplier,0);
// End Injection CLOSEST_HIT from Injection_Emission_BakedRT_triplanar.hlsl ----------------------------------------------------------
	WritePayloads(payload,albedo,emission);

}