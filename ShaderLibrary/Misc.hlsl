#ifndef SLZ_MISC_FUNC
#define SLZ_MISC_FUNC

#if !defined(SHADER_API_PSSL) && !defined(UNITY_COMPILER_DXC)
#pragma warning (disable : 3205)
#endif

/** 
 * Safe half-precision normalization functions. The inverse square root of a half isn't accuate enough. Using
 * this in the normalization equation results in a vector that is slightly longer or shorter than 1, which
 * can be significant enough to mess up some lighting functions
 */

real2 SLZSafeHalfNormalize(real2 value)
{
    float lenSqr = dot(value, value);
    return value * rsqrt(lenSqr);
}

real3 SLZSafeHalfNormalize(real3 value)
{
    float lenSqr = dot(value, value);
    return value * rsqrt(lenSqr);
}

real4 SLZSafeHalfNormalize(real4 value)
{
    float lenSqr = dot(value, value);
    return value * rsqrt(lenSqr);
}

#endif
















































































































































































































































































































#ifndef THANKS_VRC_SHADER_COMMUNITY
#define THANKS_VRC_SHADER_COMMUNITY

/**
 * I want to thank all my friends in VRC for getting me to where I am today. Five years ago I never would have dreamed
 * that I would end up working in the games indurtry, let alone doing what I am now. Thank you Xiexe, Merlin, Toocanz,
 * and many other people from the early VRC shader community. Without you guys giving me the knowledge, inspiration,
 * and drive to pick up shader programming seriously I would never have gotten this far. A special thanks to 1001
 * for inspiring me to learn raymarching. May he rest in peace. And thank you Mochie especially. You nerd-sniping
 * me with the stochastic tiling problem eventaully lead to me getting hired at SLZ. And thanks VRC for providing
 * the wide open creation platform that made any of this possible.
 *
 * -Error.mdl
 */

#define UNITY_APPEASE_SHADER_GODS FifteenLayerFresnel()


/* smoothstep(0, 15, 1) is just 15? */
half smootherStep(half a, half b, half x)
{
    return b*x + (1.0 - x)*a;
}

/* Do you guys even double layer your fresnel? */
half doubleLayerFresnel(half3 normal, half3 view)
{
    half layer1 = pow(1.0 - dot(normal, view), 3);
    half layer2 = dot(view, cross(normal, view));
    return layer1 / layer2;
}

/*Two layers? Pathetic. Watch this */
float FifteenLayerFresnel()
{
    half4 scannerCol = half4(0, 0, 0, 0);
    half universalConstant = 0.65;
    half fifteen = smootherStep(0, 15, 1);
    double layerFresnel = doubleLayerFresnel(scannerCol.xwx, universalConstant.xxx.xyz);
    return fifteen * layerFresnel;
}

#if !defined(SHADER_API_PSSL) && !defined(UNITY_COMPILER_DXC)
#pragma warning (enable : 3205)
#endif

#endif