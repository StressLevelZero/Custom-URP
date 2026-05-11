

//#!INJECT_BEGIN UNIFORMS 0
Texture2D<float4> _BaseMap;
SamplerState sampler_BaseMap;
Texture2D<float4> _EmissionMap;
SamplerState sampler_EmissionMap;
//#!INJECT_END

//#!INJECT_BEGIN INCLUDES 0
#pragma multi_compile_local_fragment _ _EXPENSIVE_TP

#if defined(_EXPENSIVE_TP)
#define SLZ_SAMPLE_TP_MAIN(tex, sampl, uv) tex.SampleLevel( sampl, uv, 0)
#else	
#define SLZ_SAMPLE_TP_MAIN(tex, sampl, uv) tex.SampleLevel( sampl, uv, 0)
#endif

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SLZTriplanar.hlsl"
//#!INJECT_END

//#!INJECT_BEGIN CLOSEST_HIT 0
float2 uvTP;
half3x3 TStoWsTP;
half2 scale = 1.0/_UVScaler;
		
#if defined(_EXPENSIVE_TP)
tpDerivatives tpDD;
GetDirectionalDerivatives( payload.hitPos, tpDD);
half2 ddxTP, ddyTP;
GetTPUVExpensive(uvTP, ddxTP, ddyTP, TStoWsTP, payload.hitPos, normalize(UNPACK_NORMAL(i)), tpDD);
ddxTP = _RotateUVs ? half2(-ddxTP.y, ddxTP.x) : ddxTP;
ddyTP = _RotateUVs ? half2(-ddyTP.y, ddyTP.x) : ddyTP;
half2 ddxMain = ddxTP * scale;
half2 ddyMain = ddyTP * scale;
#else
GetTPUVCheap(uvTP, TStoWsTP, payload.hitPos, payload.worldNormal);
#endif
		
uvTP = _RotateUVs ? float2(-uvTP.y, uvTP.x) : uvTP;
float2 uv_main = mad(uvTP, scale, _BaseMap_ST.zw);
	albedo = SLZ_SAMPLE_TP_MAIN(_BaseMap, sampler_BaseMap, uv_main);
//albedo.a = _Surface == 0 ? half(1.0) : albedo.a;

emission = _Emission * SLZ_SAMPLE_TP_MAIN(_EmissionMap, sampler_EmissionMap, uv_main) * _EmissionColor;
emission.rgb *= lerp(albedo.rgb, 1, emission.a);
emission = max(emission * _BakedMutiplier,0);
//#!INJECT_END