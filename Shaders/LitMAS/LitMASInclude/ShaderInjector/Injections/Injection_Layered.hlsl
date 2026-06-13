//#!INJECT_BEGIN INCLUDES 0
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Layering.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SLZTriplanar.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/BicubicFilter.hlsl"
//#!INJECT_END

//#!INJECT_BEGIN FUNCTIONS 0



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

//#!INJECT_END

//#!INJECT_BEGIN INTERPOLATORS 0
	//#!TEXCOORD float2 uv_splat 1
//#!INJECT_END

//#!INJECT_BEGIN DEFAULT_TEXTURES 0
Texture2D<min16float4> _BaseMap;
SAMPLER(sampler_BaseMap);

TEXTURE2D(_BumpMap);
Texture2D<min16float3> _MetallicGlossMap;
//#!INJECT_END


//#!INJECT_BEGIN UNIFORMS 0
TEXTURE2D(_SplatMap);
SAMPLER(sampler_SplatMap);

#if defined(SHADER_API_MOBILE)
#define SAMPLER_SPLAT sampler_SplatMap
#define SAMPLER_CHEAP sampler_BaseMap
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

//#!INJECT_END

//#!INJECT_BEGIN MATERIAL_CBUFFER_HALF_SCALARS 0
half _UseGRID;
//#!INJECT_END

//#!INJECT_BEGIN MATERIAL_CBUFFER_FLOAT_VECTORS 0
float4 _LightmapScaleOffset;
float4 _BaseMap1_ST;
float4 _BaseMap2_ST;
float4 _BaseMap3_ST;
float4 _BaseMap4_ST;
//#!INJECT_END

//#!INJECT_BEGIN VERTEX_NORMALS 0
	//VertexNormalInputs ntb = GetVertexNormalInputs(v.normal, v.tangent);
	half3 wNorm = (TransformObjectToWorldNormal(v.normal));
	half3 wTan = (TransformObjectToWorldDir(v.tangent.xyz));
	half tanSign = v.tangent.w * GetOddNegativeScale();
	o.normXYZ_tanZ = half4(wNorm, wTan.z);
	o.uv0XY_tanXY.zw = wTan.xy;
	o.SHVertLights_btSign.w = tanSign;
//#!INJECT_END

//#!INJECT_BEGIN VERTEX_END 0
	o.uv_splat = (v.uv1 - _LightmapScaleOffset.zw) / _LightmapScaleOffset.xy;
	if (_UseGRID != 0)
	{
		half3x3 tan2Wrld;
		GetTPUVCheap(UNPACK_UV0(o), tan2Wrld, UNPACK_WPOS(o), UNPACK_NORMAL(o));
	}
//#!INJECT_END

//#!INJECT_BEGIN FRAG_READ_INPUTS 0
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


//#!INJECT_END

    //#!INJECT_BEGIN PBR_VALUES 0
	// Test 8
	half4 albedo;
    albedo.a = _Surface == 0 ? half(1.0) : albedo.a;
	albedo.rgb = albedoSum / weightSum;
    half metallic = 0;
    half ao = aoSum / weightSum;
    half smoothness = smoothnessSum / weightSum;
	half2 hOct = hOctSum / weightSum;
    //#!INJECT_END


//#!INJECT_BEGIN NORMAL_MAP 1
	
	normalTS = SLZAccurateNormalize(UnpackNormalHemiOctEncodeNoNormalize(hOct));

//#!INJECT_END

//#!INJECT_BEGIN NORMAL_TRANSFORM 0
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
//#!INJECT_END

//#!INJECT_BEGIN SPEC_AA 0
	#if !defined(SHADER_API_MOBILE) && !defined(LITMAS_FEATURE_TP) // Specular antialiasing based on normal derivatives. Only on PC to avoid cost of derivatives on Quest
		//smoothness = min(smoothness, SLZGeometricSpecularAA(normalWS));
		smoothness = SLZGeometricNormalFiltering(smoothness, normalWS, /*variance*/ 0.1, /*threshold*/ 0.2);
	#endif
//#!INJECT_END