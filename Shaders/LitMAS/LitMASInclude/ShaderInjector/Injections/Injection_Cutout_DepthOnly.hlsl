//#!INJECT_BEGIN UNIVERSAL_DEFINES 0
#pragma shader_feature_local_fragment _ALPHATEST_ON
//#!INJECT_END

//#!INJECT_BEGIN UNIFORMS 0
	TEXTURE2D(_BaseMap);
	SAMPLER(sampler_BaseMap);
//#!INJECT_END

//#!INJECT_BEGIN VERTEX_IN 0
	//#!TEXCOORD float2 uv0 0
//#!INJECT_END

//#!INJECT_BEGIN INTERPOLATORS 0
	//#!TEXCOORD float2 uv0XY 1
//#!INJECT_END

//#!INJECT_BEGIN VERTEX_END 0
	o.uv0XY = mad(v.uv0.xy, _BaseMap_ST.xy, _BaseMap_ST.zw);
//#!INJECT_END

//#!INJECT_BEGIN FRAG_BEGIN 0
#if defined(_ALPHATEST_ON)
	float alpha = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv0XY.xy).a;
	clip((alpha * _BaseColor.a) - _Cutoff);
#endif
//#!INJECT_END