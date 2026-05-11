/* 
 * Stress Level Zero Lighting functions
 */   

#ifndef SLZ_PBR_LIGHTING
#define SLZ_PBR_LIGHTING

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/EntityLighting.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RealtimeLights.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Misc.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/BRDF_part1.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/GlobalIllumination.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SLZExtentions.hlsl"

#if defined(SLZ_LM_BICUBIC)
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/BicubicFilter.hlsl"
#endif

#if defined(DYNAMIC_ADDITIONAL_LIGHTS)
    #define BRANCH_ADDITIONAL_LIGHTS _ADDITIONAL_LIGHTS
#elif defined(_ADDITIONAL_LIGHTS)
    #define BRANCH_ADDITIONAL_LIGHTS true
#else 
    #define BRANCH_ADDITIONAL_LIGHTS false
#endif

#if !defined(UNITY_COMMON_INCLUDED) //Get my IDE to recognize half, this won't ever get compiled since I just included Common.hlsl 
    #define half half
    #define half2 half2
    #define half3 half3
    #define half4 half4
#endif

#if defined(_FLUORESCENCE)
    #define diffuseLight half4
#else
    #define diffuseLight half3
#endif

//#define USE_MOBILE_BRDF 

//------------------------------------------------------------------------
//------------------------------------------------------------------------
// Constants
//------------------------------------------------------------------------
//------------------------------------------------------------------------

#define SLZ_PI_half     half(3.141592653589793238)
#define SLZ_INV_PI_half half(0.318309886183790672)


//------------------------------------------------------------------------
//------------------------------------------------------------------------
// Data Structures
//------------------------------------------------------------------------
//------------------------------------------------------------------------


struct SLZFragData
{
    float3  position;
    half3   normal;
    half3   viewDir;
    half    NoV;
    float2  screenUV;
    float2  lightmapUV;
    float2  dynLightmapUV;
    float4  shadowCoord;
    half4   shadowMask;
    half3   vertexLighting;
#if defined(_SLZ_ANISO_SPECULAR)
    half3 bitangent;
    half3 tangent;
    half visLambdaView; //factor used in the anisotropic visibility function that depends only on the view, normal, tangent, and bitangent
#endif
};

struct SLZSurfData
{
    half3   albedo;
    half    perceptualRoughness;
    half    roughness;
    half3   specular;
    half    reflectivity;
    half3   emission;
    half    occlusion;
    half    alpha;
// #if defined(_SLZ_BRDF_LUT)
//     TEXTURE2D(brdfLUT);
//     SAMPLER(sampler_brdfLUT);
// #endif
#if defined(_SLZ_ANISO_SPECULAR)
    half anisoAspect;
    half roughnessT;
    half roughnessB;
#endif

#if defined(_FLUORESCENCE)
    half4 fluorescence;
    half4 absorbance;
#endif

#if defined(_RETROREFLECTIVE)
    half retroReflSharpness;
    half retroReflPercent;
#endif
};


struct SLZDirectSpecLightInfo
{
    #if defined(_SLZ_ANISO_SPECULAR)
    half NoH2;
    half NoL;
    half LoH;
    half ToL;
    half BoL;
    half ToH;
    half BoH;
    #elif defined(SHADER_API_MOBILE) || defined(USE_MOBILE_BRDF) || defined(UNITY_UNIFIED_SHADER_PRECISION_MODEL)
    half NoH;
    half LoH;
    half NxH2;
    half NoL;
    #else
    half NoV;
    half NoL;
    half NoH;
    half LoH;
    #endif
#if defined(UNITY_UNIFIED_SHADER_PRECISION_MODEL)
    half NoV;
#endif
};

struct SLZAnisoSpecLightInfo
{
    half NoH;
    half NoL;
    half LoH;
    half ToL;
    half BoL;
    half ToH;
    half BoH;
};

struct SLZMonoSpecInfo
{
    #if defined(SLZ_MONO_SPEC)
    half4 colorMagnitude;
    half3 direction;
#endif
};

//------------------------------------------------------------------------
//------------------------------------------------------------------------
// Basic Functions
//------------------------------------------------------------------------
//------------------------------------------------------------------------

/** 
 * The default normalize function doesn't perfectly normalize half-precsion vectors. This can result in bizarre banding in some lighting calculations.
 * It seems that taking the rsqrt of a half is the issue, giving a slightly inaccurate result. Thus cast the length squared value to a float before
 * taking the rsqrt
 *
 * @param value The vector to normalize
 * @return The normalized vector
 */
//half3 SLZSafeHalf3Normalize(half3 value)
//{
//    float3 fltVal = (float3)value;
//    float lenSqr = max(dot(fltVal, fltVal), FLT_MIN);
//	return (half3) (fltVal * rsqrt(lenSqr));
//}

half3 SLZSafeHalf3Normalize(half3 value)
{
    float lenSqr = max(dot((float3) value, (float3) value), FLT_MIN);
    return (float3) value * rsqrt(lenSqr).rrr;
}


/** 
 * Calculate the specular color from the albedo and metallic, and darken the albedo according to the reflectivity to conserve energy
 *
 * @param[in,out]   albedo      Albedo color, tints the specular according to the metallic value, and gets darkened by the reflectivity
 * @param[out]      specular    Specular color, used for controlling the strength and tint of specular lighting  
 * @param           metallic    Metallic value, determines how strongly the specular is tinted by the albedo and the reflectivity
 */
void SLZAlbedoSpecularFromMetallic(inout half3 albedo, out half3 specular, out half reflectivity, half metallic)
{
    specular = lerp(kDielectricSpec.rgb, albedo, metallic);
    half oneMinusReflectivity = -kDielectricSpec.a * metallic + kDielectricSpec.a;
    reflectivity = 1.0h - oneMinusReflectivity;
    albedo = albedo * oneMinusReflectivity;
}

/**
 * Specular antialiasing using normal derivatives to calculate a roughness value to hide sparkles.
 * Taken from the ever-relevant Valve 2015 GDC VR Rendering talk.
 * 
 * @param normal Worldspace normal
 * @return Smoothness value to reduce sparkling, calculate the final smoothness by min'ing with the half smoothness value
 */
half SLZGeometricSpecularAA(half3 normal)
{
    half3 normalDdx = ddx_fine(normal);
    half3 normalDdy = ddy_fine(normal);
    half AARoughness = saturate(max(dot(normalDdx, normalDdx), dot(normalDdy, normalDdy)));
    AARoughness = sqrt(AARoughness); // Valve used pow of 0.3333, I find that is a little too strong. Also sqrt should be cheaper
    return 1.0h - AARoughness;
}

/* Copied from .core/ShaderLibrary/CommonMaterial.hlsl
 * Modified to use fine ddx/ddy
 */
float SLZGeometricNormalVariance(float3 geometricNormalWS, float screenSpaceVariance)
{
    float3 deltaU = ddx_fine(geometricNormalWS);
    float3 deltaV = ddy_fine(geometricNormalWS);

    return screenSpaceVariance * (dot(deltaU, deltaU) + dot(deltaV, deltaV));
}

/* Copied from .core/ShaderLibrary/CommonMaterial.hlsl
 * Modified to use fine ddx/ddy
 */
float SLZGeometricNormalFiltering(float perceptualSmoothness, float3 geometricNormalWS, float screenSpaceVariance, float threshold)
{
    float variance = SLZGeometricNormalVariance(geometricNormalWS, screenSpaceVariance);
    return NormalFiltering(perceptualSmoothness, variance, threshold);
}

void StoreMaxSpecularInfo(inout SLZMonoSpecInfo specInfo, half3 diffuse, half3 direction, half NoH)
{
#if defined(SLZ_MONO_SPEC)
    half magnitude = max(diffuse.r, max(diffuse.g, diffuse.b));
    if (specInfo.colorMagnitude.a < magnitude)
    {
        specInfo.colorMagnitude = half4(diffuse, magnitude);
        specInfo.direction = direction;
    }
#endif
}



//------------------------------------------------------------------------
//------------------------------------------------------------------------
// Automated Data Structure Population Functions
//------------------------------------------------------------------------
//------------------------------------------------------------------------

SLZFragData
    SLZGetFragData(
    float4 positionCS, float3 positionWS, float3 normalWS, float2 lightmapUV, float2 dynLightmapUV, half3 vertexLighting)
{
    SLZFragData data;
    data.position = positionWS;
    data.normal = normalWS;
    data.viewDir = half3(normalize(_WorldSpaceCameraPos - positionWS));
    data.NoV = dot(data.normal, data.viewDir);
    data.lightmapUV = lightmapUV;
    data.dynLightmapUV = dynLightmapUV;
    data.vertexLighting = vertexLighting;
    data.shadowMask = SAMPLE_SHADOWMASK(data.lightmapUV);
    #if defined(MAIN_LIGHT_CALCULATE_SHADOWS)
            data.shadowCoord = TransformWorldToShadowCoord(positionWS);
    #else
            data.shadowCoord = half4(0, 0, 0, 0);
    #endif
    data.screenUV = GetNormalizedScreenSpaceUV(positionCS);
    return data;
}

/**
 * Adds anisotropic specular data to the fragment data struct when using the
 * anisotropic specular model.The surface data struct should be initialized
 * with SLZSurfDataAddAniso before using this so the tangent and bitangent
 * roughnesses can be obtained from it
 * 
 * @param[in,out] frag  fragment data structure to append to
 * @param tangent       tangent vector
 * @param bitangent     bitangent vector
 * @param roughnessT    roughness in the direction of the tangent
 * @param roughnessB    roughness in the direction of the bitangent
 */
void SLZFragDataAddAniso(inout SLZFragData fragData, half3 tangent, half3 bitangent, half roughnessT, half roughnessB)
{
    #if defined(_SLZ_ANISO_SPECULAR)
    fragData.tangent = normalize(tangent);
    fragData.bitangent = normalize(bitangent);
    half ToV = dot(tangent, fragData.viewDir);
    half BoV = dot(bitangent, fragData.viewDir);
    fragData.visLambdaView = length(half3(roughnessT * ToV, roughnessB * BoV, fragData.NoV * fragData.NoV));
    #endif
}

/**
 * Function to quickly initialize a SLZSurfData struct for a given set of PBR
 * surface parameters using the metallic-glossiness model
 * 
 * @param albedo        The abledo color
 * @param metallic      The metallic value
 * @param smoothness    Perceptual smoothness (as read from a texture, not true smoothness!)
 * @param occlusion     The occlusion factor
 * @param emission      The emission value
 */
SLZSurfData SLZGetSurfDataMetallicGloss(const half3 albedo, const half metallic, const half smoothness, const half occlusion, const half3 emission, const half alpha = 1.0)
{
    SLZSurfData data;
    data.albedo = albedo;
    SLZAlbedoSpecularFromMetallic(data.albedo, data.specular, data.reflectivity, metallic);
    data.perceptualRoughness = half(1.0) - smoothness;
    data.roughness = max(data.perceptualRoughness * data.perceptualRoughness, 1.0e-3h);
    //data.fusedVFNorm = 4.0h * data.roughness + 2.0h; // no reason to eat a register to store this, its literally a single MAD
    data.emission = emission;
    data.occlusion = occlusion;
    data.alpha = alpha;
    return data;
}

/** 
 * Adds anisotropic specular data to the surface data struct when using the
 * anisotropic specular model. The surface data struct should be initialized
 * before this
 * 
 * @param[in, out] surf The surface data struct to append to
 * @param anisoAspect   The anisotropic roughness aspect ratio, where 0
 *                      stretches the highlight along the bitangent, 0.5 is
 *                      isotropic, and 1 stretches it along the tangent
 */
void SLZSurfDataAddAniso(inout SLZSurfData surf, half anisoAspect)
{
#if defined(_SLZ_ANISO_SPECULAR)
    surf.anisoAspect = 2.0 * anisoAspect - 1.0;
    half clampedRough = surf.roughness;// clamp(surf.roughness, 0.05, 1);
    surf.roughnessT = max(clampedRough * surf.anisoAspect + clampedRough, 0.001);
    surf.roughnessB = max(-clampedRough * surf.anisoAspect + clampedRough, 0.001);
#endif
}

SLZDirectSpecLightInfo SLZGetDirectLightInfo(const SLZFragData frag, const half3 lightDir)
{

    SLZDirectSpecLightInfo data;
    #if defined(_SLZ_ANISO_SPECULAR)
        float3 halfDir = SLZSafeHalf3Normalize(lightDir + frag.viewDir);

        half3 NxH = cross(frag.normal, halfDir);
        data.NoH2 = half(1.0) - dot(NxH, NxH);
        data.ToH = dot(frag.tangent, halfDir);
        data.BoH = dot(frag.bitangent, halfDir);

        data.LoH = saturate(dot(lightDir, halfDir));
        data.ToL = dot(frag.tangent, lightDir);
        data.BoL = dot(frag.bitangent, lightDir);
        data.NoL = saturate(dot(frag.normal, lightDir));
    #elif defined(SHADER_API_MOBILE) || defined(USE_MOBILE_BRDF) || defined(UNITY_UNIFIED_SHADER_PRECISION_MODEL)
        // See https://google.github.io/filament/Filament.md.html#materialsystem/specularbrdf/normaldistributionfunction(speculard)
        // Qualcomm Adreno drivers started severely rounding the result of a cross of half-vectors. Components smaller than 2^-7 get rounded down to 0, so when the normal and half vectors are close it rounds to 0. Makes the NDF turn into a sharp square :(. 
        // Stupid solution: multiply the normalized half-vector by 4. The cross product of the 4x half-vector and the normal vector is also 4x as long, and thus the normal and half vectors can get far closer before the components of the result round to 0 
        // After dotting the 4x cross vector with itself, we can divide by 16 to get the actual value
    
        // counter-intuitively, doing the 4x multiply after calculating NoH and LoH results in 1 more register used (with DXC 1.8)
        // Multiplying NoH and LoH by 0.25 after using the 4x scaled half vector doesn't
        half3 halfDir = SLZSafeHalf3Normalize(lightDir + frag.viewDir);
        data.NoH = saturate(dot(frag.normal, halfDir));
        data.LoH = saturate(dot(lightDir, halfDir));

        half3 NxH = cross(frag.normal, half(4.0) * halfDir);
        data.NxH2 = saturate(dot(NxH, NxH) * half(0.0625h));
        data.NoL = saturate(dot(frag.normal, lightDir));
        #if defined(UNITY_UNIFIED_SHADER_PRECISION_MODEL) && !defined(USE_MOBILE_BRDF)
            data.NoV = abs(frag.NoV) + half(1e-5);
            data.NoL = dot(frag.normal, lightDir); // Visibility function needs abs, specular falloff needs saturate
        #else
            data.NoL = saturate(dot(frag.normal, lightDir));
        #endif
    #else
        data.NoV = abs(frag.NoV) + half(1e-5);
        data.NoL = dot(frag.normal, lightDir); // Visibility function needs abs, specular falloff needs saturate
        half3 halfDir = SLZSafeHalf3Normalize(lightDir + frag.viewDir);
        data.NoH = saturate(dot(frag.normal, halfDir));
        data.LoH = saturate(dot(lightDir, halfDir));
        /*
        // Avoid actually calculating half light-view vector using identities. See Earl Hammon, Jr. "PBR Diffuse Lighting for GGX+Smith Microsurfaces". GDC 2017
        half LoV = dot(lightDir, viewDir);
        half LVLen2 = half(2.0) * LoV + half(2.0); // length(L + V)^2 = 2 * dot(L,V) + 2
        half rcpLVLen2 = rcp(LVLen2); // 1 / length(L+V)
        data.LoH = rcpLVLen2 * LoV + rcpLVLen2; // dot(L, H) = 0.5 * length(L + V) = 0.5 * (length(L+V)^2) / length(L + V) = 0.5 * (2*dot(L,V) + 2) / length(L+V) =  length(L+V) * (dot(L+V) + 1)
        data.NoH = (data.NoV + data.NoL) * rcpLVLen2; // dot(N, H) = (dot(N, L) + dot(N, V)) / length(L + V)   
        */
    #endif
    return data;
}

SLZAnisoSpecLightInfo SLZGetAnisoSpecLightInfo(const half3 normal, const half3 tangent, const half3 bitangent,
    const half3 viewDir, const half3 lightDir)
{
    SLZAnisoSpecLightInfo data;
    half3 halfDir = SLZSafeHalf3Normalize(lightDir + viewDir);
    data.NoH = saturate(dot(normal, halfDir));
    data.LoH = saturate(dot(lightDir, halfDir));
    data.ToH = dot(tangent, halfDir);
    data.BoH = dot(bitangent, halfDir);
    data.ToL = dot(tangent, lightDir);
    data.BoL = dot(bitangent, lightDir);
    return data;
}
//------------------------------------------------------------------------
//------------------------------------------------------------------------
// BRDF Functions
//------------------------------------------------------------------------
//------------------------------------------------------------------------

/**
 * Lambert diffuse, simplest diffuse BDRF possible
 *
 * @param attenlightColor Light color multiplied by light attenuation
 * @param normal          Worldspace normal
 * @param lightDir        Unit vector pointing from the fragment to the light in worldspace
 */
diffuseLight SLZLambertDiffuse(const diffuseLight attenLightColor, const half3 normal, const half3 lightDir)
{
    return attenLightColor * saturate(dot(normal, lightDir));
}

half4 BDRFLUTSAMPLER(half2 UV){
    #if defined(_BRDFMAP)
        return SAMPLE_TEXTURE2D_LOD(g_tBRDFMap, sampler_LinearClamp, UV, 0);
    #else
        return 0;
    #endif
}

/**
 * Samples a 2D BRDF lookup table with normal dot light on the horizontal axis
 * and normal dot view on the vertical axis.
 *
 * @param bdrfLUT           BRDF lookup table texture
 * @param sampler_brdfLUT   sampler to use with the lookup table, probably should be a bilinear clamped sampler
 * @param NoV               dot of the normal and view, should not be saturated or abs'd (get it from fragData not surfData)
 * @param NoL               dot of the normal and light, should not be saturated or abs'd
 * @return BRDF color for the given dot products of the light, normal, and view direction
 */
half4 SLZSampleBDRFLUT(half NoV, half NoL)
{
	NoL = saturate((NoL + half(1)) * half(0.5));
    NoV = saturate(NoV);
    return BDRFLUTSAMPLER(half2(NoL, NoV));
}

/**
 * Samples a 2D BRDF lookup table with normal dot light on the horizontal axis
 * and normal dot view on the vertical axis. Takes a half lambert (from
 * directional light map) instead of N dot L 
 *
 * @param bdrfLUT           BRDF lookup table texture
 * @param sampler_brdfLUT   sampler to use with the lookup table, probably should be a bilinear clamped sampler
 * @param NoV               dot of the normal and view, should not be saturated or abs'd (get it from fragData not surfData)
 * @param NoL               dot of the normal and light, should not be saturated or abs'd
 * @return BRDF color for the given dot products of the light, normal, and view direction
 */
half4 SLZSampleBDRFLUTHalfLambert( half NoV, half halfLambert)
{
    NoV = saturate(NoV);
    return BDRFLUTSAMPLER(half2(halfLambert, NoV));
}

/**
 * Samples a 2D BRDF lookup table with normal dot light on the horizontal axis
 * and normal dot view on the vertical axis, taking into account the shadow
 * attenuation by taking the min of the shadow attenuation and N dot L.
 *
 * @param bdrfLUT           BRDF lookup table texture
 * @param sampler_brdfLUT   sampler to use with the lookup table, probably should be a bilinear clamped sampler
 * @param NoV               dot of the normal and view, should not be saturated or abs'd (get it from fragData not surfData)
 * @param NoL               dot of the normal and light, should not be saturated or abs'd
 * @param shadowAttenuation shadow attenuation value associated with the light
 * @return BRDF color for the given dot products of the light, normal, and view direction
 */
half4 SLZSampleBDRFLUTShadow( half NoV, half NoL, half shadowAttenuation)
{
    half NoL2 = saturate((NoL + 1) * 0.5);
    //half NoV2 = saturate(NoV);
    half lineartocir = sqrt(shadowAttenuation); //replace with an s curve
    // if t < d / 2 then return outSine(t * 2, b, c / 2, d) end
    // return inSine((t * 2) -d, b + c / 2, c / 2, d)
    // \sqrt{-\left(x-1\right)^{2}+1}
    
    half clampedNoL2 = min(NoL2, lineartocir);
    return BDRFLUTSAMPLER(float2(clampedNoL2, saturate(NoV)));
}

/**
 * Diffuse BDRF for realtime lights, right now just does lambert but could be modified to do a more complex diffuse BDRF
 *
 *
 * @param fragData All relevant data relating to the fragment
 * @param surfData All relevant data relating to the surface properties at the fragment
 * @param lightColor Color of the realtime light
 */
diffuseLight SLZDiffuseBDRF(const SLZFragData fragData, const SLZSurfData surfData, const Light light)
{
    diffuseLight attenuatedLight = (diffuseLight)light.color * (light.distanceAttenuation * light.shadowAttenuation);
    diffuseLight diffuse = (diffuseLight)0;
    #if defined(_BRDFMAP)
        diffuse = SLZSampleBDRFLUTShadow( fragData.NoV, dot(fragData.normal, light.direction), light.shadowAttenuation) * (diffuseLight)attenuatedLight;
    #else
        diffuse = SLZLambertDiffuse(attenuatedLight, fragData.normal, light.direction);
    #endif
    return diffuse;
}

/** 
 * GGX normal distribution function optimized for half-precision. 
 * Uses Lagrange's identity (dot(cross(A, B), cross(A, B)) = length(A)^2 * length(B)^2 - dot(A, B)^2)
 * to avoid calculating 1 - dot(N, H)^2 which has severe precision issues when dot(N, H) is close to 1
 * See the Google Filament documentation https://google.github.io/filament/Filament.md.html#materialsystem/specularbrdf  
 *
 * @param NoH       Dot product of the normal with half view-light vector
 * @param NxH2      Cross-product of the normal and half view-light, dotted with itself  
 * @param roughness Non-perceptual roughness value
 * @return GGX normal distribution value
 */
half SLZGGXSpecularDMobile(half NoH, half NxH2, half roughness)
{	
    half a = NoH * roughness;
    half d = roughness / max(a * a + NxH2, REAL_MIN);
    half d2 = (d * d * SLZ_INV_PI_half);
    return d2;
}

/** 
 * GGX normal distribution function, optimised for full-precision
 *
 * @param NoH       Dot product of the normal with half view-light vector
 * @param roughness Non-perceptual roughness value
 * @return GGX normal distribution value
 */
float SLZGGXSpecularD(float NoH, float roughness)
{
    float a = NoH * roughness;
    float d = roughness / (a * a - NoH * NoH + 1.0);
    float d2 = (d * d * SLZ_INV_PI_half);
    return d2;
}

/**
 * Burley anisotropic NDF, optimized for mobile half precision.
 * 
 * Normal Burley aniso formula:
 * 
 * N = 1/(pi) * 1/(rT * rB) * 1/((ToH / rT)^2 + (BoH / rB)^2 + NoH^2)^2
 * 
 * There are several major sources of error here. First off NoH^2 is severely
 * lacking in precision around NoH close to 1, which is right where the 
 * specular highlight is. This is easy to fix, just use Lagrange's identity
 * to replace it with 1 - dot(cross(N,H),cross(N,H)), which has much better
 * precision where we need it. Secondly, we have the dots of the tangent/
 * bitangent divided by their anisotropic roughnesses. When the roughness is
 * low, these start aliasing heavily. If we multiply the equation by
 * (rT * rB) / (rT * rB), 
 * 
 * N = 1/pi * (rT * rB) / (rT * rB)^2 * 1/((ToH / rT)^2 + (BoH / rB)^2 + NoH^2)^2
 *   = 1/pi * (rT * rB) / ( (rT * rB) * ( (ToH^2 / rT^2) + (BoH^2 / rB^2) + NoH^2)^2
 *   = 1/pi * (rT * rB) / ( ToH^2 * (rB/rT) + BoH^2 * (rT/rB) + (rT * rB) * NoH^2)^2
 * 
 * Now instead of dividing the square of the tangent & bitangent dots by their
 * roughnesses, we are multiplying by the ratio of the two roughnesses. This
 * ratio can be reduced
 * 
 * A = rB / rT = (rI * (1 - a)) / (rI * (1 + a)) = (1 - a) / (1 + a)
 * 
 * where rI is the isotropic roughness, and a is the aniso factor. This ratio
 * no longer depend on roughness and only on the aspect ratio, and this removes
 * the aliasing issues related to these terms. 
 *
 * Finally division by ( A * ToH^2  + (1/A) * BoH^2 + (rT * rB) * NoH^2)^2 
 * also causes aliasing issues. When roughness is low, this term is exceedingly
 * tiny, and taking the reciprocal results in a very large number with severe
 * loss of floating point precision. However, the dividend is rT * rB, a small
 * number when roughness is low. If instead we use (sqrt(rT * rB))^2, we can
 * move sqrt(rT * rB) into the square term, then
 * 
 * N = 1/pi * ( sqrt(rT*rB) / ( A * ToH^2  + (1/A) * BoH^2 + (rT * rB) * NoH^2) )^2
 * 
 * Now the smallness of sqrt(rT*rB) cancels out the smallness of the sum of the dots,
 * the result is closer to 1, and the square of the number is significantly smaller
 * and does not get rounded as heavily.
 * 
 * @param NoH2          Square of the dot product of the normal with half view-light vector
 * @param ToH           Dot product of the tangent with half view-light vector
 * @param BoH           Dot product of the bitangent with half view-light vector
 * @param roughnessT    Anisotropic roughness value in the direction of the tangent
 * @param roughnessB    Anisotropic roughness value in the direction of the bitangent
 * @param aspectRatio   The anisotropic roughness aspect ratio, where -1
 *                      stretches the highlight along the bitangent, 0 is
 *                      isotropic, and 1 stretches it along the tangent
 * @return Anisotropic GGX normal distribution value 
 */
half SLZGGXSpecularDAniso(half NoH2, half ToH, half BoH, half roughnessT, half roughnessB, half aspectRatio)
{
    half roughProduct = roughnessT * roughnessB;
    half aspectTerm = (1.0h - aspectRatio) / (1.0h + aspectRatio); // roughnessB/roughnessT = (rough * (1 - aspectRatio))/(rough * (1 + aspectRatio)) 
    half2 aVec = half2(ToH * aspectTerm, BoH / aspectTerm);
    half b = dot(aVec, aVec) + NoH2 * roughProduct;
    half w2 = rcp(b * rsqrt(roughProduct));
    w2 *= w2;
    return min(w2 * SLZ_INV_PI_half, 100);
}

/**
 * Kelemen and Szirmay-Kalos (KSK) visibility with J. Hable's roughness term, acts as both the visibility and fresnel functions
 * This is unity's default for the URP. Extremely cheap, perfect for mobile.
 * See https://community.arm.com/events/1155 "Optimizing PBR for Mobile" for more details
 *
 * @param LoH         Dot product of the light direction with the half view-light vector
 * @param roughness   Surface roughness (non-perceptual)
 * @return pre-multiplied geometic shadowing and fresnel terms of the specular BDRF
 */
half SLZFusedVFMobile(half LoH, half roughness)
{
    half LoH2 = LoH * LoH;
    return rcp(max(half(0.1), LoH2) * (half(4.0) * roughness + half(2.0)));
}


/** 
 * Heitz height-correlated Smith-GGX visibility function (specular geometric shadowing)
 *
 * @param NoV       Normal-view dot product
 * @param NoL       Normal-light dot product
 * @param roughness Non-perceptual roughness
 * @return Geometric shadowing term of the specular BDRF
 */
half SLZSmithVisibility(half NoV, half NoL, half roughness)
{
    half rough2 = roughness * roughness;
    NoL = abs(NoL) + 1e-5;  //The baked specular falloff function needs saturate(NoL), so NoL is stored raw and needs to be abs'd here 
    half v = NoL * sqrt(NoV * (-rough2 * NoV + 1.0h) + rough2);
    half l = NoV * sqrt(NoL * (-rough2 * NoL + 1.0h) + rough2);
    return half(0.5) / (v + l);
}

/**
 * Heitz height-correlated, anisotropic Smith-GGX visibility function (specular geometric shadowing)
 * taken from Google Filament.
 * 
 * @param NoV           Normal-view dot product
 * @param NoL           Normal-light dot product
 * @param ToL           Tangent-light dot product
 * @param BoL           Bitangent-light dot product
 * @param visLambdaView Precalculated term, stored in fragData and calculated
 *                      by SLZFragDataAddAniso
 * @param roughnessT    roughness in the tangent direction
 * @param roughnessB    roughness in the bitangent direction
 */
half SLZSmithVisibilityAniso(half NoV, half NoL, half ToL, half BoL, half visLambdaView, half roughnessT, half roughnessB)
{
    NoL = abs(NoL) + 1e-5;
    half lambdaV = NoL * visLambdaView;
    half lambdaL = length(half3(roughnessT * ToL, roughnessB * BoL, NoL));
    return 0.5 / (lambdaL + lambdaV);
}

/** 
 * Schlick Fresnel function
 *
 * @param LoH       Dot product of light direction with half light-view vector  
 * @param specColor Base specular color
 */ 
half3 SLZSchlickFresnel(half LoH, half3 specColor)
{
    half iLoH = 1.0 - LoH;
    half iLoH5 = pow(iLoH, 5.0);
    return specColor * (1.0 - iLoH5) + iLoH5;
}

/** 
 * Modified version of unity's specular BRDF function, directly takes dot/cross product values instead of internally calculating them
 * so they can be reused if need be, and more importantly fixed issues with half-precision on mobile and simplified/removed unnecessary
 * casts to full precision floats
 *
 * @param NoH       Dot product of normal and half view-light vector
 * @param LoH       Dot product of light direction and half view-light vector
 * @param NxH2      Dot product of the cross product of the normal and half view-light vector with itself
 * @param roughness Surface roughness (not perceptual)
 * @return Specular highlight intensity
 */

half SLZDirectBRDFSpecularMobile(half NoH, half LoH, half NxH2, half roughness)
{
    // GGX Distribution multiplied by combined approximation of Visibility and Fresnel
    // BRDFspec = (D * V * F) / 4.0
    // D = roughness^2 / ( NoH^2 * (roughness^2 - 1) + 1 )^2 // Wrong! Unity forgot the 1/pi term in their specular D!
    // V * F = 1.0 / ( LoH^2 * (roughness + 0.5) )
    // See "Optimizing PBR for Mobile" from Siggraph 2015 moving mobile graphics course
    // https://community.arm.com/events/1155

    // Final BRDFspec = roughness^2 / ( NoH^2 * (roughness^2 - 1) + 1 )^2 * (LoH^2 * (roughness + 0.5) * 4.0) 
   
    half NDF = SLZGGXSpecularDMobile(NoH, NxH2, roughness);
    half VF  = SLZFusedVFMobile(LoH, roughness);
    half specularTerm = (NDF * VF);

    
    #if defined(SHADER_API_MOBILE)
        // On platforms where half actually means something, the denominator has a risk of overflow
        // clamp below was added specifically to "fix" that, but dx compiler (we convert bytecode to metal/gles)
        // sees that specularTerm have only non-negative terms, so it skips max(0,..) in clamp (leaving only min(100,...))
        specularTerm = specularTerm - REAL_MIN;
        specularTerm = clamp(specularTerm, 0.0, 100.0); // Prevent FP16 overflow on mobiles
    #else
        specularTerm = max(0, specularTerm);
    #endif    

    return specularTerm;
}

/**
 * High quality specular BDRF, for use on PC. Uses the same GGX N, D, and F as google filament
 */
float3 SLZDirectBRDFSpecularHighQ(float NoH, float NoV, float NoL, float LoH, float roughness, float3 specColor)
{
    roughness = 0.999f * roughness + 0.001f; // remap to [0.01,1] to prevent specular aliasing
	float N = SLZGGXSpecularD(NoH, roughness);
	float D = SLZSmithVisibility(NoV, NoL, roughness);
	float3 F = SLZSchlickFresnel(LoH, specColor);
    return N * D * F;
}

half3 SLZDirectBRDFSpecularHighQ16(half NoH, half NxH2, half NoV, half NoL, half LoH, half roughness, half3 specColor)
{
	roughness = half(0.999) * roughness + half(0.001); // remap to [0.01,1] to prevent specular aliasing
	half N = SLZGGXSpecularDMobile(NoH, NxH2, roughness);
	half D = SLZSmithVisibility(NoV, NoL, roughness);
	half3 F = SLZSchlickFresnel(LoH, specColor);
    
	half3 specularTerm = (N * D * F) - HALF_MIN;
	specularTerm = clamp(specularTerm, 0.0, 100.0);
    
	return specularTerm;
}

half3 SLZAnisoDirectBRDFSpecular(SLZDirectSpecLightInfo lightInfo, SLZSurfData surfData, half NoV, half visLambdaView)
{
#if defined(_SLZ_ANISO_SPECULAR)
    half N = SLZGGXSpecularDAniso(lightInfo.NoH2, lightInfo.ToH, lightInfo.BoH, surfData.roughnessT, surfData.roughnessB, surfData.anisoAspect);
    half D = SLZSmithVisibilityAniso(NoV, lightInfo.NoL, lightInfo.ToL, lightInfo.BoL, visLambdaView, surfData.roughnessT, surfData.roughnessB);
    half3 F = SLZSchlickFresnel(lightInfo.LoH, surfData.specular);
    half3 specularTerm = N * D * F;

#if defined(SHADER_API_MOBILE) || defined(UNITY_UNIFIED_PRECISION_MODEL)
    // On platforms where half actually means something, the denominator has a risk of overflow
    // clamp below was added specifically to "fix" that, but dx compiler (we convert bytecode to metal/gles)
    // sees that specularTerm have only non-negative terms, so it skips max(0,..) in clamp (leaving only min(100,...))
    specularTerm = specularTerm - REAL_MIN;
    specularTerm = clamp(specularTerm, 0.0, 100.0); // Prevent FP16 overflow on mobiles
#else
    specularTerm = max(0, specularTerm);
#endif
    return specularTerm;
#else
    return half3(0, 0, 0);
#endif
}

/**
 * Specular BRDF, 
 *
 * @param specInfo Struct containing the dot products of the normal and light with the half light-view
 * @param surfData Surface information
 * @return Specular color
 */
half3 SLZDirectBRDFSpecular(SLZDirectSpecLightInfo specInfo, SLZSurfData surfData, SLZFragData fragData)
{
    half3 specular;
    #if defined(_SLZ_ANISO_SPECULAR)
    specular = SLZAnisoDirectBRDFSpecular(specInfo, surfData, fragData.NoV, fragData.visLambdaView);
    #elif defined(SHADER_API_MOBILE) || defined(USE_MOBILE_BRDF)
        specular = surfData.specular * SLZDirectBRDFSpecularMobile(specInfo.NoH, specInfo.LoH, specInfo.NxH2, surfData.roughness);
    #elif defined(UNITY_UNIFIED_SHADER_PRECISION_MODEL)
        specular = SLZDirectBRDFSpecularHighQ16(specInfo.NoH, specInfo.NxH2, specInfo.NoV, specInfo.NoL, specInfo.LoH, surfData.roughness, surfData.specular);
    #else
	    specular = SLZDirectBRDFSpecularHighQ(specInfo.NoH, specInfo.NoV, specInfo.NoL, specInfo.LoH, surfData.roughness, surfData.specular);
    #endif

    #if defined(ANIME)
    #if defined(_BRDFMAP)
        half3 bdrfTerm = SAMPLE_TEXTURE2D_LOD(g_tBRDFMap, sampler_LinearClamp, float2(specular.r, specInfo.NoH), 0).rgb;
        specular += bdrfTerm;
    #endif
    #endif

    return specular;
}

half SLZRetroreflectionLobe(half3 viewDir, half3 lightDir, half sharpness)
{
    half3 halfDir = SLZSafeHalf3Normalize(lightDir + viewDir);
    half NoH = saturate(dot(viewDir, halfDir));
    half3 NxH = cross(viewDir, half(4.0) * halfDir);
    half NxH2 = saturate(dot(NxH, NxH) * half(0.0625h));
    half retroLobe = SLZGGXSpecularDMobile(NoH, NxH2, half(1.0) - sharpness);
    return retroLobe;
}

//------------------------------------------------------------------------
//------------------------------------------------------------------------
// Lighting Functions
//------------------------------------------------------------------------
//------------------------------------------------------------------------

/**
 * Multiplier to fade out estimated specular highlights from global illumination when the normal faces away from
 * the estimated light direction. The specular BRDF actually produces two highlights, one facing the light and one 
 * opposite it. With punctual lights, multiplying by the lambert diffuse zeros out the unwanted highlight. When doing 
 * fake punctual specular based on global illuination sources like lightmaps or spherical harmonics, the diffuse light 
 * recieved by fragments facing away from the fake light direction is not 0 so the negative highlight shows up. To get 
 * rid of it, we can multiply by some factor of N dot L. Just multiplying by NoL will darken the specular too much in most
 * situations as the lambert diffuse formula is already baked into the GI's color., but if we 
 * take the square
 * root of N dot L the darkening will not be significant until very grazing angles. 1 - (1 - N dot L)^2 has a
 * similar shape but avoids the square root.
 * 
 * @param NoL Saturated dot product of the normal and light direction
 * @return scale to multiply the intensity of the specular by
 */
half SLZFakeSpecularFalloff(half NoL)
{
	half NoLMul = half(1.0) - saturate(NoL); // On PC, the smith visibility function needs abs(NoL), so NoL is stored raw and needs to be saturated here 
	NoLMul = -NoLMul * NoLMul + half(1.0);
    return NoLMul;
}



/**
 * Directionalizes the lighting information from the lightmap, interpolating between lambert diffuse and non-directional lighting
 * using the length of the unnormalized directional lightmap vector and a re-normalization factor stored in the alpha channel
 *
 * @param lightmapColor         Base lightmap color
 * @param lmDirection           Decoded and not normalized direction vector stored in the directional map (2.0 * dirMap.rgb - 1.0)
 * @param normal                Worldspace normal
 * @param directionalityFactor  Alpha of the directional map, used to make the lighting less directional in combination with the length of lmDirection
 * @return Lightmap color, attenuated by the light direction to the strength of the directionality encoded in the directional map
 */
half3 SLZApplyLightmapDirectionality(half3 lightmapColor, half3 lmDirection, half3 normal, half directionalityFactor)
{ 
    #if defined(SLZ_LM_MONOSH) // Bakery monochrome linear spherical harmonic directional lightmaps. TODO: non-linear mode
    
    half3 s0 = (2.0 * lmDirection) * normal;
    return lightmapColor + s0.x * lightmapColor + s0.y * lightmapColor + s0.z * lightmapColor;
    
    #else // Default unity half-lambert directional maps (gross)
    
    half halfLambert = dot(normal, 0.5h * lmDirection) + half(0.5);
    return lightmapColor * halfLambert / max(half(1e-4), directionalityFactor);
    
    #endif
}

/**
 * Directionalizes the lighting information from the lightmap, interpolating between lambert diffuse and non-directional lighting
 * using the length of the unnormalized directional lightmap vector and a re-normalization factor stored in the alpha channel
 *
 * @param lightmapColor         Base lightmap color
 * @param lmDirection           Decoded and not normalized direction vector stored in the directional map (2.0 * dirMap.rgb - 1.0)
 * @param normal                Worldspace normal
 * @param directionalityFactor  Alpha of the directional map, used to make the lighting less directional in combination with the length of lmDirection
 * @return Lightmap color, attenuated by the light direction to the strength of the directionality encoded in the directional map
 */
half3 SLZApplyLightmapDirectionalityBRDFLUT(const half3 lightmapColor, const half3 lmDirection, const half3 normal, const half directionalityFactor,
    const SLZFragData fragData, const SLZSurfData surfData)
{
#if defined(_BRDFMAP)
    half halfLambert = (dot(normal, 0.5h * lmDirection) + half(0.5)) / max(half(1e-4), directionalityFactor);
    half3 brdfLUT = SLZSampleBDRFLUTHalfLambert( fragData.NoV, halfLambert);
    return lightmapColor * brdfLUT;
#else
    return half3(0, 0, 0);
#endif
}

#define SLZ_LM_R_MIN  0.4
#define SLZ_LM_R_MAX  0.75
#define SLZ_LM_D_MIN  0.4
#define SLZ_LM_D_MAX  0.5

float2 IQTextureNiceUVDistort(float2 uv, float2 textureResolution)
{
    uv = uv * textureResolution + 0.5;
    float2 iuv = floor(uv);
    float2 fuv = frac(uv);
    uv = iuv + fuv * fuv * (3.0 - 2.0 * fuv);
    uv = (uv - 0.5) / textureResolution;
    return uv;
}



/**
 * Reads the lightmap, directional lightmap, and dynamic lightmap, and calculates the total diffuse lighting from them as well
 * as calculating a specular highlight using the directional map if present  
 *
 * @param[in,out] diffuse  Total diffuse lighting from the main and dynamic lightmaps
 * @param[in,out] specular Specular lighting, calculated if directional lightmapping is on
 * @param         frag     Struct containing all relevant fragment data (lightmap uvs, normal and view vectors, etc)
 * @param         surf     struct containing PBR surface information for the specular calculations
 */
void SLZGetLightmapLighting(inout half3 diffuse, inout half3 specular, const SLZFragData frag, inout SLZSurfData surf)
{
    #if defined(SLZ_LM_BICUBIC)
        half3 lmDiffuse = SampleLightmapBSpline(unity_Lightmap, samplerunity_Lightmap, frag.lightmapUV);
    #else
        half3 lmDiffuse = SAMPLE_TEXTURE2D(unity_Lightmap, samplerunity_Lightmap, frag.lightmapUV).rgb;
    #endif
    
	specular *= BakedLightingToSpecularOcclusionGray(lmDiffuse);
    
    #if defined(DIRLIGHTMAP_COMBINED)
            half4 directionalMap = SAMPLE_TEXTURE2D(unity_LightmapInd, samplerunity_Lightmap, frag.lightmapUV);
            half3 lmDirection = half(2.0) * directionalMap.xyz - half(1.0);


            #if defined(_BRDFMAP)
            lmDiffuse = SLZApplyLightmapDirectionalityBRDFLUT(lmDiffuse, lmDirection, frag.normal, directionalMap.w, frag, surf);
            #else
            lmDiffuse = max(half(0), SLZApplyLightmapDirectionality(lmDiffuse,lmDirection, frag.normal, directionalMap.w));
            #endif
            
            #if !defined(_SLZ_DISABLE_BAKED_SPEC) && !defined(SLZ_NO_SPECULAR)
                // the length of lmDirection controls the strength of the directionality. 
                // Baking a lightmap in a white furnace yields a length of 0.66.
                // Interpolate specular towards 0 as the length approaches this value
                #ifdef SLZ_LM_MONOSH 
                half maxDirectionality = half(0.54);
                #else
                half maxDirectionality = half(0.66);
                #endif
                half directionality = saturate((length(lmDirection) - maxDirectionality) / (half(1.0) - maxDirectionality));
                lmDirection = SLZSafeHalf3Normalize(lmDirection); //length not 1
                SLZDirectSpecLightInfo lightInfo = SLZGetDirectLightInfo(frag, lmDirection);
    
                half3 lmSpecular = SLZDirectBRDFSpecular(lightInfo, surf, frag);
                specular += max(half(0), lmDiffuse * lmSpecular * lightInfo.NoL * directionality);
            #endif
    #endif
    
    diffuse += lmDiffuse;

    #if defined(DYNAMICLIGHTMAP_ON)
        half3 dynLmDiffuse = SAMPLE_TEXTURE2D(unity_DynamicLightmap, samplerunity_DynamicLightmap, frag.dynLightmapUV).rgb;
        #if defined(DIRLIGHTMAP_COMBINED) && !defined(SHADER_API_MOBILE)
            half4 dynDirectionalMap = SAMPLE_TEXTURE2D(unity_DynamicDirectionality, samplerunity_DynamicLightmap, frag.dynLightmapUV);
            half3 dynLmDirection = half(2.0) * dynDirectionalMap.rgb - half(1.0);
            half dynDirectionality = saturate((length(dynLmDirection) - 0.66h) / (1.0h - 0.66h));
            dynLmDiffuse = SLZApplyLightmapDirectionality(dynLmDiffuse,dynLmDirection, frag.normal, dynDirectionalMap.w);
            #if !defined(_SLZ_DISABLE_BAKED_SPEC) && !defined(SLZ_NO_SPECULAR)
                dynLmDirection = SLZSafeHalf3Normalize(dynLmDirection); //length not 1
                SLZDirectSpecLightInfo dynLightInfo = SLZGetDirectLightInfo(frag, dynLmDirection);          
                half3 dynLmSpecular = SLZDirectBRDFSpecular(dynLightInfo, surf, frag);
                specular += dynLmDiffuse * dynLmSpecular * dynLightInfo.NoL * dynDirectionality;
            #endif
        #endif
    
        diffuse += dynLmDiffuse;
    #endif
}

/*
* Evaluate a single channel of the L0 + L1 spherical harmonic at a given normal.
* Use this when you want to evaluate SH with a different normal per channel
* (e.g. softened normals for SSS-style channel-shifted ambient diffuse).
*
* @param N   Worldspace normal to evaluate at
* @param shA One of unity_SHAr / unity_SHAg / unity_SHAb
* @return    L0 + L1 contribution for that channel
*/
real SHEvalLinearL0L1Channel(real3 N, real4 shA)
{
    return dot(shA, real4(N, 1.0));
}
/**
 * Evaluate a single channel of the L2 SH at a given normal.
 * Use when evaluating SH with different normals per channel
 * (e.g. softened normals for SSS-style channel-shifted ambient diffuse).
 *
 * @param N         World   space normal to evaluate at
 * @param shB       One of unity_SHBr / unity_SHBg / unity_SHBb
 * @param shCChan   Corresponding channel of unity_SHC (.r/.g/.b)
 * @return          L2 contribution for that channel
 */
real SHEvalLinearL2Channel(real3 N, real4 shB, real shCChan)
{
    real4 vB = N.xyzz * N.yzzx;
    real  x2 = dot(shB, vB);
    real  vC = N.x * N.x - N.y * N.y;
    return x2 + shCChan * vC;
}

/**
 * Add to the diffuse the light from spherical harmonics
 *
 * @param[in,out] diffuse  Current total diffuse light
 * @param         normal   Worldspace normal vector
 */
void SLZSHDiffuse(inout half3 diffuse, half3 normal)
{
    #if !defined(LIGHTMAP_ON) 
        #if defined(EVALUATE_SH_VERTEX) // all of spherical harmonics are calculated in the vertex program
           // do nothing 
        #else // Calculate all or some of the SH in the frag
    
            #if defined(_BRDFMAP)
            // Soften N toward the L1-derived ambient gather direction
            // Larger softening factor = larger wraparound (deeper scatter)
            // Dominant direction from luminance-summed L1
            float3 l1Vec = unity_SHAr.xyz + unity_SHAg.xyz + unity_SHAb.xyz;
            float  l1LenSq = max(dot(l1Vec, l1Vec), FLT_MIN);
            half3  dominantDir = (half3)(l1Vec * rsqrt(l1LenSq));
            
            // Soften normal per channel — red scatters deepest, blue not at all.
            // Tune these to taste / expose as material params.
            half3 nR = SLZSafeHalf3Normalize(lerp(normal, dominantDir, _SSSColor.r));
            half3 nG = SLZSafeHalf3Normalize(lerp(normal, dominantDir, _SSSColor.g));
            half3 nB = SLZSafeHalf3Normalize(lerp(normal, dominantDir, _SSSColor.b));
            
            half3 shL0L1 ;
            shL0L1.r = SHEvalLinearL0L1Channel(nR, unity_SHAr);
            shL0L1.g = SHEvalLinearL0L1Channel(nG, unity_SHAg);
            shL0L1.b = SHEvalLinearL0L1Channel(nB, unity_SHAb);
            #else 
            half3 shL0L1 = SHEvalLinearL0L1(normal, unity_SHAr, unity_SHAg, unity_SHAb);
            #endif
            #if defined(EVALUATE_SH_MIXED) // In mixed mode, the L2 component is calculated in the vertex
                half3 shL2 = half3(0,0,0);
            #else
                #if defined(_BRDFMAP)
                half3 shL2;
                shL2.r = SHEvalLinearL2Channel(nR, unity_SHBr, unity_SHC.r);
                shL2.g = SHEvalLinearL2Channel(nG, unity_SHBg, unity_SHC.g);
                shL2.b = SHEvalLinearL2Channel(nB, unity_SHBb, unity_SHC.b);
                #else
                half3 shL2 = SHEvalLinearL2(normal, unity_SHBr, unity_SHBg, unity_SHBb, unity_SHC);
                #endif
    
            #endif
            diffuse += shL2 + shL0L1;
            //shL1 += shL0;
        #endif
    #endif
}

/**
 * A crude attempt to get a specular highlight from light probes. The L1 ceofficient is similar in shape
 * to the diffuse shading from a single light, so it stands to reason that the L1 will often represent
 * the light contribution from a single source. Averaging the L1 r,g,b vectors will give us a light direction
 * we can use to create a specular highlight. This works well in many situations, but there are also plenty
 * of others where it falls apart. If there isn't a strong uni-directional component to the light, the L1
 * vectors will point in seemingly random and rapidly spatially varying directions.
 * Still better than nothing. Baking direct specular into reflection probes by giving light sources physical
 * emissive meshes the probe can see is a superior solution in many situations. However it needs box projection
 * and probe blending to look good, which we aren't doing on the quest.
 * 
 * @return direction Average direction of the spherical harmonic L1 vectors, normalized 
 */
half3 SLZSHSpecularDirection()
{
    float3 direction = (unity_SHAr.xyz + unity_SHAg.xyz + unity_SHAb.xyz);
    float lengthSq = max(float(dot(direction, direction)), FLT_MIN);
    float invLength = rsqrt(lengthSq);
    direction = direction * invLength;
    return direction;
}

half3 SLZProbeReflectionDir(SLZFragData fragData, SLZSurfData surfData)
{
#if defined(_SLZ_ANISO_SPECULAR)
    half3 anisoDir = surfData.anisoAspect > 0 ? fragData.bitangent : fragData.tangent;
    half viewSign = surfData.anisoAspect > 0 ? 1 : -1;
    half3 anisoTangent = cross(anisoDir, fragData.viewDir);
    half3 anisoNormal = cross(anisoTangent, anisoDir);
    half3 bentNormal = normalize(lerp(fragData.normal, anisoNormal, abs(surfData.anisoAspect)));
    
    return reflect(-fragData.viewDir, bentNormal);
#else
    return reflect(-fragData.viewDir, fragData.normal);
#endif
}

/**
 * Specular from reflection probes. Mostly copying unity's code here, but without their data structures.
 * 
 *
 * @param[in,out] specular  Running total of the specular color
 * @param         fragData  Struct containing all relevant fragment data (normal, position, etc)
 * @param         surfData  Struct containing physical properties of the surface (specular color, roughness, etc)
 * @param         indSSAO   Indirect screenspace ambient occlusion, not used if SSAO isn't enabled
 */
void SLZImageBasedSpecular(half3 diffuse, inout half3 specular, half3 reflectionDir, const SLZFragData fragData, const SLZSurfData surfData, half indSSAO)
{
    // On mobile, do a hard switch between the retroreflection direction/roughness and the normal reflection direction
    // PC will calculate a separate retroreflection lobe for the image-based lighting
    half roughness = surfData.roughness;
    #if defined(_RETROREFLECTIVE) && defined(SHADER_API_MOBILE)
    if (surfData.retroReflPercent > 0.2)
    {
        reflectionDir = fragData.viewDir;   
        roughness = 1.0 - surfData.retroReflSharpness;
    }
    #endif

   // half3 LitSpecularOcclusion = BakedLightingToSpecularOcclusion(diffuse);
    half AOSpecularOcclusion = GetSpecularOcclusionFromAmbientOcclusion(fragData.NoV, surfData.occlusion, roughness);
	half3 reflectionProbe = GlossyEnvironmentReflection(reflectionDir, fragData.position, sqrt(roughness), AOSpecularOcclusion, fragData.screenUV);// * LitSpecularOcclusion;

	half surfaceReduction = half(1.0h) / (roughness * roughness + half(1.0h));
	half3 grazingTerm = saturate((half(1.0h) - sqrt(roughness)) + surfData.reflectivity);
	half fresnelTerm = (half(1.0h) - saturate(fragData.NoV));
    fresnelTerm *= fresnelTerm;
    fresnelTerm *= fresnelTerm; // fresnelTerm ^ 4

    half3 IBSpec = half3(surfaceReduction * lerp(surfData.specular, grazingTerm, fresnelTerm));

    #if defined(_RETROREFLECTIVE) && defined(SHADER_API_MOBILE)
    if (surfData.retroReflPercent > 0.2)
    {
        IBSpec = surfData.retroReflPercent * saturate(fragData.NoV);
    }
    #endif

    
    UNITY_BRANCH if (BRANCH_SCREEN_SPACE_OCCLUSION)
    {
        reflectionProbe *= indSSAO;
    }
    
    

    specular += IBSpec * reflectionProbe;
}


/**
 * Specular horizon occlusion factor copied from Filament, which fades out the specular
 * as the reflected ray dips below the surface. This is possible because the normal from
 * normal maps and even smooth interpolated mesh normals aren't geometrically sane.
 * The camera can be below the plane defined by the pixel's normal but still be above 
 * the triangle's true normal plane, meaning that we're still rendering the geometry from
 * the front but the shader thinks were looking at the back. This leads to a reflection
 * vector pointing into the surface.
 *
 * @param   normal          Worldspace normal vector
 * @param   reflectionDir   Reflection vector
 */
half SLZSpecularHorizonOcclusion(half3 normal, half3 reflectionDir)
{
	half horizonOcclusion = min(half(1.0h) + dot(reflectionDir, normal), half(1.0h));
    return horizonOcclusion * horizonOcclusion;
}

// On Quest, when sampling diffuse lighting from probes calculate a specular highlight from either
// the directional light if it is present or the probe's L1 vector if not. If on PC, if specular from
// baked sources (ie L1) is disabled, if shadowmasking is on, or if using non-directional lightmaps
// always calculate the directional light's highlight separately.
#if defined(SHADER_API_MOBILE) && !defined(LIGHTMAP_ON) && !defined(_SLZ_DISABLE_BAKED_SPEC)
    #define USE_EITHER_DIR_OR_SH_SPEC
#elif !defined(SHADER_API_MOBILE) || defined(_SLZ_DISABLE_BAKED_SPEC) || defined(SHADOWS_SHADOWMASK) || (defined(LIGHTMAP_ON) && !defined(DIRLIGHTMAP_COMBINED))
    #define USE_BOTH_DIR_AND_SH_SPEC
#endif

/**
 * Primary diffuse and specular light. Assumes that only the lightmap or spherical harmonic lighting is in the diffuse
 * parameter to begin with. Adds diffuse and specular light from the directional light if its intensity is non-0. Otherwise,
 * if the object isn't lightmapped then the specular is estimated from the spherical harmonics. Avoids having to do a
 * specular highlight for both the directional light and spherical harmonics, especially considering most of the time
 * scenes are fully baked so always calculating a highlight for the directional light is a waste (quest is incapable
 * of real branching, so we can't conditionally calculate it).
 *
 * @param[in,out] diffuse   Running total of the diffuse light color
 * @param[in,out] specular  Running total of the specular color
 * @param         fragData  Struct containing all relevant fragment data (normal, position, etc)
 * @param         surfData  Struct containing physical properties of the surface (specular color, roughness, etc)
 * @param         directSSAO Direct screen-space ambient occlusion factor       
 */
void SLZMainLight(inout diffuseLight diffuse, inout half3 specular, const SLZFragData fragData, const SLZSurfData surfData, half directSSAO)
{
    Light mainLight = GetMainLight(fragData.shadowCoord, fragData.position, fragData.shadowMask);
    diffuseLight diffuseBRDF = SLZDiffuseBDRF(fragData, surfData, mainLight);

    UNITY_BRANCH if (BRANCH_SCREEN_SPACE_OCCLUSION)
    {
        diffuseBRDF *= directSSAO;
    }
    
    #if !defined(SLZ_NO_SPECULAR)
        #if defined(USE_EITHER_DIR_OR_SH_SPEC) || defined(_RETROREFLECTIVE)
        
            bool isMainLight = any(mainLight.color.rgb * mainLight.shadowAttenuation > REAL_MIN) ? true : false;
            half3 shL1Dir = SLZSHSpecularDirection();
            half3 dominantDir = isMainLight ? mainLight.direction : shL1Dir;
            SLZDirectSpecLightInfo specInfo = SLZGetDirectLightInfo(fragData, dominantDir);
            diffuseLight dominantColor = isMainLight ? diffuseBRDF : (diffuseLight)max(half(0.0), diffuse);
            half NoLMul = SLZFakeSpecularFalloff(specInfo.NoL);
            NoLMul = isMainLight ? half(1.0h) : NoLMul;
            dominantColor *= NoLMul;
            specular += dominantColor.rgb * SLZDirectBRDFSpecular(specInfo, surfData, fragData) * NoLMul;

            #if defined(_RETROREFLECTIVE)
                diffuse *= (1.0-surfData.retroReflPercent);
                diffuse += (1.0-surfData.retroReflPercent)*diffuseBRDF;
                half retroLobe = SLZRetroreflectionLobe(fragData.viewDir, dominantDir, surfData.retroReflSharpness);
                diffuse += saturate(fragData.NoV) * (isMainLight ? 1 : (1.0/(2*PI))) * surfData.retroReflPercent * retroLobe * dominantColor;
            #endif
        #elif defined(USE_BOTH_DIR_AND_SH_SPEC)

            #if !defined(SHADER_API_MOBILE) && !defined(LIGHTMAP_ON) && !defined(_SLZ_DISABLE_BAKED_SPEC)
                half3 shL1Dir = SLZSHSpecularDirection();
                SLZDirectSpecLightInfo shSpecInfo = SLZGetDirectLightInfo(fragData, shL1Dir);
                half shNoLMul = SLZFakeSpecularFalloff(shSpecInfo.NoL);
                specular += max(half(0.0), diffuse) * SLZDirectBRDFSpecular(shSpecInfo, surfData, fragData) * shNoLMul;
            #endif

            [branch] if (any(_MainLightColor.rgb > REAL_MIN))
            {
                SLZDirectSpecLightInfo specInfo = SLZGetDirectLightInfo(fragData, mainLight.direction);
                specular += diffuseBRDF.rgb * SLZDirectBRDFSpecular(specInfo, surfData, fragData);
            }

        #endif
    #endif
    diffuse += diffuseBRDF;

}

/**
 * Calculates diffuse and specular lighting from additional lights
 *
 * @param[in,out] diffuse   Running total of the diffuse light color
 * @param[in,out] specular  Running total of the specular color
 * @param[in,out] maxSpecular Running max light source's direction and color (only used if SLZ_MONO_SPECULAR is defined)
 * @param         fragData  Struct containing all relevant fragment data (normal, position, etc)
 * @param         surfData  Struct containing physical properties of the surface (specular color, roughness, etc)
 * @param         addLight  Struct containing the information about a given light (color, attenuation, shadowing, etc)
 * @param         directSSAO Direct screen-space ambient occlusion factor    
 */
void SLZAddLight(inout diffuseLight diffuse, inout half3 specular, const SLZFragData fragData, const SLZSurfData surfData, Light addLight, half directSSAO)
{
    diffuseLight diffuseBRDF = SLZDiffuseBDRF(fragData, surfData, addLight);
    UNITY_BRANCH if (BRANCH_SCREEN_SPACE_OCCLUSION)
    {
        diffuseBRDF *= directSSAO;
    }

    #if defined(_RETROREFLECTIVE)
        
        // half cosView = saturate(dot(fragData.viewDir, addLight.direction));
        // half s = rcp(half(1.001)-_RetroRefl);
        // half sharpness = (s - 1.0) + 0.0001;
        // 
        // half vmf = (max(1.0, sharpness) / (2 * PI /* * (1.0 - exp(-2*lambda))*/)) * exp(sharpness * (cosView - 1.0f));
        // //vmf *= 4*PI;
        // // integral of vMF is 1 over the full sphere, not the hemisphere! however, we can calculate
        // // a sharpness threshold where the vMF is smaller than some epsilon beyond cosView = 0. 
        // // See https://therealmjp.github.io/posts/sg-series-part-2-spherical-gaussians-101/
        // // above this threshold, the integral over the hemisphere will approximately be 1. Above this
        // // threshold, just lerp to a flat value of 1.0 
        // //const float threshold = 5.65;
        // //float lerpToOne = saturate((sharpness - threshold)/(1.0 - threshold));
        // //vmf = lerp(vmf, 1.0, lerpToOne);

        // 
        diffuse += diffuseBRDF * (1.0 - surfData.retroReflPercent);
    #else
        diffuse += diffuseBRDF;
    #endif

    #if defined(_RETROREFLECTIVE)
        half retroLobe = SLZRetroreflectionLobe(fragData.viewDir, addLight.direction, surfData.retroReflSharpness);
        diffuse += saturate(fragData.NoV) * surfData.retroReflPercent * retroLobe * diffuseBRDF;
    #endif

    #if !defined(SLZ_NO_SPECULAR)
        SLZDirectSpecLightInfo specInfo = SLZGetDirectLightInfo(fragData, addLight.direction);
        specular += diffuseBRDF.rgb * SLZDirectBRDFSpecular(specInfo, surfData, fragData);
    #endif

}


struct ForwardPlusMacroFix
{
    float2 normalizedScreenSpaceUV;
    float3 positionWS;
};


/**
 * Full PBR lighting calculation
 *
 * @param  fragData    Struct containing all relevant fragment data (normal, position, etc)
 * @param  surfData    Struct containing physical properties of the surface (specular color, roughness, etc)
 * @param  surfaceType int indicating if the surface is opaque (0), transparent (1), fade (2). In transparent, 
 *                     the blend mode is presumed to be alpha premultiplied, and the diffuse is multiplied by
 *                     the alpha
 * @return PBR lit surface color
 */
half4 SLZPBRFragment(SLZFragData fragData, SLZSurfData surfData, int surfaceType = 0)
{
    diffuseLight diffuse = (diffuseLight)0;
    half3 specular = (half3)0;
   
    
        //-------------------------------------------------------------------------------------------------
    // Image-based specular
    //-------------------------------------------------------------------------------------------------
#if !defined(SLZ_NO_SPECULAR)
	half3 reflectionDir = SLZProbeReflectionDir(fragData, surfData);

	SLZImageBasedSpecular(diffuse, specular, reflectionDir, fragData, surfData, 1.0);// ao.indirectAmbientOcclusion);

	half occlusionFactor = SLZSpecularHorizonOcclusion(fragData.normal, reflectionDir);
	specular *= occlusionFactor;
    #endif
    
	if (surfaceType == 1)
	{
		surfData.alpha = lerp(surfData.alpha, half(1.0h), surfData.reflectivity);
		half fresnelTerm = (half(1.0h) - saturate(fragData.NoV));
		fresnelTerm *= fresnelTerm;
		fresnelTerm *= fresnelTerm;
		surfData.alpha = lerp(surfData.alpha, 1, fresnelTerm);
#if !defined(SLZ_NO_SPECULAR)
		surfData.alpha *= occlusionFactor;
#endif
	}

    //half2 dfg = SLZDFG(fragData.NoV, surfData.roughness);
       
    #if defined(LIGHTMAP_ON) 
    //-------------------------------------------------------------------------------------------------
    // Lightmapping diffuse and specular calculations
    //-------------------------------------------------------------------------------------------------
        
        SLZGetLightmapLighting(diffuse.rgb, specular, fragData, surfData);
    
    #else 
    //-------------------------------------------------------------------------------------------------
    // Spherical harmonic diffuse calculations
    //-------------------------------------------------------------------------------------------------
        
        SLZSHDiffuse(diffuse.rgb, fragData.normal);
	    specular *= BakedLightingToSpecularOcclusionGray(diffuse);
    #endif
    
    diffuse.rgb += fragData.vertexLighting; //contains both vertex lights and L2 coefficient of SH on mobile

    //Apply SSAO to "indirect" sources (not halfly indirect, but that's what unity calls baked and image based lighting) 
    AmbientOcclusionFactor ao = (AmbientOcclusionFactor)0;
    
    UNITY_BRANCH if (BRANCH_SCREEN_SPACE_OCCLUSION)
    {
        ao = CreateAmbientOcclusionFactor(fragData.screenUV, surfData.occlusion);
        if (surfaceType > 0) ao.indirectAmbientOcclusion = 1; 
		surfData.occlusion = half(1.0h); // we are already multiplying by the AO here, don't do it at the end like normal
        diffuse *= ao.indirectAmbientOcclusion;
        #if !defined(SLZ_NO_SPECULAR)
        specular *= ao.indirectAmbientOcclusion;
        #endif
    }
    
    //-------------------------------------------------------------------------------------------------
    // realtime light calculations
    //-------------------------------------------------------------------------------------------------
    
    // For dynamic objects, this also does specular for probes if there is no main light, assuming the
    // diffuse only contains probe light (it also contains vertex lights, but we'll just ignore that)
    SLZMainLight(diffuse, specular, fragData, surfData, ao.directAmbientOcclusion);

    // On PC, calculate a separate IBL retroreflection lobe
    #if defined(_RETROREFLECTIVE) && !defined(SHADER_API_MOBILE)
        //half retroLobe = SLZRetroreflectionLobe(fragData.normal, fragData.viewDir, surfData.retroReflSharpness);
	    half3 retroRflProbe = saturate(fragData.NoV) * GlossyEnvironmentReflection(fragData.viewDir, fragData.position, max(0.12,sqrt(1.0 - surfData.retroReflSharpness)), 1.0, fragData.screenUV);// * LitSpecularOcclusion;

        diffuse.rgb += surfData.retroReflPercent * retroRflProbe;
    #endif

    
    #if defined(SHADER_API_MOBILE)
    
    #if !defined(SLZ_NO_MOBILE_IMPORTANT_LIGHT)
    //uint lightMask = 
    //    (1 << (int)(unity_LightIndices[0].x - 1)) | 
    //    (1 << (int)(unity_LightIndices[0].y - 1)) |
    //    (1 << (int)(unity_LightIndices[0].z - 1));
    
    if (_ImportantLightIndex != -1 
    //    && ((1 << (int)(_ImportantLightIndex - 1)) & lightMask) != 0 // light mask not necessary
    )
    {
            Light light = GetAdditionalPerObjectLight((MAX_VISIBLE_LIGHTS - 1), fragData.position);
            #if defined(_LIGHT_COOKIES)
                half3 cookieColor = SampleAdditionalLightCookie((MAX_VISIBLE_LIGHTS - 1), fragData.position);
                light.color *= cookieColor.rgbb;
            #endif
            //Light light = GetAdditionalLight(_ImportantLightIndex, fragData.position, fragData.shadowMask);
            SLZAddLight(diffuse, specular, fragData, surfData, light, ao.directAmbientOcclusion);
    }
    #endif // !SLZ_NO_MOBILE_IMPORTANT_LIGHT
    
    #else // !SHADER_API_MOBILE
    [branch] if (BRANCH_ADDITIONAL_LIGHTS)
    {
        uint pixelLightCount = GetAdditionalLightsCount();
        
        #if USE_FORWARD_PLUS
        ForwardPlusMacroFix inputData = {fragData.screenUV, fragData.position};
        #endif
        
        LIGHT_LOOP_BEGIN(pixelLightCount)
            Light light = GetAdditionalLight(lightIndex, fragData.position, fragData.shadowMask);
            SLZAddLight(diffuse, specular, fragData, surfData, light, ao.directAmbientOcclusion);
        LIGHT_LOOP_END
    }
    #endif // !SHADER_API_MOBILE


    //-------------------------------------------------------------------------------------------------
    // Combine the final lighting information
    //-------------------------------------------------------------------------------------------------
    half3 diffuseReflectance = surfData.occlusion * (surfData.albedo * diffuse.rgb);
    #if defined(_FLUORESCENCE)
        BlendFluorescence(diffuseReflectance, diffuse, surfData.absorbance, surfData.fluorescence);
    #endif
    diffuseReflectance += surfData.emission;

    if (surfaceType == 1) diffuseReflectance *= surfData.alpha;
    #if !defined(SLZ_NO_SPECULAR)
    half3 finalSpecular = surfData.occlusion * specular;
    #else
    half3 finalSpecular = 0;
    #endif
    

    
    return half4(diffuseReflectance + finalSpecular, surfData.alpha);
}



#endif