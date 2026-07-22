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
#else              

// Begin Injection STANDALONE_DEFINES from Injection_SSR.hlsl ----------------------------------------------------------


// Previously was _SLZ_SSR_ENABLED, had to be inverted to support disabling SSR as a material property without
// needing the local _SSR_DISABLED shader feature keyword.
//
// Unity will not allow an enabled global keyword to be disabled by the material's local keyword state.
// This means that in order to disable SSR on a specific material another local keyword is neccessary,
// potentially doubling the shader size with an unnecessary duplicates of the programs for the global SSR
// off state.
//
// However, unity will override the local keyword state with the global state if the global is enabled but
// the local is not Thus, if we have SSR enabled be the default state, the material can enable the disabled
// keyword regardless of the global state

#pragma multi_compile _ _SLZ_SSR_DISABLED

#if !defined(_SLZ_SSR_DISABLED) && !defined(SHADER_API_MOBILE)
    #define _SSR_ENABLED
#endif
// End Injection STANDALONE_DEFINES from Injection_SSR.hlsl ----------------------------------------------------------

#endif

#if !defined(LITMAS_FEATURE_LIGHTMAPPING)
#define _DISABLE_LIGHTMAPS
#endif

#define UNITY_UNIFIED_SHADER_PRECISION_MODEL

#include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DefaultLitVariants.hlsl"

// Begin Injection UNIVERSAL_DEFINES from Injection_DetailMap.hlsl ----------------------------------------------------------
#pragma shader_feature_local_fragment _ _DETAILS_ON


#if defined(NO_FRACTAL_DETAILS)
    #if defined(_DETAILS_ON)
        #define _FRACTAL_DETAILS_OFF
    #endif
#else
    // phrase fractal details keyword as a negative so it can be disabled both locally and globally
    #pragma multi_compile_fragment _ _FRACTAL_DETAILS_OFF
#endif
// End Injection UNIVERSAL_DEFINES from Injection_DetailMap.hlsl ----------------------------------------------------------
// Begin Injection UNIVERSAL_DEFINES from Injection_Fluorescence.hlsl ----------------------------------------------------------
#pragma shader_feature_local_fragment _FLUORESCENCE
// End Injection UNIVERSAL_DEFINES from Injection_Fluorescence.hlsl ----------------------------------------------------------



#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SLZVkExtensions.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Texture.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/TextureStack.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ShaderGraphFunctions.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DBuffer.hlsl"
#include "Packages/com.unity.render-pipelines.universal/Editor/ShaderGraph/Includes/ShaderPass.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SLZLighting.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SLZBlueNoise.hlsl"

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/MobileAntibanding.hlsl"

// Begin Injection INCLUDES from Injection_DetailMap.hlsl ----------------------------------------------------------
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Detailmaps.hlsl"
// End Injection INCLUDES from Injection_DetailMap.hlsl ----------------------------------------------------------
// Begin Injection INCLUDES from Injection_Impacts_CBuffer.hlsl ----------------------------------------------------------
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/PosespaceImpacts.hlsl"
// End Injection INCLUDES from Injection_Impacts_CBuffer.hlsl ----------------------------------------------------------
// Begin Injection INCLUDES from Injection_Impacts.hlsl ----------------------------------------------------------
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/PosespaceImpacts.hlsl"
// End Injection INCLUDES from Injection_Impacts.hlsl ----------------------------------------------------------
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

// Begin Injection INTERPOLATORS from Injection_SSR.hlsl ----------------------------------------------------------
	float4 lastVertex : TEXCOORD5;
// End Injection INTERPOLATORS from Injection_SSR.hlsl ----------------------------------------------------------
// Begin Injection INTERPOLATORS from Injection_NormalMaps.hlsl ----------------------------------------------------------
	////#!TEXCOORD half4 tanXYZ_ 1
// End Injection INTERPOLATORS from Injection_NormalMaps.hlsl ----------------------------------------------------------
// Begin Injection INTERPOLATORS from Injection_Impacts.hlsl ----------------------------------------------------------
	float3 unskinnedObjPos : TEXCOORD6;
// End Injection INTERPOLATORS from Injection_Impacts.hlsl ----------------------------------------------------------

    UNITY_VERTEX_INPUT_INSTANCE_ID
        UNITY_VERTEX_OUTPUT_STEREO
};

#define UNPACK_UV0(i) i.uv0XY_tanXY.xy
#define UNPACK_NORMAL(i) i.normXYZ_tanZ.xyz
#define UNPACK_TANGENT(i) half3(i.uv0XY_tanXY.zw, i.normXYZ_tanZ.w)
#define UNPACK_BITANGENT_SIGN(i) i.SHVertLights_btSign.w
#define UNPACK_WPOS(i) i.wPos_fog.xyz
//#define UNPACK_FOG(i) i.wPos_fog.w
#define UNPACK_VERTLIGHTS(i) i.SHVertLights_btSign.xyz

TEXTURE2D(_BaseMap);
SAMPLER(sampler_BaseMap);

TEXTURE2D(_BumpMap);
TEXTURE2D(_MetallicGlossMap);



// Begin Injection UNIFORMS from Injection_DetailMap.hlsl ----------------------------------------------------------
TEXTURE2D(_DetailMap);
#if defined(_FRACTAL_DETAILS_OFF)
    SAMPLER(sampler_DetailMap);
#else
    #define sampler_DetailMap sampler_TrilinearRepeat
#endif
// End Injection UNIFORMS from Injection_DetailMap.hlsl ----------------------------------------------------------
// Begin Injection UNIFORMS from Injection_Emission.hlsl ----------------------------------------------------------
TEXTURE2D(_EmissionMap);
// End Injection UNIFORMS from Injection_Emission.hlsl ----------------------------------------------------------
// Begin Injection UNIFORMS from Injection_Fluorescence.hlsl ----------------------------------------------------------
#if defined(_FLUORESCENCE)
	TEXTURE2D(_FluorMap);
#endif
// End Injection UNIFORMS from Injection_Fluorescence.hlsl ----------------------------------------------------------

#if defined(CBUFFER_PATH)
#include CBUFFER_PATH
#endif

// Begin Injection FUNCTIONS from Injection_Impacts.hlsl ----------------------------------------------------------
half4 GetSSSColor()
{
	return _SSSColor;
}
// End Injection FUNCTIONS from Injection_Impacts.hlsl ----------------------------------------------------------

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
    // half clipZ_0Far = UNITY_Z_0_FAR_FROM_CLIPSPACE(o.vertex.z);
    // o.wPos_fog.w = unity_FogParams.x * clipZ_0Far;

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
// Begin Injection VERTEX_END from Injection_Impacts.hlsl ----------------------------------------------------------
	o.unskinnedObjPos = v.uv1.xyz;
// End Injection VERTEX_END from Injection_Impacts.hlsl ----------------------------------------------------------
    return o;
}

struct FragOut
{
    float4 color : SV_Target;
};

FragOut frag(VertOut i 
    , bool frontFace : SV_IsFrontFace
    SLZ_DECLARE_FRAG_SIZE
) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(i);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);

    RequestFragmentDensityEXT();
    SLZ_SETUP_FRAG_SIZE(i.vertex.xy);

    if (!frontFace)
    {
        UNPACK_NORMAL(i) = -UNPACK_NORMAL(i);
    }
/*---------------------------------------------------------------------------------------------------------------------------*/
/*---Read Input Data---------------------------------------------------------------------------------------------------------*/
/*---------------------------------------------------------------------------------------------------------------------------*/

    float2 uv0 = UNPACK_UV0(i);
    float2 uv_main = mad(uv0, _BaseMap_ST.xy, _BaseMap_ST.zw);
    half4 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv_main);
    half4 mas = SAMPLE_TEXTURE2D(_MetallicGlossMap, sampler_BaseMap, uv_main);

// Begin Injection FRAG_POST_READ from Injection_DetailMap.hlsl ----------------------------------------------------------
    float2 uv_detail = mad(uv0, _DetailMap_ST.xy, _DetailMap_ST.zw);
// End Injection FRAG_POST_READ from Injection_DetailMap.hlsl ----------------------------------------------------------
// Begin Injection FRAG_POST_READ from Injection_Fluorescence.hlsl ----------------------------------------------------------
#if defined(_FLUORESCENCE)
	half4 fluorMap = SAMPLE_TEXTURE2D(_FluorMap, sampler_BaseMap, uv_main);
#endif
// End Injection FRAG_POST_READ from Injection_Fluorescence.hlsl ----------------------------------------------------------

    albedo *= _BaseColor;
    albedo.a = _Surface == 0 ? half(1.0) : albedo.a;
    half metallic = mas.r;
    half ao = mas.g;
    half smoothness = mas.b;

// Begin Injection FRAG_POST_INPUTS from Injection_Impacts.hlsl ----------------------------------------------------------
	half2 impactUV = GetClosestImpactUV(i.unskinnedObjPos, EllipsoidPosArray, _NumberOfHits);
	half4 impactMASI = SampleHitTexture(impactUV);
	impactMASI.a = impactUV.x > 0.999 ? 0 : impactMASI.a;
	albedo = lerp(albedo, _HitColor, impactMASI.a);
	metallic = lerp(metallic, impactMASI.r, impactMASI.a);
	smoothness = lerp(smoothness, impactMASI.b, impactMASI.a);
	ao = min(ao, max(1.0 - impactMASI.a, impactMASI.g));
// End Injection FRAG_POST_INPUTS from Injection_Impacts.hlsl ----------------------------------------------------------

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

// Begin Injection DETAIL_MAP from Injection_DetailMap.hlsl ----------------------------------------------------------
    #if defined(_DETAILS_ON) && defined(_FRACTAL_DETAILS_OFF)
        BlendDetailMap( _DetailMap, sampler_DetailMap, uv_detail, albedo.rgb, smoothness, normalTS, _DetailNormalScale);
    #elif defined(_DETAILS_ON)
        BlendDetailMapFractal( _DetailMap, _BaseMap,  sampler_DetailMap,  uv_detail, uv_main, albedo.rgb, smoothness, normalTS, _DetailNormalScale);
    #endif
// End Injection DETAIL_MAP from Injection_DetailMap.hlsl ----------------------------------------------------------
// Begin Injection DETAIL_MAP from Injection_Fluorescence.hlsl ----------------------------------------------------------
#if defined(_FLUORESCENCE)
	half4 fluorescence = (fluorMap * _FluorColor) * half4(lerp(half3(1,1,1), albedo.rgb, _FluorAlbedoTint), 1.0);
	albedo.rgb = lerp(albedo.rgb, half3(0,0,0), _FluorAlbedoTint * fluorMap);
#endif
// End Injection DETAIL_MAP from Injection_Fluorescence.hlsl ----------------------------------------------------------
    



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
	normalWS = SafeNormalize(normalWS);
// End Injection NORMAL_TRANSFORM from Injection_NormalMaps.hlsl ----------------------------------------------------------


/*---------------------------------------------------------------------------------------------------------------------------*/
/*---Lighting Calculations---------------------------------------------------------------------------------------------------*/
/*---------------------------------------------------------------------------------------------------------------------------*/
    
// Begin Injection SPEC_AA from Injection_NormalMaps.hlsl ----------------------------------------------------------
	#if !defined(SHADER_API_MOBILE) && !defined(LITMAS_FEATURE_TP) // Specular antialiasing based on normal derivatives. Only on PC to avoid cost of derivatives on Quest
		//smoothness = min(smoothness, SLZGeometricSpecularAA(normalWS));
		smoothness = SLZGeometricNormalFiltering(smoothness, normalWS, /*variance*/ 0.1, /*threshold*/ 0.2);
	#endif
// End Injection SPEC_AA from Injection_NormalMaps.hlsl ----------------------------------------------------------


    #if defined(LIGHTMAP_ON)
        SLZFragData fragData = SLZGetFragData(i.vertex, UNPACK_WPOS(i), normalWS, i.uv1.xy, i.uv1.zw, UNPACK_VERTLIGHTS(i));
    #else
        SLZFragData fragData = SLZGetFragData(i.vertex, UNPACK_WPOS(i), normalWS, float2(0, 0), float2(0, 0), UNPACK_VERTLIGHTS(i));
    #endif
    #if defined(SHADER_API_MOBILE)
        half antibandingNoise = AntibandingNoise(i.vertex.xy);
    #endif

    half4 emission = half4(0,0,0,0);

// Begin Injection EMISSION from Injection_Emission.hlsl ----------------------------------------------------------
	UNITY_BRANCH if (_Emission)
	{
		emission += SAMPLE_TEXTURE2D(_EmissionMap, sampler_BaseMap, uv_main) * _EmissionColor;
		emission.rgb *= lerp(albedo.rgb, half3(1, 1, 1), emission.a);
		half emNoV = _EmissionFalloff >= half(0) ? abs(fragData.NoV) : half(1.0) - abs(fragData.NoV);
		emission.rgb *= saturate(pow(emNoV, abs(_EmissionFalloff)));
		emission = max(emission,half(0));
	}
// End Injection EMISSION from Injection_Emission.hlsl ----------------------------------------------------------


    SLZSurfData surfData = SLZGetSurfDataMetallicGloss(albedo.rgb, saturate(metallic), saturate(smoothness), ao, emission.rgb, albedo.a);
    half4 color = half4(1, 1, 1, 1);

// Begin Injection PRE_LIGHTING_CALC from Injection_Fluorescence.hlsl ----------------------------------------------------------
#if defined(_FLUORESCENCE)
	surfData.fluorescence = fluorescence;
	surfData.absorbance = _FluorAbsorbance;
#endif
// End Injection PRE_LIGHTING_CALC from Injection_Fluorescence.hlsl ----------------------------------------------------------

// Begin Injection LIGHTING_CALC from Injection_SSR.hlsl ----------------------------------------------------------
    #if defined(_SSR_ENABLED)
        float2 noiseScreenCoords = i.vertex.xy;
        half4 noiseRGBA = SSRGetInterleavedGradientNoise(noiseScreenCoords, _BlueNoise_Frame);

        SSRExtraData ssrExtra;
        ssrExtra.meshNormal = UNPACK_NORMAL(i);
        //ssrExtra.lastClipPos = i.lastVertex;
        //ssrExtra.temporalWeight = _SSRTemporalMul;
        ssrExtra.depthDerivativeSum = 0;
        ssrExtra.noise = noiseRGBA;
       // ssrExtra.fogFactor = UNPACK_FOG(i);
        ssrExtra.roughnessRange = half2(half(1.0) - _SSRSmoothnessRange.y, half(1.0) - _SSRSmoothnessRange.x);
        color = SLZPBRFragmentSSR(fragData, surfData, ssrExtra, _Surface);
        color.rgb = max(half(0), color.rgb);
    #else
        color = SLZPBRFragment(fragData, surfData, _Surface);
    #endif
// End Injection LIGHTING_CALC from Injection_SSR.hlsl ----------------------------------------------------------


// Begin Injection VOLUMETRIC_FOG from Injection_SSR.hlsl ----------------------------------------------------------
    #if !defined(_SSR_ENABLED)
      //  color = MixFogSurf(color, -fragData.viewDir, UNPACK_FOG(i), _Surface);
        
        color = VolumetricsSurf(color, fragData.position, _Surface);
    #endif
// End Injection VOLUMETRIC_FOG from Injection_SSR.hlsl ----------------------------------------------------------
    
    FragOut output = (FragOut) 0;
    output.color = color;
    
    #if defined(SHADER_API_MOBILE)
        // Don't do this for now, holding on to fragData.screenUV or i.vertex.xy occupies a full-precision register for the entire shader 
        ApplyInterleavedAntibanding(output.color.rgb, antibandingNoise);
    #endif
    

    return output;
}