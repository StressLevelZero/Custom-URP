/*-----------------------------------------------------------------------------------------------------*
 *-----------------------------------------------------------------------------------------------------*
 * WARNING: THIS FILE WAS CREATED WITH SHADERINJECTOR, AND SHOULD NOT BE EDITED DIRECTLY. MODIFY THE   *
 * BASE INCLUDE AND INJECTED FILES INSTEAD, AND REGENERATE!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!   *
 *-----------------------------------------------------------------------------------------------------*
 *-----------------------------------------------------------------------------------------------------*/

#define SHADERPASS SHADERPASS_DEPTHNORMALS

#if defined(SHADER_API_MOBILE)
#else
#endif

// Begin Injection UNIVERSAL_DEFINES from Injection_Cutout_DepthOnly.hlsl ----------------------------------------------------------
#pragma shader_feature_local_fragment _ALPHATEST_ON
// End Injection UNIVERSAL_DEFINES from Injection_Cutout_DepthOnly.hlsl ----------------------------------------------------------

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
// #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Texture.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/Editor/ShaderGraph/Includes/ShaderPass.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/EncodeNormalsTexture.hlsl"

struct appdata
{
	float4 vertex : POSITION;
// Begin Injection VERTEX_IN from Injection_Cutout_DepthOnly.hlsl ----------------------------------------------------------
	float2 uv0 : TEXCOORD0;
// End Injection VERTEX_IN from Injection_Cutout_DepthOnly.hlsl ----------------------------------------------------------
	UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct v2f
{
	float4 vertex : SV_POSITION;
// Begin Injection INTERPOLATORS from Injection_Cutout_DepthOnly.hlsl ----------------------------------------------------------
	float2 uv0XY : TEXCOORD0;
// End Injection INTERPOLATORS from Injection_Cutout_DepthOnly.hlsl ----------------------------------------------------------
	UNITY_VERTEX_INPUT_INSTANCE_ID
	UNITY_VERTEX_OUTPUT_STEREO
};

// Begin Injection UNIFORMS from Injection_Cutout_DepthOnly.hlsl ----------------------------------------------------------
	TEXTURE2D(_BaseMap);
	SAMPLER(sampler_BaseMap);
// End Injection UNIFORMS from Injection_Cutout_DepthOnly.hlsl ----------------------------------------------------------

#if defined(CBUFFER_PATH)
#include CBUFFER_PATH
#endif


v2f vert(appdata v)
{

	v2f o;
	UNITY_SETUP_INSTANCE_ID(v);
	UNITY_TRANSFER_INSTANCE_ID(v, o);
	UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);


	o.vertex = TransformObjectToHClip(v.vertex.xyz);

// Begin Injection VERTEX_END from Injection_Cutout_DepthOnly.hlsl ----------------------------------------------------------
	o.uv0XY = mad(v.uv0.xy, _BaseMap_ST.xy, _BaseMap_ST.zw);
// End Injection VERTEX_END from Injection_Cutout_DepthOnly.hlsl ----------------------------------------------------------
	return o;
}

half4 frag(v2f i) : SV_Target
{
   UNITY_SETUP_INSTANCE_ID(i);
   UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);

// Begin Injection FRAG_BEGIN from Injection_Cutout_DepthOnly.hlsl ----------------------------------------------------------
#if defined(_ALPHATEST_ON)
	float alpha = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv0XY.xy).a;
	clip((alpha * _BaseColor.a) - _Cutoff);
#endif
// End Injection FRAG_BEGIN from Injection_Cutout_DepthOnly.hlsl ----------------------------------------------------------


	return half4(0, 0, 0, 0);
}