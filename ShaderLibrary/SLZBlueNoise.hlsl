#if !defined(SLZ_BLUENOISE)
#define SLZ_BLUENOISE

Texture2DArray<float4> _BlueNoiseRGBA;
Texture2DArray<float> _BlueNoiseR;

CBUFFER_START(BlueNoiseDim)
int3 _BlueNoise_Dim;
int _BlueNoise_Frame;
CBUFFER_END

half GetScreenNoise(float2 screenUV)
{
	half2 noiseUvs = screenUV * _ScreenParams.xy;
	noiseUvs.xy = fmod(noiseUvs.xy, _BlueNoise_Dim.xy);
	return _BlueNoiseR.Load(int4(noiseUvs.xy, 0, 0)).r;
}

half GetScreenNoiseRGBA(float2 screenUV)
{
	half2 noiseUvs = screenUV * _ScreenParams.xy;
	noiseUvs.xy = fmod(noiseUvs.xy, _BlueNoise_Dim.xy);
	return _BlueNoiseRGBA.Load(int4(noiseUvs.xy, 0, 0));
}

#endif