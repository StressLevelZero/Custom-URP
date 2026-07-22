#if !defined(SLZ_BLUENOISE)
#define SLZ_BLUENOISE

Texture2DArray<float4> _BlueNoiseRGBA;
Texture2DArray<float> _BlueNoiseR;

CBUFFER_START(BlueNoiseDim)
float4 _BlueNoise_0;
float4 _BlueNoise_1;
CBUFFER_END

// _ScaledScreenParams causes unity's light baker to silently die and enter a boot-loop if it is used in the meta pass!
// unity doesn't have defines for the light mode of the current pass, but the shader graph added its own. If the shader
// including this file uses those, we can guard against using _ScaledScreenParams in the meta.
#if !defined(FIXED_META_SCREENPARAMS) && defined(SHADERPASS) && defined(SHADERPASS_META) && (SHADERPASS==SHADERPASS_META)
#define _ScaledScreenParams _ScreenParams
#endif

#if defined(SLZ_VK_EXT_ENABLED)
    // when the fragment size is > 1, we need to divide by the fragment size and round up to get a whole number resolution 
    #define BLUENOISE_SCREEN_DIM ceil(_ScaledScreenParams.xy / (float2)SLZ_FRAG_SIZE )
#else
    #define BLUENOISE_SCREEN_DIM  _ScaledScreenParams.xy
#endif

#define _BlueNoise_Dim _BlueNoise_0.xyz
#define _BlueNoise_Frame _BlueNoise_0.w
#define _BlueNoise_RandomOffset _BlueNoise_1.xy

half GetScreenNoiseR(float2 screenUV)
{
	float2 noiseUvs = screenUV * BLUENOISE_SCREEN_DIM + _BlueNoise_RandomOffset;
    noiseUvs.xy = fmod(noiseUvs.xy, _BlueNoise_Dim.xy);
    return _BlueNoiseR.Load(int4(noiseUvs.xy, _BlueNoise_Frame, 0)).r;
}

half GetScreenNoiseRSlice(float2 screenUV, int slice)
{
	float2 noiseUvs = screenUV * BLUENOISE_SCREEN_DIM + _BlueNoise_RandomOffset;
    noiseUvs.xy = fmod(noiseUvs.xy, _BlueNoise_Dim.xy);
    return _BlueNoiseR.Load(int4(noiseUvs.xy, slice, 0)).r;
}

half GetScreenNoiseROffset(float2 screenUV, float offset)
{
    float frame = fmod((float)_BlueNoise_Frame + offset, _BlueNoise_Dim.z);
	float2 noiseUvs = screenUV * BLUENOISE_SCREEN_DIM + _BlueNoise_RandomOffset;
    noiseUvs.xy = fmod(noiseUvs.xy, _BlueNoise_Dim.xy);
    return _BlueNoiseR.Load(int4(noiseUvs.xy, frame, 0)).r;
}

half4 GetScreenNoiseRGBA(float2 screenUV)
{
	float2 noiseUvs = screenUV * BLUENOISE_SCREEN_DIM;
    noiseUvs.xy = fmod(noiseUvs.xy, _BlueNoise_Dim.xy);
    return _BlueNoiseRGBA.Load(int4(noiseUvs.xy, _BlueNoise_Frame, 0));
}

half4 GetScreenNoiseRGBASlice(float2 screenUV, int slice)
{
	float2 noiseUvs = screenUV * BLUENOISE_SCREEN_DIM;
    noiseUvs.xy = fmod(noiseUvs.xy, _BlueNoise_Dim.xy);
    return _BlueNoiseRGBA.Load(int4(noiseUvs.xy, slice, 0));
}

half4 GetScreenNoiseRGBAOffset(float2 screenUV, float offset)
{
    float frame = fmod(_BlueNoise_Frame + offset, _BlueNoise_Dim.z);
	float2 noiseUvs = screenUV * BLUENOISE_SCREEN_DIM;
    noiseUvs.xy = fmod(noiseUvs.xy, _BlueNoise_Dim.xy);
    return _BlueNoiseRGBA.Load(int4(noiseUvs.xy, frame, 0));
}


#undef BLUENOISE_SCREEN_DIM
//#undef _BlueNoise_Dim
//#undef _BlueNoise_Frame
//#undef _BlueNoise_RandomOffset

#endif