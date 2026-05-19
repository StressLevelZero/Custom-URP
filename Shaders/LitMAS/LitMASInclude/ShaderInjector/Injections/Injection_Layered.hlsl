//#!INJECT_BEGIN INCLUDES 0
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Layering.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SLZTriplanar.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/BicubicFilter.hlsl"
//#!INJECT_END

//#!INJECT_BEGIN FUNCTIONS 0



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
#define SAMPLER_SPLAT sampler_LinearRepeat
#define SAMPLER_CHEAP sampler_LinearRepeat
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
if (layers[0].weight > HALF_MIN)
{
	LAYER_UVS(layers[0].index, uv_height, dx_height, dy_height);
	SAMPLE_LAYERED(layerHeight0, r, _HeightMap, SAMPLER_SPLAT, uv_height, layers[0].index, dx_height, dy_height);
	layers[0].weight *= layerHeight0;
}

if (layers[1].weight > HALF_MIN)
{
	half layerHeight1 = 0;
	LAYER_UVS(layers[1].index, uv_height, dx_height, dy_height);
	SAMPLE_LAYERED(layerHeight1, r, _HeightMap, SAMPLER_SPLAT, uv_height, layers[1].index, dx_height, dy_height);
	layers[1].weight *= layerHeight1;
}


half layerHeight2 = 0;
LAYER_UVS(layers[2].index, uv_height, dx_height, dy_height);
SAMPLE_LAYERED(layerHeight2, r, _HeightMap, SAMPLER_SPLAT, uv_height, layers[2].index, dx_height, dy_height);
layers[2].weight = layerHeight2 * saturate(1.0 - layers[0].weight - layers[1].weight);


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
albedo *= layer0.weight;

half4 aysx0 = 0;
SAMPLE_LAYERED(aysx0, rgba, _AYSXMap, SAMPLER_CHEAP, uv_layer0, layer0.index, dx_layer0, dy_layer0);
mas = layer0.weight * half3(0.0f, aysx0.r, aysx0.b);
normalMap2 = (half(2.0) * aysx0.ag - half(1.0)); 


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
//#!INJECT_END




//#!INJECT_BEGIN NORMAL_MAP 1
	
	normalTS = SLZAccurateNormalize(UnpackNormalHemiOctEncodeNoNormalize(normalMap2));

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