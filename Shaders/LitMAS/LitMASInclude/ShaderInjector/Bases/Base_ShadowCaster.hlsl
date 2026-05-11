#define SHADERPASS SHADERPASS_SHADOWCASTER

#if defined(SHADER_API_MOBILE)
    //#!INJECT_POINT MOBILE_DEFINES
#else
    //#!INJECT_POINT STANDALONE_DEFINES
#endif

//#!INJECT_POINT UNIVERSAL_DEFINES

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
//#!INJECT_POINT INCLUDES

// Shadow Casting Light geometric parameters. These variables are used when applying the shadow Normal Bias and are set by UnityEngine.Rendering.Universal.ShadowUtils.SetupShadowCasterConstantBuffer in com.unity.render-pipelines.universal/Runtime/ShadowUtils.cs
// For Directional lights, _LightDirection is used when applying shadow Normal Bias.
// For Spot lights and Point lights, _LightPosition is used to compute the actual light direction because it is different at each shadow caster geometry vertex.
float3 _LightDirection;
float3 _LightPosition;

struct Attributes
{
    float4 positionOS   : POSITION;
    float3 normalOS     : NORMAL;
    //#!INJECT_POINT VERTEX_IN
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
    //#!INJECT_POINT INTERPOLATORS
    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
};

//#!INJECT_POINT UNIFORMS

#if defined(CBUFFER_PATH)
#include CBUFFER_PATH
#endif

Varyings vert(Attributes v)
{
    Varyings o;
    UNITY_SETUP_INSTANCE_ID(v);
    UNITY_TRANSFER_INSTANCE_ID(v, o);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
	//#!INJECT_POINT VERTEX_BEGIN

	//#!INJECT_POINT VERTEX_POSITION
	//#!INJECT_DEFAULT
	    o.positionCS = GetShadowPositionHClip(v);
	//#!INJECT_END

	//#!INJECT_POINT VERTEX_END
    return o;
}

half4 frag(Varyings i) : SV_TARGET
{
    UNITY_SETUP_INSTANCE_ID(i);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);

    //#!INJECT_POINT FRAG_BEGIN
    
    //#!INJECT_POINT FRAG_END

    return half4(0, 0, 0, 0);
}
