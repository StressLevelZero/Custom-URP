/*-----------------------------------------------------------------------------------------------------*
 *-----------------------------------------------------------------------------------------------------*
 * WARNING: THIS FILE WAS CREATED WITH SHADERINJECTOR, AND SHOULD NOT BE EDITED DIRECTLY. MODIFY THE   *
 * BASE INCLUDE AND INJECTED FILES INSTEAD, AND REGENERATE!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!   *
 *-----------------------------------------------------------------------------------------------------*
 *-----------------------------------------------------------------------------------------------------*/


#define SHADERPASS SHADERPASS_FORWARD
#define _NORMAL_DROPOFF_TS 1
#define _EMISSION
#define _NORMALMAP 1

#if defined(SHADER_API_MOBILE)
	#define _ADDITIONAL_LIGHTS_VERTEX
#else              
	#pragma multi_compile_fragment  _  _MAIN_LIGHT_SHADOWS_CASCADE

	#define DYNAMIC_SCREEN_SPACE_OCCLUSION
	#pragma dynamic_branch _SCREEN_SPACE_OCCLUSION
	
#define DYNAMIC_ADDITIONAL_LIGHTS
#pragma dynamic_branch _ADDITIONAL_LIGHTS


#define DYNAMIC_ADDITIONAL_LIGHT_SHADOWS
#pragma dynamic_branch _ADDITIONAL_LIGHT_SHADOWS

	#define _SHADOWS_SOFT 1
	
	#define _REFLECTION_PROBE_BLENDING
	//#pragma shader_feature_fragment _REFLECTION_PROBE_BOX_PROJECTION
	// We don't need a keyword for this! the w component of the probe position already branches box vs non-box, & so little cost on pc it doesn't matter
	#define _REFLECTION_PROBE_BOX_PROJECTION 

// Begin Injection STANDALONE_DEFINES from Injection_SSR.hlsl ----------------------------------------------------------
#pragma multi_compile _ _SLZ_SSR_ENABLED
#pragma shader_feature_local _ _NO_SSR
#if defined(_SLZ_SSR_ENABLED) && !defined(_NO_SSR) && !defined(SHADER_API_MOBILE)
	#define _SSR_ENABLED
#endif
// End Injection STANDALONE_DEFINES from Injection_SSR.hlsl ----------------------------------------------------------

#endif

#pragma multi_compile_fragment _ _LIGHT_COOKIES
#pragma multi_compile _ SHADOWS_SHADOWMASK
#pragma multi_compile_fragment _ _VOLUMETRICS_ENABLED
#pragma multi_compile_fog
#pragma skip_variants FOG_LINEAR FOG_EXP
//#pragma multi_compile_fragment _ DEBUG_DISPLAY
#pragma multi_compile_fragment _ _DETAILS_ON
//#pragma multi_compile_fragment _ _EMISSION_ON


#if defined(LITMAS_FEATURE_LIGHTMAPPING)
	#pragma multi_compile _ LIGHTMAP_ON
	#pragma multi_compile _ DYNAMICLIGHTMAP_ON
	#pragma multi_compile _ DIRLIGHTMAP_COMBINED
	#pragma multi_compile _ LIGHTMAP_SHADOW_MIXING
#endif


#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Texture.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/TextureStack.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ShaderGraphFunctions.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DBuffer.hlsl"
#include "Packages/com.unity.render-pipelines.universal/Editor/ShaderGraph/Includes/ShaderPass.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SLZLighting.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SLZBlueNoise.hlsl"

// Begin Injection INCLUDES from Injection_SSR.hlsl ----------------------------------------------------------
#if !defined(SHADER_API_MOBILE)
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SLZLightingSSR.hlsl"
#endif
// End Injection INCLUDES from Injection_SSR.hlsl ----------------------------------------------------------



struct VertIn
{
	float4 vertex   : POSITION;
	float3 normal    : NORMAL;
	float4 tangent   : TANGENT;
	float4 uv0 : TEXCOORD0;
	float4 uv1 : TEXCOORD1;
	float4 uv2 : TEXCOORD2;
// Begin Injection VERTEX_IN from Injection_VertexColorAO.hlsl ----------------------------------------------------------
float4 color : COLOR;
// End Injection VERTEX_IN from Injection_VertexColorAO.hlsl ----------------------------------------------------------
	UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct VertOut
{
	float4 vertex       : SV_POSITION;
	float4 uv0XY_tanXY : TEXCOORD0;
#if defined(LIGHTMAP_ON) || defined(DYNAMICLIGHTMAP_ON)
	float4 uv1 : TEXCOORD1;
#endif
	half4 SHVertLights_btSign : TEXCOORD2;
	half4 normXYZ_tanZ : TEXCOORD3;
	float4 wPos_fog : TEXCOORD4;

// Begin Injection INTERPOLATORS from Injection_VertexColorAO.hlsl ----------------------------------------------------------
   float4 color : COLOR;
// End Injection INTERPOLATORS from Injection_VertexColorAO.hlsl ----------------------------------------------------------
// Begin Injection INTERPOLATORS from Injection_SSR.hlsl ----------------------------------------------------------
	float4 lastVertex : TEXCOORD5;
// End Injection INTERPOLATORS from Injection_SSR.hlsl ----------------------------------------------------------
// Begin Injection INTERPOLATORS from Injection_NormalMaps.hlsl ----------------------------------------------------------
	////#!TEXCOORD half4 tanXYZ_ 1
// End Injection INTERPOLATORS from Injection_NormalMaps.hlsl ----------------------------------------------------------

	UNITY_VERTEX_INPUT_INSTANCE_ID
		UNITY_VERTEX_OUTPUT_STEREO
};

#define UNPACK_UV0(i) i.uv0XY_tanXY.xy
#define UNPACK_NORMAL(i) i.normXYZ_tanZ.xyz
#define UNPACK_TANGENT(i) half3(i.uv0XY_tanXY.zw, i.normXYZ_tanZ.w)
#define UNPACK_BITANGENT_SIGN(i) i.SHVertLights_btSign.w
#define UNPACK_WPOS(i) i.wPos_fog.xyz
#define UNPACK_FOG(i) i.wPos_fog.w
#define UNPACK_VERTLIGHTS(i) i.SHVertLights_btSign.xyz

TEXTURE2D(_BaseMap);
SAMPLER(sampler_BaseMap);

TEXTURE2D(_BumpMap);
TEXTURE2D(_MetallicGlossMap);

TEXTURE2D(_DetailMap);
SAMPLER(sampler_DetailMap);

// Begin Injection UNIFORMS from Injection_Emission.hlsl ----------------------------------------------------------
TEXTURE2D(_EmissionMap);
// End Injection UNIFORMS from Injection_Emission.hlsl ----------------------------------------------------------

CBUFFER_START(UnityPerMaterial)
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
// Begin Injection MATERIAL_CBUFFER from Injection_Emission.hlsl ----------------------------------------------------------
	half  _Emission;
	half4 _EmissionColor;
	half  _EmissionFalloff;
	half  _BakedMutiplier;
// End Injection MATERIAL_CBUFFER from Injection_Emission.hlsl ----------------------------------------------------------
	int _Surface;
CBUFFER_END

half3 OverlayBlendDetail(half source, half3 destination)
{
	half3 switch0 = round(destination); // if destination >= 0.5 then 1, else 0 assuming 0-1 input
	half3 blendGreater = mad(mad(2.0, destination, -2.0), 1.0 - source, 1.0); // (2.0 * destination - 2.0) * ( 1.0 - source) + 1.0
	half3 blendLesser = (2.0 * source) * destination;
	return mad(switch0, blendGreater, mad(-switch0, blendLesser, blendLesser)); // switch0 * blendGreater + (1 - switch0) * blendLesser 
	//return half3(destination.r > 0.5 ? blendGreater.r : blendLesser.r,
	//             destination.g > 0.5 ? blendGreater.g : blendLesser.g,
	//             destination.b > 0.5 ? blendGreater.b : blendLesser.b
	//            );
}


VertOut vert(VertIn v)
{
	VertOut o = (VertOut)0;
	UNITY_SETUP_INSTANCE_ID(v);
	UNITY_TRANSFER_INSTANCE_ID(v, o);
	UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

	o.wPos_fog.xyz = TransformObjectToWorld(v.vertex.xyz);
	o.vertex = TransformWorldToHClip(o.wPos_fog.xyz);
	o.uv0XY_tanXY.xy = v.uv0.xy;

#if defined(LIGHTMAP_ON) || defined(DIRLIGHTMAP_COMBINED)
	OUTPUT_LIGHTMAP_UV(v.uv1.xy, unity_LightmapST, o.uv1.xy);
#endif

#ifdef DYNAMICLIGHTMAP_ON
	OUTPUT_LIGHTMAP_UV(v.uv2.xy, unity_DynamicLightmapST, o.uv1.zw);
#endif

	// Exp2 fog
	half clipZ_0Far = UNITY_Z_0_FAR_FROM_CLIPSPACE(o.vertex.z);
	o.wPos_fog.w = unity_FogParams.x * clipZ_0Far;

// Begin Injection VERTEX_NORMALS from Injection_NormalMaps.hlsl ----------------------------------------------------------
	//VertexNormalInputs ntb = GetVertexNormalInputs(v.normal, v.tangent);
	half3 wNorm = (TransformObjectToWorldNormal(v.normal));
	half3 wTan = (TransformObjectToWorldDir(v.tangent.xyz));
	half tanSign = v.tangent.w * GetOddNegativeScale();
	o.normXYZ_tanZ = half4(wNorm, wTan.z);
	o.uv0XY_tanXY.zw = wTan.xy;
	o.SHVertLights_btSign.w = tanSign;
// End Injection VERTEX_NORMALS from Injection_NormalMaps.hlsl ----------------------------------------------------------


	// Calculate vertex lights and L2 probe lighting on quest 
	o.SHVertLights_btSign.xyz = VertexLighting(UNPACK_WPOS(o), UNPACK_NORMAL(o));
#if !defined(LIGHTMAP_ON) && !defined(DYNAMICLIGHTMAP_ON) && defined(SHADER_API_MOBILE)
	o.SHVertLights_btSign.xyz += SampleSHVertex(o.normXYZ_tanZ.xyz);
#endif

// Begin Injection VERTEX_END from Injection_SSR.hlsl ----------------------------------------------------------
	//#if defined(_SSR_ENABLED)
	//	float4 lastWPos = mul(GetPrevObjectToWorldMatrix(), v.vertex);
	//	o.lastVertex = mul(prevVP, lastWPos);
	//#endif
// End Injection VERTEX_END from Injection_SSR.hlsl ----------------------------------------------------------
// Begin Injection VERTEX_END from Injection_VertexColorAO.hlsl ----------------------------------------------------------
	o.color = v.color;
// End Injection VERTEX_END from Injection_VertexColorAO.hlsl ----------------------------------------------------------
	return o;
}

half4 frag(VertOut i) : SV_Target
{
	UNITY_SETUP_INSTANCE_ID(i);
	UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);

/*---------------------------------------------------------------------------------------------------------------------------*/
/*---Read Input Data---------------------------------------------------------------------------------------------------------*/
/*---------------------------------------------------------------------------------------------------------------------------*/

	float2 uv0 = UNPACK_UV0(i);
	float2 uv_main = mad(uv0, _BaseMap_ST.xy, _BaseMap_ST.zw);
	float2 uv_detail = mad(uv0, _DetailMap_ST.xy, _DetailMap_ST.zw);
	half4 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv_main);
	half4 mas = SAMPLE_TEXTURE2D(_MetallicGlossMap, sampler_BaseMap, uv_main);



// Begin Injection PBR_VALUES from Injection_VertexColorAO.hlsl ----------------------------------------------------------
	albedo *= lerp(1, _BaseColor, albedo.a);
	half metallic = mas.r;
	half ao = mas.g;
	half smoothness = mas.b;
// End Injection PBR_VALUES from Injection_VertexColorAO.hlsl ----------------------------------------------------------


/*---------------------------------------------------------------------------------------------------------------------------*/
/*---Sample Normal Map-------------------------------------------------------------------------------------------------------*/
/*---------------------------------------------------------------------------------------------------------------------------*/

	half3 normalTS = half3(0, 0, 1);
	half  geoSmooth = 1;
	half4 normalMap = half4(0, 0, 1, 0);

// Begin Injection NORMAL_MAP from Injection_NormalMaps.hlsl ----------------------------------------------------------
	normalMap = SAMPLE_TEXTURE2D(_BumpMap, sampler_BaseMap, uv_main);
	normalTS = UnpackNormal(normalMap);
	normalTS = _Normals ? normalTS : half3(0, 0, 1);
	geoSmooth = _Normals ? 1.0 - normalMap.b : 1.0;
	smoothness = saturate(smoothness + geoSmooth - 1.0);
// End Injection NORMAL_MAP from Injection_NormalMaps.hlsl ----------------------------------------------------------

/*---------------------------------------------------------------------------------------------------------------------------*/
/*---Read Detail Map---------------------------------------------------------------------------------------------------------*/
/*---------------------------------------------------------------------------------------------------------------------------*/

	#if defined(_DETAILS_ON) 

// Begin Injection DETAIL_MAP from Injection_NormalMaps.hlsl ----------------------------------------------------------
		half4 detailMap = SAMPLE_TEXTURE2D(_DetailMap, sampler_DetailMap, uv_detail);
		half3 detailTS = UnpackNormalAG(detailMap);
		normalTS = normalize(BlendNormalRNM(normalTS, detailTS));
// End Injection DETAIL_MAP from Injection_NormalMaps.hlsl ----------------------------------------------------------
	   
		smoothness = saturate(2.0 * detailMap.b * smoothness);
		albedo.rgb = OverlayBlendDetail(detailMap.r, albedo.rgb);

	#endif


/*---------------------------------------------------------------------------------------------------------------------------*/
/*---Transform Normals To Worldspace-----------------------------------------------------------------------------------------*/
/*---------------------------------------------------------------------------------------------------------------------------*/

// Begin Injection NORMAL_TRANSFORM from Injection_NormalMaps.hlsl ----------------------------------------------------------
	half3 normalWS = UNPACK_NORMAL(i);
	half3 tangentWS = UNPACK_TANGENT(i);
	half3 bitangentWS = cross(normalWS, tangentWS) * UNPACK_BITANGENT_SIGN(i);
	
	half3x3 TStoWS = half3x3(
		tangentWS.x, bitangentWS.x, normalWS.x,
		tangentWS.y, bitangentWS.y, normalWS.y,
		tangentWS.z, bitangentWS.z, normalWS.z
		);
	normalWS = mul(TStoWS, normalTS);
	normalWS = normalize(normalWS);
// End Injection NORMAL_TRANSFORM from Injection_NormalMaps.hlsl ----------------------------------------------------------


/*---------------------------------------------------------------------------------------------------------------------------*/
/*---Lighting Calculations---------------------------------------------------------------------------------------------------*/
/*---------------------------------------------------------------------------------------------------------------------------*/
	
// Begin Injection SPEC_AA from Injection_NormalMaps.hlsl ----------------------------------------------------------
	#if !defined(SHADER_API_MOBILE) && !defined(LITMAS_FEATURE_TP) // Specular antialiasing based on normal derivatives. Only on PC to avoid cost of derivatives on Quest
		smoothness = min(smoothness, SLZGeometricSpecularAA(normalWS));
	#endif
// End Injection SPEC_AA from Injection_NormalMaps.hlsl ----------------------------------------------------------

// Begin Injection PRE_FRAGDATA from Injection_VertexColorAO.hlsl ----------------------------------------------------------
	ao *= i.color.a;
	albedo.rgb *= i.color.rgb;
// End Injection PRE_FRAGDATA from Injection_VertexColorAO.hlsl ----------------------------------------------------------

	#if defined(LIGHTMAP_ON)
		SLZFragData fragData = SLZGetFragData(i.vertex, UNPACK_WPOS(i), normalWS, i.uv1.xy, i.uv1.zw, UNPACK_VERTLIGHTS(i));
	#else
		SLZFragData fragData = SLZGetFragData(i.vertex, UNPACK_WPOS(i), normalWS, float2(0, 0), float2(0, 0), UNPACK_VERTLIGHTS(i));
	#endif

	half4 emission = half4(0,0,0,0);

// Begin Injection EMISSION from Injection_Emission.hlsl ----------------------------------------------------------
	UNITY_BRANCH if (_Emission)
	{
		emission += SAMPLE_TEXTURE2D(_EmissionMap, sampler_BaseMap, uv_main) * _EmissionColor;
		emission.rgb *= lerp(albedo.rgb, half3(1, 1, 1), emission.a);
		emission.rgb *= pow(abs(fragData.NoV), _EmissionFalloff);
	}
// End Injection EMISSION from Injection_Emission.hlsl ----------------------------------------------------------


	SLZSurfData surfData = SLZGetSurfDataMetallicGloss(albedo.rgb, saturate(metallic), saturate(smoothness), ao, emission.rgb, albedo.a);
	half4 color = half4(1, 1, 1, 1);


// Begin Injection LIGHTING_CALC from Injection_SSR.hlsl ----------------------------------------------------------
	#if defined(_SSR_ENABLED)
		half4 noiseRGBA = GetScreenNoiseRGBA(fragData.screenUV);

		SSRExtraData ssrExtra;
		ssrExtra.meshNormal = UNPACK_NORMAL(i);
		//ssrExtra.lastClipPos = i.lastVertex;
		ssrExtra.temporalWeight = _SSRTemporalMul;
		ssrExtra.depthDerivativeSum = 0;
		ssrExtra.noise = noiseRGBA;
		ssrExtra.fogFactor = UNPACK_FOG(i);

		color = SLZPBRFragmentSSR(fragData, surfData, ssrExtra, _Surface);
		color.rgb = max(0, color.rgb);
	#else
		color = SLZPBRFragment(fragData, surfData, _Surface);
	#endif
// End Injection LIGHTING_CALC from Injection_SSR.hlsl ----------------------------------------------------------


// Begin Injection VOLUMETRIC_FOG from Injection_SSR.hlsl ----------------------------------------------------------
	#if !defined(_SSR_ENABLED)
		color = MixFogSurf(color, -fragData.viewDir, UNPACK_FOG(i), _Surface);
		
		color = VolumetricsSurf(color, fragData.position, _Surface);
	#endif
// End Injection VOLUMETRIC_FOG from Injection_SSR.hlsl ----------------------------------------------------------
	return color;
}