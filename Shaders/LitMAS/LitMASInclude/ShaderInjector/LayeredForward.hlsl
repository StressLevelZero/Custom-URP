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

// Begin Injection DEFAULT_TEXTURES from Injection_Layered.hlsl ----------------------------------------------------------
Texture2D<min16float4> _BaseMap;
SAMPLER(sampler_BaseMap);

TEXTURE2D(_BumpMap);
Texture2D<min16float3> _MetallicGlossMap;
// End Injection DEFAULT_TEXTURES from Injection_Layered.hlsl ----------------------------------------------------------



// Begin Injection UNIFORMS from Injection_Layered.hlsl ----------------------------------------------------------
TEXTURE2D(_SplatMap);
SAMPLER(sampler_SplatMap);

#if defined(SHADER_API_MOBILE)
#define SAMPLER_SPLAT sampler_LinearRepeat
#define SAMPLER_CHEAP sampler_TrilinearRepeat
#else
#define SAMPLER_SPLAT sampler_SplatMap
#define SAMPLER_CHEAP sampler_BaseMap
#endif

Texture2D<min16float> _HeightMap;
Texture2D<min16float> _HeightMap1;
Texture2D<min16float> _HeightMap2;
Texture2D<min16float> _HeightMap3;
Texture2D<min16float> _HeightMap4;

SAMPLER(sampler_HeightMap);

Texture2D<min16float4> _BaseMap1;
Texture2D<min16float4> _BaseMap2;
Texture2D<min16float4> _BaseMap3;
Texture2D<min16float4> _BaseMap4;

Texture2D<min16float4> _AYSXMap;
Texture2D<min16float4> _AYSXMap1;
Texture2D<min16float4> _AYSXMap2;
Texture2D<min16float4> _AYSXMap3;
Texture2D<min16float4> _AYSXMap4;

//Texture2D<min16float3> _MetallicGlossMap1;
//Texture2D<min16float3> _MetallicGlossMap2;
//Texture2D<min16float3> _MetallicGlossMap3;
//Texture2D<min16float3> _MetallicGlossMap4;
//
//TEXTURE2D(_BumpMap1);
//TEXTURE2D(_BumpMap2);
//TEXTURE2D(_BumpMap3);
//TEXTURE2D(_BumpMap4);

// End Injection UNIFORMS from Injection_Layered.hlsl ----------------------------------------------------------

#if defined(CBUFFER_PATH)
#include CBUFFER_PATH
#endif

// Begin Injection FUNCTIONS from Injection_Layered.hlsl ----------------------------------------------------------



#define SAMPLE_LAYERED(outp, swizzle, tex, sampler_tex, uv, index, dx, dy) 						\
	[forcecase] switch (index) 											                        \
	{ 																							\
		case 0: outp = SAMPLE_TEXTURE2D_GRAD(tex,    sampler_tex, uv, dx, dy). swizzle ; break;	\
		case 1: outp = SAMPLE_TEXTURE2D_GRAD(tex##1, sampler_tex, uv, dx, dy). swizzle ; break;	\
		case 2: outp = SAMPLE_TEXTURE2D_GRAD(tex##2, sampler_tex, uv, dx, dy). swizzle ; break;	\
		case 3: outp = SAMPLE_TEXTURE2D_GRAD(tex##3, sampler_tex, uv, dx, dy). swizzle ; break;	\
		case 4: outp = SAMPLE_TEXTURE2D_GRAD(tex##4, sampler_tex, uv, dx, dy). swizzle ; break;	\
	}																						    \

#define LAYER_UVS(index, iuv, idx, idy)                                                                                         \
	[forcecase] switch (index) 																				    				\
	{ 																														    \
		case 0: iuv = _BaseMap_ST.xy  * uv0 +  _BaseMap_ST.zw; idx = dx *  _BaseMap_ST.xy; idy = dy *  _BaseMap_ST.xy; break;   \
		case 1: iuv = _BaseMap1_ST.xy * uv0 + _BaseMap1_ST.zw; idx = dx * _BaseMap1_ST.xy; idy = dy * _BaseMap1_ST.xy; break;	\
		case 2: iuv = _BaseMap2_ST.xy * uv0 + _BaseMap2_ST.zw; idx = dx * _BaseMap2_ST.xy; idy = dy * _BaseMap2_ST.xy; break;	\
		case 3: iuv = _BaseMap3_ST.xy * uv0 + _BaseMap3_ST.zw; idx = dx * _BaseMap3_ST.xy; idy = dy * _BaseMap3_ST.xy; break;	\
		case 4: iuv = _BaseMap4_ST.xy * uv0 + _BaseMap4_ST.zw; idx = dx * _BaseMap4_ST.xy; idy = dy * _BaseMap4_ST.xy; break;	\
	}																															\

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
#define _Surface 0
float2 uv2 = i.uv_splat;
//float2 splatDim;
//_SplatMap.GetDimensions(splatDim.x, splatDim.y);
//float4 stupidUnityTexelSize = float4(rcp(width), rcp(height), width, height);
//uv2 = IQTextureNiceUVDistort(uv2, splatDim);
half4 layerWeights = SAMPLE_TEXTURE2D(_SplatMap, sampler_SplatMap, uv2);
//half4 layerWeights = SampleBSplineRGBA_LOD(_SplatMap, sampler_SplatMap, uv2, stupidUnityTexelSize);


if ((layerWeights.x < layerWeights.y && layerWeights.x < layerWeights.z) ||
	(layerWeights.x < layerWeights.y && layerWeights.x < layerWeights.w) ||
	(layerWeights.x < layerWeights.z && layerWeights.x < layerWeights.w)
	)
	{layerWeights.x = 0;}

if ((layerWeights.y < layerWeights.x && layerWeights.y < layerWeights.z) ||
	(layerWeights.y < layerWeights.x && layerWeights.y < layerWeights.w) ||
	(layerWeights.y < layerWeights.z && layerWeights.y < layerWeights.w)
	)
	{layerWeights.y = 0;}

if ((layerWeights.z < layerWeights.x && layerWeights.z < layerWeights.y) ||
	(layerWeights.z < layerWeights.x && layerWeights.z < layerWeights.w) ||
	(layerWeights.z < layerWeights.y && layerWeights.z < layerWeights.w)
	)
	{layerWeights.z = 0;}

if ((layerWeights.w < layerWeights.x && layerWeights.w < layerWeights.y) ||
	(layerWeights.w < layerWeights.x && layerWeights.w < layerWeights.z) ||
	(layerWeights.w < layerWeights.y && layerWeights.w < layerWeights.z)
	)
	{layerWeights.w = 0;}

half splatWeightSum = layerWeights.x + layerWeights.y + layerWeights.z + layerWeights.w;
half baseWeight = saturate(half(1.0) - splatWeightSum);

float2 uv0 = UNPACK_UV0(i);
half2 dx = 0;
half2 dy = 0;

dx = (half2)ddx(uv0);
dy = (half2)ddy(uv0);
half weightSum = (half)0;
half3 albedoSum = (half3)0;
half aoSum = (half)0;
half smoothnessSum = (half)0;
half2 hOctSum = (half2)0;

//if (baseWeight > 0)
{
	const int layer0idx = 0;
	float2 layer0uv; half2 layer0dx, layer0dy;
	LAYER_UVS(layer0idx, layer0uv, layer0dx, layer0dy);
	half layer0Height = 0;
	SAMPLE_LAYERED(layer0Height, r, _HeightMap, SAMPLER_CHEAP, layer0uv, layer0idx, layer0dx, layer0dy);
	layer0Height = saturate(layer0Height - layerWeights.x - layerWeights.y - layerWeights.z - layerWeights.w) + 0.01;
	weightSum += layer0Height;
	if (layer0Height > 0)
	{
		half3 layer0albedo = (half3)0;
		SAMPLE_LAYERED(layer0albedo, rgb, _BaseMap, SAMPLER_CHEAP, layer0uv, layer0idx, layer0dx, layer0dy)
		layer0albedo *= _BaseColor;
		albedoSum += layer0albedo * layer0Height;

		half4 layer0aysx = (half4)0;
		SAMPLE_LAYERED(layer0aysx, rgba, _AYSXMap, SAMPLER_CHEAP, layer0uv, layer0idx, layer0dx, layer0dy);
		aoSum += layer0aysx.x * layer0Height;
		smoothnessSum += layer0aysx.z * layer0Height;
		hOctSum += layer0Height * (half(2.0) * layer0aysx.ag - half(1.0));
	}
}


if (layerWeights.x > 0)
{
	const int layer1idx = 1;
	float2 layer1uv; 
	half2 layer1dx;
	half2 layer1dy;
	
	LAYER_UVS(layer1idx, layer1uv, layer1dx, layer1dy);
	
	half layer1Height = 0;
	SAMPLE_LAYERED(layer1Height, r, _HeightMap, SAMPLER_CHEAP, layer1uv, layer1idx, layer1dx, layer1dy);
	
	layer1Height *= layerWeights.x;
	
	weightSum = layer1Height + weightSum;
	if (layer1Height > 0)
	{
		half3 layer1albedo = (half3)0;
		SAMPLE_LAYERED(layer1albedo, rgb, _BaseMap, SAMPLER_CHEAP, layer1uv, layer1idx, layer1dx, layer1dy)
		layer1albedo *= _BaseColor;
		albedoSum += layer1albedo * layer1Height;
		
		half4 layer1aysx = (half4)0;
		SAMPLE_LAYERED(layer1aysx, rgba, _AYSXMap, SAMPLER_CHEAP, layer1uv, layer1idx, layer1dx, layer1dy);
		aoSum += layer1aysx.x * layer1Height;
		smoothnessSum += layer1aysx.z * layer1Height;
		hOctSum += layer1Height * (half(2.0) * layer1aysx.ag - half(1.0));
	}
}

if (layerWeights.y > 0)
{
	const int layer2idx = 2;
	float2 layer2uv; half2 layer2dx, layer2dy;
	LAYER_UVS(layer2idx, layer2uv, layer2dx, layer2dy);
	half layer2Height = 0;
	SAMPLE_LAYERED(layer2Height, r, _HeightMap, SAMPLER_CHEAP, layer2uv, layer2idx, layer2dx, layer2dy);
	layer2Height *= layerWeights.y;
	weightSum += layer2Height;
	if (layer2Height > 0)
	{
		half3 layer2albedo = (half3)0;
		SAMPLE_LAYERED(layer2albedo, rgb, _BaseMap, SAMPLER_CHEAP, layer2uv, layer2idx, layer2dx, layer2dy)
		layer2albedo *= _BaseColor;
		albedoSum += layer2albedo * layer2Height;

		half4 layer2aysx = (half4)0;
		SAMPLE_LAYERED(layer2aysx, rgba, _AYSXMap, SAMPLER_CHEAP, layer2uv, layer2idx, layer2dx, layer2dy);
		aoSum += layer2aysx.x * layer2Height;
		smoothnessSum += layer2aysx.z * layer2Height;
		hOctSum += layer2Height * (half(2.0) * layer2aysx.ag - half(1.0));
	}
}

if (layerWeights.z > 0)
{
	const int layer3idx = 3;
	float2 layer3uv; half2 layer3dx, layer3dy;
	LAYER_UVS(layer3idx, layer3uv, layer3dx, layer3dy);
	half layer3Height = 0;
	SAMPLE_LAYERED(layer3Height, r, _HeightMap, SAMPLER_CHEAP, layer3uv, layer3idx, layer3dx, layer3dy);
	layer3Height *= layerWeights.z;
	weightSum += layer3Height;
	if (layer3Height > 0)
	{
		half3 layer3albedo = (half3)0;
		SAMPLE_LAYERED(layer3albedo, rgb, _BaseMap, SAMPLER_CHEAP, layer3uv, layer3idx, layer3dx, layer3dy)
		layer3albedo *= _BaseColor;
		albedoSum += layer3albedo * layer3Height;

		half4 layer3aysx = (half4)0;
		SAMPLE_LAYERED(layer3aysx, rgba, _AYSXMap, SAMPLER_CHEAP, layer3uv, layer3idx, layer3dx, layer3dy);
		aoSum += layer3aysx.x * layer3Height;
		smoothnessSum += layer3aysx.z * layer3Height;
		hOctSum += layer3Height * (half(2.0) * layer3aysx.ag - half(1.0));
	}
}

if (layerWeights.w > 0)
{
	const int layer4idx = 4;
	float2 layer4uv; half2 layer4dx, layer4dy;
	LAYER_UVS(layer4idx, layer4uv, layer4dx, layer4dy);
	half layer4Height = 0;
	SAMPLE_LAYERED(layer4Height, r, _HeightMap, SAMPLER_CHEAP, layer4uv, layer4idx, layer4dx, layer4dy);
	layer4Height *= layerWeights.w;
	weightSum += layer4Height;
	if (layer4Height > 0)
	{
		half3 layer4albedo = (half3)0;
		SAMPLE_LAYERED(layer4albedo, rgb, _BaseMap, SAMPLER_CHEAP, layer4uv, layer4idx, layer4dx, layer4dy)
		layer4albedo *= _BaseColor;
		albedoSum += layer4albedo * layer4Height;

		half4 layer4aysx = (half4)0;
		SAMPLE_LAYERED(layer4aysx, rgba, _AYSXMap, SAMPLER_CHEAP, layer4uv, layer4idx, layer4dx, layer4dy);
		aoSum += layer4aysx.x * layer4Height;
		smoothnessSum += layer4aysx.z * layer4Height;
		hOctSum += layer4Height * (half(2.0) * layer4aysx.ag - half(1.0));
	}
}


/*
// Misguided attempt at optimization. Tried making a list of layer index and splat weight pairs, sorting by weight, and only sampling the top two layers.
// It turns out the quest really hates using a non-constant index to select the texture, uvs, and cbuffer properties to sample with. This is about 40%
// slower than the brute force approach of going through all the layers in order.

SLZ::Layering::Layer layers[3];
SLZ::Layering::GetThreeActiveLayersNoBase(layerWeights, layers);
layers[2].index = 0;

float2 uv0 = UNPACK_UV0(i);
half2 dx = 0;
half2 dy = 0;

half layerHeight0 = 0;
float2 uv_height; half2 dx_height, dy_height;
dx = (half2)ddx(uv0);
dy = (half2)ddy(uv0);

half layerHeight2 = 0;
LAYER_UVS(layers[2].index, uv_height, dx_height, dy_height);
SAMPLE_LAYERED(layerHeight2, r, _HeightMap, SAMPLER_CHEAP, uv_height, layers[2].index, dx_height, dy_height);
layers[2].weight = saturate(layerHeight2 - layers[0].weight - layers[1].weight) + half(0.01);

if (layers[0].weight > HALF_MIN)
{
	LAYER_UVS(layers[0].index, uv_height, dx_height, dy_height);
	SAMPLE_LAYERED(layerHeight0, r, _HeightMap, SAMPLER_CHEAP, uv_height, layers[0].index, dx_height, dy_height);
	layers[0].weight *= layerHeight0;
}

if (layers[1].weight > HALF_MIN)
{
	half layerHeight1 = 0;
	LAYER_UVS(layers[1].index, uv_height, dx_height, dy_height);
	SAMPLE_LAYERED(layerHeight1, r, _HeightMap, SAMPLER_CHEAP, uv_height, layers[1].index, dx_height, dy_height);
	layers[1].weight *= layerHeight1;
}

// sort the layers, pick the two most important
if (layers[0].weight < layers[2].weight) SLZ::Layering::Swap(layers, 0, 2);
if (layers[0].weight < layers[1].weight) SLZ::Layering::Swap(layers, 0, 1);
if (layers[1].weight < layers[2].weight) SLZ::Layering::Swap(layers, 1, 2);

layers[1].weight += HALF_MIN;

//if (layers[1].weight < 1e-5) layers[1].weight = 1 - layers[0].weight;

half layerWeightTotal = layers[0].weight + layers[1].weight;

layers[0].weight /= layerWeightTotal;
layers[1].weight /= layerWeightTotal;

SLZ::Layering::Layer layer0 = layers[0];
SLZ::Layering::Layer layer1 = layers[1];

half4 albedo = (half4)0;
half3 mas = (half3)0;
half2 normalMap2 = half2(0,0);
dx = 1 * (half2)ddx(uv0);
dy = 1 * (half2)ddy(uv0);
float2 uv_layer0; half2 dx_layer0, dy_layer0;
LAYER_UVS(layer0.index, uv_layer0, dx_layer0, dy_layer0);

SAMPLE_LAYERED(albedo, rgba, _BaseMap, sampler_BaseMap, uv_layer0, layer0.index, dx_layer0, dy_layer0)
albedo *= layer0.index == min16int(0) ? _BaseColor : half4(1,1,1,1);
albedo *= layer0.weight;

half4 aysx0 = 0;
SAMPLE_LAYERED(aysx0, rgba, _AYSXMap, SAMPLER_CHEAP, uv_layer0, layer0.index, dx_layer0, dy_layer0);
mas = layer0.weight * half3(0.0f, aysx0.r, aysx0.b);
normalMap2 = layer0.weight * (half(2.0) * aysx0.ag - half(1.0)); 


//SAMPLE_LAYERED(mas, rgb, _MetallicGlossMap, SAMPLER_CHEAP, uv_layer0, layer0.index, dx_layer0, dy_layer0)
////normalMap2.rg = half(0.5) * ((half(2) * mas.gb - half(1)) * layer0.weight) + half(0.5);
//mas *= layer0.weight;
//
//SAMPLE_LAYERED(normalMap2, rgba, _BumpMap, SAMPLER_CHEAP, uv_layer0, layer0.index, dx_layer0, dy_layer0)
////normalMap2 *= layer0.weight;


if (layer1.weight > 2 * HALF_MIN)
{
	layer1.weight = half(1) - layer0.weight;
	float2 uv_layer1; half2 dx_layer1, dy_layer1;
	LAYER_UVS(layer1.index, uv_layer1, dx_layer1, dy_layer1);

	half4 albedo1 = (half4)0;
	SAMPLE_LAYERED(albedo1, rgba, _BaseMap, SAMPLER_CHEAP, uv_layer1, layer1.index, dx_layer1, dy_layer1)
	albedo1 *= layer1.index == min16int(0) ? _BaseColor : half4(1,1,1,1);
	albedo += albedo1 * layer1.weight;

	half4 aysx1 = 0;
	SAMPLE_LAYERED(aysx1, rgba, _AYSXMap, SAMPLER_CHEAP, uv_layer1, layer1.index, dx_layer1, dy_layer1);

	mas += layer1.weight * half3(0.0f, aysx1.r, aysx1.b);
	normalMap2 += layer1.weight * (half(2.0) * aysx1.ag - half(1.0)); 

	//half3 mas1 = (half3)0;
	//SAMPLE_LAYERED(mas1, rgb, _MetallicGlossMap, SAMPLER_CHEAP, uv_layer1, layer1.index, dx_layer1, dy_layer1)
	//mas += mas1 *  layer1.weight;
	//
	//half4 normalMap1 = 0;
	//SAMPLE_LAYERED(normalMap1, rgba, _BumpMap, SAMPLER_CHEAP, uv_layer1, layer1.index, dx_layer1, dy_layer1)
	//normalMap2 += normalMap1 * (half(1.0f) - layer0.weight);
	////normalMap2.rg += half(0.5) * ((half(2) * mas1.gb - half(1)) * layer1.weight) + half(0.5);
}
albedo.a = 1;
*/


// End Injection FRAG_READ_INPUTS from Injection_Layered.hlsl ----------------------------------------------------------


// Begin Injection PBR_VALUES from Injection_Layered.hlsl ----------------------------------------------------------
	// Test 8
	half4 albedo;
    albedo.a = _Surface == 0 ? half(1.0) : albedo.a;
	albedo.rgb = albedoSum / weightSum;
    half metallic = 0;
    half ao = aoSum / weightSum;
    half smoothness = smoothnessSum / weightSum;
	half2 hOct = hOctSum / weightSum;
// End Injection PBR_VALUES from Injection_Layered.hlsl ----------------------------------------------------------


/*---------------------------------------------------------------------------------------------------------------------------*/
/*---Sample Normal Map-------------------------------------------------------------------------------------------------------*/
/*---------------------------------------------------------------------------------------------------------------------------*/

    half3 normalTS = half3(0, 0, 1);
    half  geoSmooth = 1;
    half4 normalMap = half4(0, 0, 1, 0);

// Begin Injection NORMAL_MAP from Injection_Layered.hlsl ----------------------------------------------------------
	
	normalTS = SLZAccurateNormalize(UnpackNormalHemiOctEncodeNoNormalize(hOct));

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