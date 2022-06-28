#if !defined(SLZ_BLUENOISE)
#define SLZ_BLUENOISE

Texture2DArray<float4> _BlueNoiseRGBA;
Texture2DArray<float> _BlueNoiseR;

CBUFFER_START(BlueNoiseDim)
int3 _BlueNoise_Dim;
int _BlueNoise_Frame;
CBUFFER_END

#endif