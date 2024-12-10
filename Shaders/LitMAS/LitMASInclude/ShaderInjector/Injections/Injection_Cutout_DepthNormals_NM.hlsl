//#!INJECT_BEGIN UNIVERSAL_DEFINES 0
#pragma shader_feature_local_fragment _ALPHATEST_ON
//#!INJECT_END

//#!INJECT_BEGIN UNIFORMS 0
	TEXTURE2D(_BaseMap);
	SAMPLER(sampler_BaseMap);
//#!INJECT_END

//#!INJECT_BEGIN FRAG_BEGIN 0
#if defined(_ALPHATEST_ON)
	float alpha = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv0XY.xy).a;
	clip((alpha * _BaseColor.a) - _Cutoff);
#endif
//#!INJECT_END