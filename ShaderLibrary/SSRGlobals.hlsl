#if !defined(SLZ_SSR_GLOBALS)
#define SLZ_SSR_GLOBALS

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/DefaultSamplers.hlsl"


CBUFFER_START(SSRConstants)
float4 _SSRVariables;
float4 _SSRVariables2;
CBUFFER_END

#define _SSRHitRadius _SSRVariables.x
#define _SSRHitScale _SSRVariables.x
#define _SSRTemporalWeight _SSRVariables.y
#define _SSRHitBias _SSRVariables.y
#define _SSRSteps _SSRVariables.z
#define _SSRMinMip asuint(_SSRVariables.w)
#define _SSRDistScale _SSRVariables2.x

//SamplerState sampler_trilinear_clamp;

#endif