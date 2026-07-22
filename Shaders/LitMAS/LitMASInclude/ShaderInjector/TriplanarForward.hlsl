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

// Begin Injection UNIVERSAL_DEFINES from Injection_Triplanar.hlsl ----------------------------------------------------------
	#pragma multi_compile_local_fragment _ _EXPENSIVE_TP

	#if defined(_EXPENSIVE_TP)
		#define SLZ_SAMPLE_TP_MAIN(tex, sampl, uv) SAMPLE_TEXTURE2D_GRAD(tex, sampl, uv, ddxMain, ddyMain)
		#define SLZ_SAMPLE_TP_DETAIL(tex, sampl, uv) SAMPLE_TEXTURE2D_GRAD(tex, sampl, uv, ddxDetail, ddyDetail)
	#else
		#define SLZ_SAMPLE_TP_MAIN(tex, sampl, uv) SAMPLE_TEXTURE2D(tex, sampl, uv)
		#define SLZ_SAMPLE_TP_DETAIL(tex, sampl, uv) SAMPLE_TEXTURE2D(tex, sampl, uv)
	#endif
// End Injection UNIVERSAL_DEFINES from Injection_Triplanar.hlsl ----------------------------------------------------------



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

// Begin Injection INCLUDES from Injection_Triplanar.hlsl ----------------------------------------------------------
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SLZTriplanar.hlsl"
// End Injection INCLUDES from Injection_Triplanar.hlsl ----------------------------------------------------------
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



// Begin Injection UNIFORMS from Injection_Triplanar.hlsl ----------------------------------------------------------
TEXTURE2D(_DetailMap);
SAMPLER(sampler_DetailMap);
// End Injection UNIFORMS from Injection_Triplanar.hlsl ----------------------------------------------------------
// Begin Injection UNIFORMS from Injection_Emission.hlsl ----------------------------------------------------------
TEXTURE2D(_EmissionMap);
// End Injection UNIFORMS from Injection_Emission.hlsl ----------------------------------------------------------

#if defined(CBUFFER_PATH)
#include CBUFFER_PATH
#endif


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

// Begin Injection VERTEX_NORMALS from Injection_Triplanar.hlsl ----------------------------------------------------------
	o.normXYZ_tanZ = half4(TransformObjectToWorldNormal(v.normal, false), v.tangent.z); //Avoid optimization that would remove the tangent from the vertex input (causes issues)
// End Injection VERTEX_NORMALS from Injection_Triplanar.hlsl ----------------------------------------------------------


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

// Begin Injection FRAG_READ_INPUTS from Injection_Triplanar.hlsl ----------------------------------------------------------

		/*-Triplanar---------------------------------------------------------------------------------------------------------*/

		float2 uvTP;
		half3x3 TStoWsTP;
		half2 scale = 1.0/_UVScaler;
		
		#if defined(_EXPENSIVE_TP)
			tpDerivatives tpDD;
			GetDirectionalDerivatives( UNPACK_WPOS(i), tpDD);
			half2 ddxTP, ddyTP;
			GetTPUVExpensive(uvTP, ddxTP, ddyTP, TStoWsTP, UNPACK_WPOS(i), normalize(UNPACK_NORMAL(i)), tpDD);
			ddxTP = _RotateUVs ? half2(-ddxTP.y, ddxTP.x) : ddxTP;
			ddyTP = _RotateUVs ? half2(-ddyTP.y, ddyTP.x) : ddyTP;
			half2 ddxMain = ddxTP * scale;
			half2 ddyMain = ddyTP * scale;
		#else
			GetTPUVCheap(uvTP, TStoWsTP, UNPACK_WPOS(i), normalize(UNPACK_NORMAL(i)));
		#endif
		
		uvTP = _RotateUVs ? float2(-uvTP.y, uvTP.x) : uvTP;
		float2 uv_main = mad(uvTP, scale, _BaseMap_ST.zw);
		half4 albedo = SLZ_SAMPLE_TP_MAIN(_BaseMap, sampler_BaseMap, uv_main);
		albedo.a = _Surface == 0 ? half(1.0) : albedo.a;
		half3 mas = SLZ_SAMPLE_TP_MAIN(_MetallicGlossMap, sampler_BaseMap, uv_main).rgb;

// End Injection FRAG_READ_INPUTS from Injection_Triplanar.hlsl ----------------------------------------------------------


    albedo *= _BaseColor;
    albedo.a = _Surface == 0 ? half(1.0) : albedo.a;
    half metallic = mas.r;
    half ao = mas.g;
    half smoothness = mas.b;


/*---------------------------------------------------------------------------------------------------------------------------*/
/*---Sample Normal Map-------------------------------------------------------------------------------------------------------*/
/*---------------------------------------------------------------------------------------------------------------------------*/

    half3 normalTS = half3(0, 0, 1);
    half  geoSmooth = 1;
    half4 normalMap = half4(0, 0, 1, 0);

// Begin Injection NORMAL_MAP from Injection_Triplanar.hlsl ----------------------------------------------------------

		/*-Triplanar Psuedo tangent space normals----------------------------------------------------------------------------*/
		normalMap = SLZ_SAMPLE_TP_MAIN(_BumpMap, sampler_BaseMap, uv_main);
		normalTS = UnpackNormal(normalMap);
		normalTS = _Normals ? normalTS : half3(0, 0, 1);
		normalTS = _RotateUVs ? half3(normalTS.y, -normalTS.x, normalTS.z) : normalTS;
		geoSmooth = _Normals ? 1.0 - normalMap.b : 1.0;
		smoothness = saturate(smoothness + geoSmooth - 1.0);

// End Injection NORMAL_MAP from Injection_Triplanar.hlsl ----------------------------------------------------------

/*---------------------------------------------------------------------------------------------------------------------------*/
/*---Read Detail Map---------------------------------------------------------------------------------------------------------*/
/*---------------------------------------------------------------------------------------------------------------------------*/

// Begin Injection DETAIL_MAP from Injection_Triplanar.hlsl ----------------------------------------------------------
		/*-Triplanar---------------------------------------------------------------------------------------------------------*/
		float2 uv_detail = mad(uvTP, _DetailMap_ST.xx, _DetailMap_ST.zw);
		uv_detail = _DetailsuseLocalUVs ? mad(float2(UNPACK_UV0(i)), _DetailMap_ST.xy, _DetailMap_ST.zw) : uv_detail;
#if defined(_EXPENSIVE_TP)
		half2 ddxDetail = ddx(uv_detail);
		half2 ddyDetail = ddy(uv_detail);
		ddxDetail = _DetailsuseLocalUVs ? ddxDetail : ddxTP * _DetailMap_ST.xx;
		ddyDetail = _DetailsuseLocalUVs ? ddyDetail : ddyTP * _DetailMap_ST.xx;
#endif
		half4 detailMap = SLZ_SAMPLE_TP_DETAIL(_DetailMap, sampler_DetailMap, uv_detail);
		half3 detailTS = half3(2.0 * detailMap.ag - 1.0, 1.0);
		detailTS = _RotateUVs && !(_DetailsuseLocalUVs) ? half3(detailTS.y, -detailTS.x, detailTS.z) : detailTS;
		normalTS = BlendNormal(normalTS, detailTS);
// End Injection DETAIL_MAP from Injection_Triplanar.hlsl ----------------------------------------------------------
    



/*---------------------------------------------------------------------------------------------------------------------------*/
/*---Transform Normals To Worldspace-----------------------------------------------------------------------------------------*/
/*---------------------------------------------------------------------------------------------------------------------------*/

// Begin Injection NORMAL_TRANSFORM from Injection_Triplanar.hlsl ----------------------------------------------------------

	/*-Triplanar-------------------------------------------------------------------------------------------------------------*/
	half3 normalWS = mul(TStoWsTP, normalTS);
	normalWS = normalize(normalWS);

// End Injection NORMAL_TRANSFORM from Injection_Triplanar.hlsl ----------------------------------------------------------


/*---------------------------------------------------------------------------------------------------------------------------*/
/*---Lighting Calculations---------------------------------------------------------------------------------------------------*/
/*---------------------------------------------------------------------------------------------------------------------------*/
    
    #if !defined(SHADER_API_MOBILE) && !defined(LITMAS_FEATURE_TP) // Specular antialiasing based on normal derivatives. Only on PC to avoid cost of derivatives on Quest
        //smoothness = min(smoothness, SLZGeometricSpecularAA(UNPACK_NORMAL(i)));
        smoothness = SLZGeometricNormalFiltering(smoothness, UNPACK_NORMAL(i), /*variance*/ 0.075, /*threshold*/ 0.2);
    #endif


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