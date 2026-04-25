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

//#!INJECT_BEGIN UNIFORMS 0
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


//#!INJECT_END




//#!INJECT_BEGIN NORMAL_MAP 1
	normalMap = normalMap2;
	normalTS = UnpackNormal(normalMap);
	normalTS = _Normals ? normalTS : half3(0, 0, 1);
	geoSmooth = _Normals ? 1.0 - normalMap.b : 1.0;
	smoothness = saturate(smoothness + geoSmooth - 1.0);
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