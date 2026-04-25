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


#endif

#if !defined(LITMAS_FEATURE_LIGHTMAPPING)
#define _DISABLE_LIGHTMAPS
#endif

#define UNITY_UNIFIED_SHADER_PRECISION_MODEL

#include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DefaultLitVariants.hlsl"




#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

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

// Begin Injection INCLUDES from Injection_Layered.hlsl ----------------------------------------------------------
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Layering.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SLZTriplanar.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/BicubicFilter.hlsl"
// End Injection INCLUDES from Injection_Layered.hlsl ----------------------------------------------------------


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

// Begin Injection INTERPOLATORS from Injection_Layered.hlsl ----------------------------------------------------------
	float2 uv_splat : TEXCOORD5;
// End Injection INTERPOLATORS from Injection_Layered.hlsl ----------------------------------------------------------

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



// Begin Injection UNIFORMS from Injection_Layered.hlsl ----------------------------------------------------------
TEXTURE2D(_SplatMap);
SAMPLER(sampler_SplatMap);

Texture2D<min16float> _HeightMap;
Texture2D<min16float> _HeightMap1;
Texture2D<min16float> _HeightMap2;
Texture2D<min16float> _HeightMap3;
Texture2D<min16float> _HeightMap4;

SAMPLER(sampler_HeightMap);

TEXTURE2D(_BaseMap1);
TEXTURE2D(_BaseMap2);
TEXTURE2D(_BaseMap3);
TEXTURE2D(_BaseMap4);

TEXTURE2D(_MetallicGlossMap1);
TEXTURE2D(_MetallicGlossMap2);
TEXTURE2D(_MetallicGlossMap3);
TEXTURE2D(_MetallicGlossMap4);

TEXTURE2D(_BumpMap1);
TEXTURE2D(_BumpMap2);
TEXTURE2D(_BumpMap3);
TEXTURE2D(_BumpMap4);

// End Injection UNIFORMS from Injection_Layered.hlsl ----------------------------------------------------------

#if defined(CBUFFER_PATH)
#include CBUFFER_PATH
#endif

// Begin Injection FUNCTIONS from Injection_Layered.hlsl ----------------------------------------------------------

#define SAMPLE_LAYERED(outp, swizzle, tex, sampler_tex, uv, index, dx, dy) 									\
	[forcecase] switch (clamp(index, min16int(0), min16int(4))) 											\
	{ 																										\
		case min16int(0): outp = SAMPLE_TEXTURE2D_GRAD(tex,    sampler_tex, uv, dx, dy). swizzle ; break;	\
		case min16int(1): outp = SAMPLE_TEXTURE2D_GRAD(tex##1, sampler_tex, uv, dx, dy). swizzle ; break;	\
		case min16int(2): outp = SAMPLE_TEXTURE2D_GRAD(tex##2, sampler_tex, uv, dx, dy). swizzle ; break;	\
		case min16int(3): outp = SAMPLE_TEXTURE2D_GRAD(tex##3, sampler_tex, uv, dx, dy). swizzle ; break;	\
		case min16int(4): outp = SAMPLE_TEXTURE2D_GRAD(tex##4, sampler_tex, uv, dx, dy). swizzle ; break;	\
	}																										\

#define LAYER_UVS(index, iuv, idx, idy) \
	[forcecase] switch (clamp(index, min16int(0), min16int(4))) 																		\
	{ 																																	\
		case min16int(0): iuv = _BaseMap_ST.xy  * uv0 +  _BaseMap_ST.zw; idx = dx *  _BaseMap_ST.xy; idy = dy *  _BaseMap_ST.xy; break; \
		case min16int(1): iuv = _BaseMap1_ST.xy * uv0 + _BaseMap1_ST.zw; idx = dx * _BaseMap1_ST.xy; idy = dy * _BaseMap1_ST.xy; break;	\
		case min16int(2): iuv = _BaseMap2_ST.xy * uv0 + _BaseMap2_ST.zw; idx = dx * _BaseMap2_ST.xy; idy = dy * _BaseMap2_ST.xy; break;	\
		case min16int(3): iuv = _BaseMap3_ST.xy * uv0 + _BaseMap3_ST.zw; idx = dx * _BaseMap3_ST.xy; idy = dy * _BaseMap3_ST.xy; break;	\
		case min16int(4): iuv = _BaseMap4_ST.xy * uv0 + _BaseMap4_ST.zw; idx = dx * _BaseMap4_ST.xy; idy = dy * _BaseMap4_ST.xy; break;	\
	}																																	\

// End Injection FUNCTIONS from Injection_Layered.hlsl ----------------------------------------------------------

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

// Begin Injection VERTEX_NORMALS from Injection_Layered.hlsl ----------------------------------------------------------
	//VertexNormalInputs ntb = GetVertexNormalInputs(v.normal, v.tangent);
	half3 wNorm = (TransformObjectToWorldNormal(v.normal));
	half3 wTan = (TransformObjectToWorldDir(v.tangent.xyz));
	half tanSign = v.tangent.w * GetOddNegativeScale();
	o.normXYZ_tanZ = half4(wNorm, wTan.z);
	o.uv0XY_tanXY.zw = wTan.xy;
	o.SHVertLights_btSign.w = tanSign;
// End Injection VERTEX_NORMALS from Injection_Layered.hlsl ----------------------------------------------------------


    // Calculate vertex lights and L2 probe lighting on quest 
    o.SHVertLights_btSign.xyz = VertexLighting(UNPACK_WPOS(o), UNPACK_NORMAL(o));
#if !defined(LIGHTMAP_ON) && !defined(DYNAMICLIGHTMAP_ON) && defined(SHADER_API_MOBILE)
    o.SHVertLights_btSign.xyz += SampleSHVertex(o.normXYZ_tanZ.xyz);
#endif

// Begin Injection VERTEX_END from Injection_Layered.hlsl ----------------------------------------------------------
	o.uv_splat = (v.uv1 - _LightmapScaleOffset.zw) / _LightmapScaleOffset.xy;
	if (_UseGRID != 0)
	{
		half3x3 tan2Wrld;
		GetTPUVCheap(UNPACK_UV0(o), tan2Wrld, UNPACK_WPOS(o), UNPACK_NORMAL(o));
	}
// End Injection VERTEX_END from Injection_Layered.hlsl ----------------------------------------------------------
    return o;
}

struct FragOut
{
    float4 color : SV_Target;
};

FragOut frag(VertOut i 
    , bool frontFace : SV_IsFrontFace
) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(i);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);

    if (!frontFace)
    {
        UNPACK_NORMAL(i) = -UNPACK_NORMAL(i);
    }
/*---------------------------------------------------------------------------------------------------------------------------*/
/*---Read Input Data---------------------------------------------------------------------------------------------------------*/
/*---------------------------------------------------------------------------------------------------------------------------*/

// Begin Injection FRAG_READ_INPUTS from Injection_Layered.hlsl ----------------------------------------------------------

float2 uv2 = i.uv_splat;
float2 splatDim;
_SplatMap.GetDimensions(splatDim.x, splatDim.y);
//float4 stupidUnityTexelSize = float4(rcp(width), rcp(height), width, height);
//uv2 = IQTextureNiceUVDistort(uv2, splatDim);
half4 layerWeights = SAMPLE_TEXTURE2D(_SplatMap, sampler_SplatMap, uv2);
//half4 layerWeights = SampleBSplineRGBA_LOD(_SplatMap, sampler_SplatMap, uv2, stupidUnityTexelSize);
half baseWeight = saturate(1.0 - (layerWeights.x + layerWeights.y + layerWeights.z + layerWeights.w));

SLZ::Layering::Layer layers[3];
SLZ::Layering::GetThreeActiveLayers(baseWeight, layerWeights, layers);

float2 uv0 = UNPACK_UV0(i);
half2 dx = ddx(uv0);
half2 dy = ddy(uv0);


half layerHeight0 = 0;
float2 uv_height; half2 dx_height, dy_height;
LAYER_UVS(layers[0].index, uv_height, dx_height, dy_height);
SAMPLE_LAYERED(layerHeight0, r, _HeightMap, sampler_HeightMap, uv_height, layers[0].index, dx_height, dy_height);
layers[0].weight *= layerHeight0;

if (layers[1].weight > HALF_MIN)
{
	half layerHeight1 = 0;
	LAYER_UVS(layers[1].index, uv_height, dx_height, dy_height);
	SAMPLE_LAYERED(layerHeight1, r, _HeightMap, sampler_HeightMap, uv_height, layers[1].index, dx_height, dy_height);
	layers[1].weight *= layerHeight1;
}

if (layers[2].weight > HALF_MIN)
{
	half layerHeight2 = 0;
	LAYER_UVS(layers[2].index, uv_height, dx_height, dy_height);
	SAMPLE_LAYERED(layerHeight2, r, _HeightMap, sampler_HeightMap, uv_height, layers[2].index, dx_height, dy_height);
	layers[2].weight *= layerHeight2;
}

// sort the layers, pick the two most important
if (layers[0].weight < layers[2].weight) SLZ::Layering::Swap(layers, 0, 2);
if (layers[0].weight < layers[1].weight) SLZ::Layering::Swap(layers, 0, 1);
if (layers[1].weight < layers[2].weight) SLZ::Layering::Swap(layers, 1, 2);

// Sort the indicies so we don't get divergence when sampling the same textures
if (layers[0].index > layers[1].index) SLZ::Layering::Swap(layers, 0, 1);

SLZ::Layering::Layer layer0 = layers[0];
SLZ::Layering::Layer layer1 = layers[1];

layer0.weight += HALF_MIN;

half layerWeightTotal = layer0.weight + layer1.weight;

layer0.weight /= layerWeightTotal;
layer1.weight /= layerWeightTotal;

half4 albedo = (half4)0;
half4 mas = (half4)0;
half4 normalMap2 = (half4)0;
float2 uv_layer0; half2 dx_layer0, dy_layer0;
LAYER_UVS(layers[0].index, uv_layer0, dx_layer0, dy_layer0);
float2 uv_layer1; half2 dx_layer1, dy_layer1;
LAYER_UVS(layers[1].index, uv_layer1, dx_layer1, dy_layer1);

if (layer0.weight > 2 * HALF_MIN)
{
	SAMPLE_LAYERED(albedo, rgba, _BaseMap, sampler_BaseMap, uv_layer0, layer0.index, dx_layer0, dy_layer0)
	albedo *= layer0.weight;
	
	SAMPLE_LAYERED(mas, rgba, _MetallicGlossMap, sampler_BaseMap, uv_layer0, layer0.index, dx_layer0, dy_layer0)
	mas *= layer0.weight;

	SAMPLE_LAYERED(normalMap2, rgba, _BumpMap, sampler_BaseMap, uv_layer0, layer0.index, dx_layer0, dy_layer0)
	normalMap2 *= layer0.weight;
}

if (layer1.weight > 2 * HALF_MIN)
{
	half4 albedo1 = (half4)0;
	SAMPLE_LAYERED(albedo1, rgba, _BaseMap, sampler_BaseMap, uv_layer1, layer1.index, dx_layer1, dy_layer1)
	albedo += albedo1 * layer1.weight;

	half4 mas1 = (half4)0;
	SAMPLE_LAYERED(mas1, rgba, _MetallicGlossMap, sampler_BaseMap, uv_layer1, layer1.index, dx_layer1, dy_layer1)
	mas += mas1 * layer1.weight;

	half4 normalMap1 = 0;
	SAMPLE_LAYERED(normalMap1, rgba, _BumpMap, sampler_BaseMap, uv_layer1, layer1.index, dx_layer1, dy_layer1)
	normalMap2 += normalMap1 * layer1.weight;
}


// End Injection FRAG_READ_INPUTS from Injection_Layered.hlsl ----------------------------------------------------------


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

// Begin Injection NORMAL_MAP from Injection_Layered.hlsl ----------------------------------------------------------
	normalMap = normalMap2;
	normalTS = UnpackNormal(normalMap);
	normalTS = _Normals ? normalTS : half3(0, 0, 1);
	geoSmooth = _Normals ? 1.0 - normalMap.b : 1.0;
	smoothness = saturate(smoothness + geoSmooth - 1.0);
// End Injection NORMAL_MAP from Injection_Layered.hlsl ----------------------------------------------------------

/*---------------------------------------------------------------------------------------------------------------------------*/
/*---Read Detail Map---------------------------------------------------------------------------------------------------------*/
/*---------------------------------------------------------------------------------------------------------------------------*/

    



/*---------------------------------------------------------------------------------------------------------------------------*/
/*---Transform Normals To Worldspace-----------------------------------------------------------------------------------------*/
/*---------------------------------------------------------------------------------------------------------------------------*/

// Begin Injection NORMAL_TRANSFORM from Injection_Layered.hlsl ----------------------------------------------------------
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
// End Injection NORMAL_TRANSFORM from Injection_Layered.hlsl ----------------------------------------------------------


/*---------------------------------------------------------------------------------------------------------------------------*/
/*---Lighting Calculations---------------------------------------------------------------------------------------------------*/
/*---------------------------------------------------------------------------------------------------------------------------*/
    
// Begin Injection SPEC_AA from Injection_Layered.hlsl ----------------------------------------------------------
	#if !defined(SHADER_API_MOBILE) && !defined(LITMAS_FEATURE_TP) // Specular antialiasing based on normal derivatives. Only on PC to avoid cost of derivatives on Quest
		//smoothness = min(smoothness, SLZGeometricSpecularAA(normalWS));
		smoothness = SLZGeometricNormalFiltering(smoothness, normalWS, /*variance*/ 0.1, /*threshold*/ 0.2);
	#endif
// End Injection SPEC_AA from Injection_Layered.hlsl ----------------------------------------------------------


    #if defined(LIGHTMAP_ON)
        SLZFragData fragData = SLZGetFragData(i.vertex, UNPACK_WPOS(i), normalWS, i.uv1.xy, i.uv1.zw, UNPACK_VERTLIGHTS(i));
    #else
        SLZFragData fragData = SLZGetFragData(i.vertex, UNPACK_WPOS(i), normalWS, float2(0, 0), float2(0, 0), UNPACK_VERTLIGHTS(i));
    #endif
    #if defined(SHADER_API_MOBILE)
        half antibandingNoise = AntibandingNoise(i.vertex.xy);
    #endif

    half4 emission = half4(0,0,0,0);



    SLZSurfData surfData = SLZGetSurfDataMetallicGloss(albedo.rgb, saturate(metallic), saturate(smoothness), ao, emission.rgb, albedo.a);
    half4 color = half4(1, 1, 1, 1);


        color = SLZPBRFragment(fragData, surfData, _Surface);


    //color = MixFogSurf(color, -fragData.viewDir, UNPACK_FOG(i), _Surface);
    color = VolumetricsSurf(color, fragData.position, _Surface);
    
    FragOut output = (FragOut) 0;
    output.color = color;
    
    #if defined(SHADER_API_MOBILE)
        // Don't do this for now, holding on to fragData.screenUV or i.vertex.xy occupies a full-precision register for the entire shader 
        ApplyInterleavedAntibanding(output.color.rgb, antibandingNoise);
    #endif
    

    return output;
}