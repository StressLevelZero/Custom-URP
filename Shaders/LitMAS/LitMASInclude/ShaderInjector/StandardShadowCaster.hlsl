/*-----------------------------------------------------------------------------------------------------*
 *-----------------------------------------------------------------------------------------------------*
 * WARNING: THIS FILE WAS CREATED WITH SHADERINJECTOR, AND SHOULD NOT BE EDITED DIRECTLY. MODIFY THE   *
 * BASE INCLUDE AND INJECTED FILES INSTEAD, AND REGENERATE!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!   *
 *-----------------------------------------------------------------------------------------------------*
 *-----------------------------------------------------------------------------------------------------*/

#define SHADERPASS SHADERPASS_SHADOWCASTER

#if defined(SHADER_API_MOBILE)
#else
#endif

// Begin Injection UNIVERSAL_DEFINES from Injection_Cutout_DepthOnly.hlsl ----------------------------------------------------------
#pragma shader_feature_local_fragment _ALPHATEST_ON
// End Injection UNIVERSAL_DEFINES from Injection_Cutout_DepthOnly.hlsl ----------------------------------------------------------

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

// Shadow Casting Light geometric parameters. These variables are used when applying the shadow Normal Bias and are set by UnityEngine.Rendering.Universal.ShadowUtils.SetupShadowCasterConstantBuffer in com.unity.render-pipelines.universal/Runtime/ShadowUtils.cs
// For Directional lights, _LightDirection is used when applying shadow Normal Bias.
// For Spot lights and Point lights, _LightPosition is used to compute the actual light direction because it is different at each shadow caster geometry vertex.
float3 _LightDirection;
float3 _LightPosition;

struct Attributes
{
    float4 positionOS   : POSITION;
    float3 normalOS     : NORMAL;
// Begin Injection VERTEX_IN from Injection_Cutout_DepthOnly.hlsl ----------------------------------------------------------
	float2 uv0 : TEXCOORD0;
// End Injection VERTEX_IN from Injection_Cutout_DepthOnly.hlsl ----------------------------------------------------------
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

float4 GetShadowPositionHClip(Attributes input)
{
	float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
	float3 normalWS = TransformObjectToWorldNormal(input.normalOS);

#if _CASTING_PUNCTUAL_LIGHT_SHADOW
    float3 lightDirectionWS = normalize(_LightPosition - positionWS);
#else
	float3 lightDirectionWS = _LightDirection;
#endif
	float2 vShadowOffsets = GetShadowOffsets(normalWS, lightDirectionWS);
    //positionWS.xyz -= vShadowOffsets.x * normalWS.xyz * .01;
	positionWS.xyz -= vShadowOffsets.y * lightDirectionWS.xyz * .01;
	float4 positionCS = TransformObjectToHClip(mul(unity_WorldToObject, float4(positionWS.xyz, 1.0)).xyz);
    //float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));

#if UNITY_REVERSED_Z
    positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
#else
	positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
#endif

	return positionCS;
}

struct Varyings
{
    float4 positionCS   : SV_POSITION;
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

Varyings vert(Attributes v)
{
    Varyings o;
    UNITY_SETUP_INSTANCE_ID(v);
    UNITY_TRANSFER_INSTANCE_ID(v, o);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

	    o.positionCS = GetShadowPositionHClip(v);

// Begin Injection VERTEX_END from Injection_Cutout_DepthOnly.hlsl ----------------------------------------------------------
	o.uv0XY = mad(v.uv0.xy, _BaseMap_ST.xy, _BaseMap_ST.zw);
// End Injection VERTEX_END from Injection_Cutout_DepthOnly.hlsl ----------------------------------------------------------
    return o;
}

half4 frag(Varyings i) : SV_TARGET
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
