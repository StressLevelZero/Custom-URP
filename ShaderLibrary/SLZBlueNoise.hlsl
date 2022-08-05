#if !defined(SLZ_BLUENOISE)
#define SLZ_BLUENOISE

Texture2DArray<float4> _BlueNoiseRGBA;
Texture2DArray<float> _BlueNoiseR;

CBUFFER_START(BlueNoiseDim)
float3 _BlueNoise_Dim;
float _BlueNoise_Frame;
float2 _BlueNoise_RandomOffset;
CBUFFER_END

half GetScreenNoiseR(float2 screenUV)
{
	float2 noiseUvs = screenUV * _ScreenParams.xy + _BlueNoise_RandomOffset;
	noiseUvs.xy = fmod(noiseUvs.xy, _BlueNoise_Dim.xy);
	return _BlueNoiseR.Load(int4(noiseUvs.xy, _BlueNoise_Frame, 0)).r;
}

half GetScreenNoiseRSlice(float2 screenUV, int slice)
{
	float2 noiseUvs = screenUV * _ScreenParams.xy + _BlueNoise_RandomOffset;
	noiseUvs.xy = fmod(noiseUvs.xy, _BlueNoise_Dim.xy);
	return _BlueNoiseR.Load(int4(noiseUvs.xy, slice, 0)).r;
}

half GetScreenNoiseROffset(float2 screenUV, float offset)
{
	float frame = fmod((float)_BlueNoise_Frame + offset, _BlueNoise_Dim.z);
	float2 noiseUvs = screenUV * _ScreenParams.xy + _BlueNoise_RandomOffset;
	noiseUvs.xy = fmod(noiseUvs.xy, _BlueNoise_Dim.xy);
	return _BlueNoiseR.Load(int4(noiseUvs.xy, frame, 0)).r;
}

half4 GetScreenNoiseRGBA(float2 screenUV)
{
	float2 noiseUvs = screenUV * _ScreenParams.xy;
	noiseUvs.xy = fmod(noiseUvs.xy, _BlueNoise_Dim.xy);
	return _BlueNoiseRGBA.Load(int4(noiseUvs.xy, _BlueNoise_Frame, 0));
}

half4 GetScreenNoiseRGBASlice(float2 screenUV, int slice)
{
	float2 noiseUvs = screenUV * _ScreenParams.xy;
	noiseUvs.xy = fmod(noiseUvs.xy, _BlueNoise_Dim.xy);
	return _BlueNoiseRGBA.Load(int4(noiseUvs.xy, slice, 0));
}

half4 GetScreenNoiseRGBAOffset(float2 screenUV, float offset)
{
	float frame = fmod(_BlueNoise_Frame + offset, _BlueNoise_Dim.z);
	float2 noiseUvs = screenUV * _ScreenParams.xy;
	noiseUvs.xy = fmod(noiseUvs.xy, _BlueNoise_Dim.xy);
	return _BlueNoiseRGBA.Load(int4(noiseUvs.xy, frame, 0));
}

#endif