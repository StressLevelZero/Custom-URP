//#!INJECT_BEGIN UNIVERSAL_DEFINES 0
#define _RETROREFLECTIVE
//#!INJECT_END

//#!INJECT_BEGIN UNIFORMS 0
TEXTURE2D(_RetroReflMap);
//#!INJECT_END

//#!INJECT_BEGIN PRE_LIGHTING_CALC 10
	surfData.retroReflPercent = _RetroReflIntensity * SAMPLE_TEXTURE2D(_RetroReflMap, sampler_BaseMap, uv0).r;
	surfData.retroReflSharpness = _RetroReflSharpness;
	#if defined(_FLUORESCENCE)
		//surfData.fluorescence = saturate(surfData.fluorescence - 4 * surfData.retroReflPercent);
	#endif
//#!INJECT_END