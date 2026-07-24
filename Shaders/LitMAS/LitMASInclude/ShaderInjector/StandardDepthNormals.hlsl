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

// Begin Injection UNIVERSAL_DEFINES from Injection_Cutout_DepthNormals_NM.hlsl ----------------------------------------------------------
#pragma shader_feature_local_fragment _ALPHATEST_ON
// End Injection UNIVERSAL_DEFINES from Injection_Cutout_DepthNormals_NM.hlsl ----------------------------------------------------------

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
// #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Texture.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/Editor/ShaderGraph/Includes/ShaderPass.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/EncodeNormalsTexture.hlsl"

struct appdata
{
    float4 vertex : POSITION;
    float3 normal : NORMAL;
// Begin Injection VERTEX_IN from Injection_NormalMap_DepthNormals.hlsl ----------------------------------------------------------
	float4 tangent : TANGENT;
	float2 uv0 : TEXCOORD0;
// End Injection VERTEX_IN from Injection_NormalMap_DepthNormals.hlsl ----------------------------------------------------------
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct v2f
{
    float4 vertex : SV_POSITION;
    float4 normalWS : NORMAL;
// Begin Injection INTERPOLATORS from Injection_NormalMap_DepthNormals.hlsl ----------------------------------------------------------
	float4 tanXYZ_btSign : TEXCOORD0;
	float2 uv0XY : TEXCOORD1;
// End Injection INTERPOLATORS from Injection_NormalMap_DepthNormals.hlsl ----------------------------------------------------------
    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
};

#define UNPACK_NORMAL(i) i.normalWS.xyz
    
// Begin Injection UNIFORMS from Injection_NormalMap_DepthNormals.hlsl ----------------------------------------------------------
	TEXTURE2D(_BumpMap);
	SAMPLER(sampler_BumpMap);
// End Injection UNIFORMS from Injection_NormalMap_DepthNormals.hlsl ----------------------------------------------------------
// Begin Injection UNIFORMS from Injection_Cutout_DepthNormals_NM.hlsl ----------------------------------------------------------
	TEXTURE2D(_BaseMap);
	SAMPLER(sampler_BaseMap);
// End Injection UNIFORMS from Injection_Cutout_DepthNormals_NM.hlsl ----------------------------------------------------------

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

// Begin Injection VERTEX_NORMAL from Injection_NormalMap_DepthNormals.hlsl ----------------------------------------------------------
	half3 wNorm = (TransformObjectToWorldNormal(v.normal));
	half3 wTan = (TransformObjectToWorldDir(v.tangent.xyz));
	half tanSign = v.tangent.w * GetOddNegativeScale();
	o.normalWS = float4(wNorm, 1);
	o.tanXYZ_btSign = float4(wTan, tanSign);
	o.uv0XY.xy = TRANSFORM_TEX(v.uv0, _BaseMap);
// End Injection VERTEX_NORMAL from Injection_NormalMap_DepthNormals.hlsl ----------------------------------------------------------

    return o;
}

half4 frag(v2f i
    , bool frontFace : SV_IsFrontFace
) : SV_Target
{
   UNITY_SETUP_INSTANCE_ID(i);
   UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
    
    if (!frontFace)
    {
        UNPACK_NORMAL(i) = -UNPACK_NORMAL(i);
    }

// Begin Injection FRAG_BEGIN from Injection_Cutout_DepthNormals_NM.hlsl ----------------------------------------------------------
#if defined(_ALPHATEST_ON)
	float alpha = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv0XY.xy).a;
	clip((alpha * _BaseColor.a) - _Cutoff);
#endif
// End Injection FRAG_BEGIN from Injection_Cutout_DepthNormals_NM.hlsl ----------------------------------------------------------

   half4 normals = half4(0, 0, 0, 1);

// Begin Injection FRAG_NORMALS from Injection_NormalMap_DepthNormals.hlsl ----------------------------------------------------------
	half4 normalMap = SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, i.uv0XY.xy);
	half3 normalTS = UnpackNormal(normalMap);
	normalTS = _Normals ? normalTS : half3(0, 0, 1);

	half3 normalWS = i.normalWS.xyz;
	half3 tangentWS = i.tanXYZ_btSign.xyz;
	half3 bitangentWS = cross(normalWS, tangentWS) * i.tanXYZ_btSign.w;
	half3x3 TStoWS = half3x3(
		tangentWS.x, bitangentWS.x, normalWS.x,
		tangentWS.y, bitangentWS.y, normalWS.y,
		tangentWS.z, bitangentWS.z, normalWS.z
		);
	normalWS = mul(TStoWS, normalTS);
	normalWS = normalize(normalWS);

	normals = half4(EncodeWSNormalForNormalsTex(normalWS),0);
// End Injection FRAG_NORMALS from Injection_NormalMap_DepthNormals.hlsl ----------------------------------------------------------


    return normals;
}