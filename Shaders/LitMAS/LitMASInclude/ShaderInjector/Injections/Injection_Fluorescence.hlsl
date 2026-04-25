//#!INJECT_BEGIN UNIVERSAL_DEFINES 1
#pragma shader_feature_local_fragment _FLUORESCENCE
//#!INJECT_END

//#!INJECT_BEGIN UNIFORMS 0
#if defined(_FLUORESCENCE)
	TEXTURE2D(_FluorMap);
#endif
//#!INJECT_END


//#!INJECT_BEGIN FRAG_POST_READ 0
#if defined(_FLUORESCENCE)
	half4 fluorMap = SAMPLE_TEXTURE2D(_FluorMap, sampler_BaseMap, uv_main);
#endif
//#!INJECT_END

//#!INJECT_BEGIN DETAIL_MAP 100000
#if defined(_FLUORESCENCE)
	half4 fluorescence = (fluorMap * _FluorColor) * half4(lerp(half3(1,1,1), albedo.rgb, _FluorAlbedoTint), 1.0);
	albedo.rgb = lerp(albedo.rgb, half3(0,0,0), _FluorAlbedoTint * fluorMap);
#endif
//#!INJECT_END

//#!INJECT_BEGIN PRE_LIGHTING_CALC 0
#if defined(_FLUORESCENCE)
	surfData.fluorescence = fluorescence;
	surfData.absorbance = _FluorAbsorbance;
#endif
//#!INJECT_END
