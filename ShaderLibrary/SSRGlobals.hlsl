#if !defined(SLZ_SSR_GLOBALS)
#define SLZ_SSR_GLOBALS




CBUFFER_START(SSRConstants)
float _SSRHitRadius;
float _SSREdgeFade;
float _SSRSteps;
float _empty;
CBUFFER_END

SamplerState sampler_trilinear_clamp;

#endif