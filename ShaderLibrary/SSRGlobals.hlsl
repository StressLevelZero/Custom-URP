#if !defined(SLZ_SSR_GLOBALS)
#define SLZ_SSR_GLOBALS




CBUFFER_START(SSRConstants)
float4 _SSRVariables;
CBUFFER_END

#define _SSRHitRadius _SSRVariables.x
#define _SSRHitScale _SSRVariables.x
#define _SSRTemporalWeight _SSRVariables.y
#define _SSRHitBias _SSRVariables.y
#define _SSRSteps _SSRVariables.z
#define _SSRMinMip asint(_SSRVariables.w)

SamplerState sampler_trilinear_clamp;

#endif