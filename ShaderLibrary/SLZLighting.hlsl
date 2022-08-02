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



#if !defined(UNITY_COMMON_INCLUDED) //Get my IDE to recognize real, this won't ever get compiled since I just included Common.hlsl 
    #define real half
    #define real2 half2
    #define real3 half3
    #define real4 half4
#endif

//#define USE_MOBILE_BRDF 

//------------------------------------------------------------------------
//------------------------------------------------------------------------
// Constants
//------------------------------------------------------------------------
//------------------------------------------------------------------------

#define SLZ_PI_REAL     real(3.141592653589793238)
#define SLZ_INV_PI_REAL real(0.318309886183790672)


//------------------------------------------------------------------------
//------------------------------------------------------------------------
// Data Structures
//------------------------------------------------------------------------
//------------------------------------------------------------------------


struct SLZFragData
{
    float3  position;
    real3   normal;
    real3   viewDir;
    real    NoV;
    float2  screenUV;
    float2  lightmapUV;
    float2  dynLightmapUV;
    float4  shadowCoord;
    real4   shadowMask;
    real3   vertexLighting;
#if defined(_SLZ_ANISO_SPECULAR)
    real3 bitangent;
    real3 tangent;
    real visLambdaView; //factor used in the anisotropic visibility function that depends only on the view, normal, tangent, and bitangent
#endif
};

struct SLZSurfData
{
    real3   albedo;
    real    perceptualRoughness;
    real    roughness;
    real3   specular;
    real    reflectivity;
    real3   emission;
    real    occlusion;
// #if defined(_SLZ_BRDF_LUT)
//     TEXTURE2D(brdfLUT);
//     SAMPLER(sampler_brdfLUT);
// #endif
#if defined(_SLZ_ANISO_SPECULAR)
    real anisoAspect;
    real roughnessT;
    real roughnessB;
#endif
};


struct SLZDirectSpecLightInfo
{
    #if defined(_SLZ_ANISO_SPECULAR)
    real NoH2;
    real NoL;
    real LoH;
    real ToL;
    real BoL;
    real ToH;
    real BoH;
    #elif defined(SHADER_API_MOBILE) || defined(USE_MOBILE_BRDF)
    real NoH;
    real LoH;
    real NxH2;
    real NoL;
    #else
    real NoV;
    real NoL;
    real NoH;
    real LoH;
    #endif
};

struct SLZAnisoSpecLightInfo
{
    real NoH;
    real NoL;
    real LoH;
    real ToL;
    real BoL;
    real ToH;
    real BoH;
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
real3 SLZSafeHalf3Normalize(real3 value)
{
    float lenSqr = max(dot(value, value), REAL_MIN);
    return value * rsqrt(lenSqr); 
}


/** 
 * Calculate the specular color from the albedo and metallic, and darken the albedo according to the reflectivity to conserve energy
 *
 * @param[in,out]   albedo      Albedo color, tints the specular according to the metallic value, and gets darkened by the reflectivity
 * @param[out]      specular    Specular color, used for controlling the strength and tint of specular lighting  
 * @param           metallic    Metallic value, determines how strongly the specular is tinted by the albedo and the reflectivity
 */
void SLZAlbedoSpecularFromMetallic(inout real3 albedo, out real3 specular, out real reflectivity, real metallic)
{
    specular = lerp(kDielectricSpec.rgb, albedo, metallic);
    real oneMinusReflectivity = -kDielectricSpec.a * metallic + kDielectricSpec.a;
    reflectivity = 1.0h - oneMinusReflectivity;
    albedo = albedo * oneMinusReflectivity;
}

/**
 * Specular antialiasing using normal derivatives to calculate a roughness value to hide sparkles.
 * Taken from the ever-relevant Valve 2015 GDC VR Rendering talk.
 * 
 * @param normal Worldspace normal
 * @return Smoothness value to reduce sparkling, calculate the final smoothness by min'ing with the real smoothness value
 */
half SLZGeometricSpecularAA(half3 normal)
{
    half3 normalDdx = ddx_fine(normal);
    half3 normalDdy = ddy_fine(normal);
    half AARoughness = saturate(max(dot(normalDdx, normalDdx), dot(normalDdy, normalDdy)));
    AARoughness = sqrt(AARoughness); // Valve used pow of 0.3333, I find that is a little too strong. Also sqrt should be cheaper
    return 1.0h - AARoughness;
}

//------------------------------------------------------------------------
//------------------------------------------------------------------------
// Automated Data Structure Population Functions
//------------------------------------------------------------------------
//------------------------------------------------------------------------

SLZFragData SLZGetFragData(float4 positionCS, float3 positionWS, float3 normalWS, float2 lightmapUV, float2 dynLightmapUV, real3 vertexLighting)
{
    SLZFragData data;
    data.position = positionWS;
    data.normal = normalWS;
    data.viewDir = SLZSafeHalf3Normalize(real3(_WorldSpaceCameraPos - positionWS));
    data.NoV = dot(data.normal, data.viewDir);
    data.lightmapUV = lightmapUV;
    data.dynLightmapUV = dynLightmapUV;
    data.vertexLighting = vertexLighting;
    data.shadowMask = SAMPLE_SHADOWMASK(data.lightmapUV);
    #if defined(MAIN_LIGHT_CALCULATE_SHADOWS)
            data.shadowCoord = TransformWorldToShadowCoord(positionWS);
    #else
            data.shadowCoord = float4(0, 0, 0, 0);
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
void SLZFragDataAddAniso(inout SLZFragData fragData, real3 tangent, real3 bitangent, real roughnessT, real roughnessB)
{
    #if defined(_SLZ_ANISO_SPECULAR)
    fragData.tangent = normalize(tangent);
    fragData.bitangent = normalize(bitangent);
    real ToV = dot(tangent, fragData.viewDir);
    real BoV = dot(bitangent, fragData.viewDir);
    fragData.visLambdaView = length(real3(roughnessT * ToV, roughnessB * BoV, fragData.NoV * fragData.NoV));
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
SLZSurfData SLZGetSurfDataMetallicGloss(const real3 albedo, const real metallic, const real smoothness, const real occlusion, const real3 emission)
{
    SLZSurfData data;
    data.albedo = albedo;
    SLZAlbedoSpecularFromMetallic(data.albedo, data.specular, data.reflectivity, metallic);
    data.perceptualRoughness = real(1.0) - smoothness;
    data.roughness = max(data.perceptualRoughness * data.perceptualRoughness, 1.0e-3h);
    //data.fusedVFNorm = 4.0h * data.roughness + 2.0h; // no reason to eat a register to store this, its literally a single MAD
    data.emission = emission;
    data.occlusion = occlusion;
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
void SLZSurfDataAddAniso(inout SLZSurfData surf, real anisoAspect)
{
#if defined(_SLZ_ANISO_SPECULAR)
    surf.anisoAspect = 2.0 * anisoAspect - 1.0;
    real clampedRough = surf.roughness;// clamp(surf.roughness, 0.05, 1);
    surf.roughnessT = max(clampedRough * surf.anisoAspect + clampedRough, 0.001);
    surf.roughnessB = max(-clampedRough * surf.anisoAspect + clampedRough, 0.001);
#endif
}

SLZDirectSpecLightInfo SLZGetDirectLightInfo(const SLZFragData frag, const real3 lightDir)
{

    SLZDirectSpecLightInfo data;
    #if defined(_SLZ_ANISO_SPECULAR)
        real3 halfDir = SLZSafeHalf3Normalize(lightDir + frag.viewDir);

        real3 NxH = cross(frag.normal, halfDir);
        data.NoH2 = 1.0 - dot(NxH, NxH);
        data.ToH = dot(frag.tangent, halfDir);
        data.BoH = dot(frag.bitangent, halfDir);

        data.LoH = saturate(dot(lightDir, halfDir));
        data.ToL = dot(frag.tangent, lightDir);
        data.BoL = dot(frag.bitangent, lightDir);
        data.NoL = saturate(dot(frag.normal, lightDir));
    #elif defined(SHADER_API_MOBILE) || defined(USE_MOBILE_BRDF)
        real3 halfDir = SLZSafeHalf3Normalize(lightDir + frag.viewDir);
        data.NoH = saturate(dot(frag.normal, halfDir));
        data.LoH = saturate(dot(lightDir, halfDir));
        real3 NxH = cross(frag.normal, halfDir);
        data.NxH2 = saturate(dot(NxH, NxH));
        data.NoL = saturate(dot(frag.normal, lightDir));
    #else
        data.NoV = abs(frag.NoV) + 1e-5;
        data.NoL = dot(frag.normal, lightDir); // Visibility function needs abs, specular falloff needs saturate
        real3 halfDir = SLZSafeHalf3Normalize(lightDir + frag.viewDir);
        data.NoH = saturate(dot(frag.normal, halfDir));
        data.LoH = saturate(dot(lightDir, halfDir));
        /*
        // Avoid actually calculating half light-view vector using identities. See Earl Hammon, Jr. "PBR Diffuse Lighting for GGX+Smith Microsurfaces". GDC 2017
        real LoV = dot(lightDir, viewDir);
        real LVLen2 = real(2.0) * LoV + real(2.0); // length(L + V)^2 = 2 * dot(L,V) + 2
        real rcpLVLen2 = rcp(LVLen2); // 1 / length(L+V)
        data.LoH = rcpLVLen2 * LoV + rcpLVLen2; // dot(L, H) = 0.5 * length(L + V) = 0.5 * (length(L+V)^2) / length(L + V) = 0.5 * (2*dot(L,V) + 2) / length(L+V) =  length(L+V) * (dot(L+V) + 1)
        data.NoH = (data.NoV + data.NoL) * rcpLVLen2; // dot(N, H) = (dot(N, L) + dot(N, V)) / length(L + V)   
        */
    #endif
    return data;
}

SLZAnisoSpecLightInfo SLZGetAnisoSpecLightInfo(const real3 normal, const real3 tangent, const real3 bitangent,
    const real3 viewDir, const real3 lightDir)
{
    SLZAnisoSpecLightInfo data;
    real3 halfDir = SLZSafeHalf3Normalize(lightDir + viewDir);
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
real3 SLZLambertDiffuse(const real3 attenLightColor, const real3 normal, const real3 lightDir)
{
    return attenLightColor * saturate(dot(normal, lightDir));
}

real4 BDRFLUTSAMPLER(real2 UV){
    #if defined(_BRDFMAP)
        return SAMPLE_TEXTURE2D_LOD(g_tBRDFMap, BRDF_linear_clamp_sampler, UV, 0);
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
real4 SLZSampleBDRFLUT(real NoV, real NoL)
{
    NoL = saturate((NoL + 1) * 0.5);
    NoV = saturate(NoV);
    return BDRFLUTSAMPLER(float2(NoL, NoV));
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
real4 SLZSampleBDRFLUTHalfLambert( real NoV, real halfLambert)
{
    NoV = saturate(NoV);
    return BDRFLUTSAMPLER(float2(halfLambert, NoV));
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
real4 SLZSampleBDRFLUTShadow( real NoV, real NoL, real shadowAttenuation)
{
    real NoL2 = saturate((NoL + 1) * 0.5);
    //real NoV2 = saturate(NoV);
    real lineartocir = sqrt(shadowAttenuation); //replace with an s curve
    // if t < d / 2 then return outSine(t * 2, b, c / 2, d) end
    // return inSine((t * 2) -d, b + c / 2, c / 2, d)
   // \sqrt{-\left(x-1\right)^{2}+1}
    return BDRFLUTSAMPLER(float2(min(NoL2, lineartocir  ), saturate(NoV)));
}

/**
 * Diffuse BDRF for realtime lights, right now just does lambert but could be modified to do a more complex diffuse BDRF
 *
 *
 * @param fragData All relevant data relating to the fragment
 * @param surfData All relevant data relating to the surface properties at the fragment
 * @param lightColor Color of the realtime light
 */
real3 SLZDiffuseBDRF(const SLZFragData fragData, const SLZSurfData surfData, const Light light)
{
    #if defined(_BRDFMAP)
    return SLZSampleBDRFLUTShadow( fragData.NoV, dot(fragData.normal, light.direction), light.shadowAttenuation) * light.distanceAttenuation*light.color.rgb;
    #else
    real3 attenuatedLight = light.color.rgb * (light.distanceAttenuation * light.shadowAttenuation);
    return SLZLambertDiffuse(attenuatedLight, fragData.normal, light.direction);
    #endif
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
real SLZGGXSpecularDMobile(real NoH, real NxH2, real roughness)
{	
    real a = NoH * roughness;
    real d = roughness / max(a * a + NxH2, REAL_MIN);
    real d2 = (d * d * SLZ_INV_PI_REAL);
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
    float d2 = (d * d * SLZ_INV_PI_REAL);
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
real SLZGGXSpecularDAniso(real NoH2, real ToH, real BoH, real roughnessT, real roughnessB, real aspectRatio)
{
    real roughProduct = roughnessT * roughnessB;
    real aspectTerm = (1.0h - aspectRatio) / (1.0h + aspectRatio); // roughnessB/roughnessT = (rough * (1 - aspectRatio))/(rough * (1 + aspectRatio)) 
    real2 aVec = real2(ToH * aspectTerm, BoH / aspectTerm);
    real b = dot(aVec, aVec) + NoH2 * roughProduct;
    real w2 = rcp(b * rsqrt(roughProduct));
    w2 *= w2;
    return min(w2 * SLZ_INV_PI_REAL, 100);
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
real SLZFusedVFMobile(real LoH, real roughness)
{
    real LoH2 = LoH * LoH;
    return rcp(max(real(0.1), LoH2) * (real(4.0) * roughness + real(2.0)));
}


/** 
 * Heitz height-correlated Smith-GGX visibility function (specular geometric shadowing)
 *
 * @param NoV       Normal-view dot product
 * @param NoL       Normal-light dot product
 * @param roughness Non-perceptual roughness
 * @return Geometric shadowing term of the specular BDRF
 */
real SLZSmithVisibility(real NoV, real NoL, real roughness)
{
    real rough2 = roughness * roughness;
    NoL = abs(NoL) + 1e-5;  //The baked specular falloff function needs saturate(NoL), so NoL is stored raw and needs to be abs'd here 
    real v = NoL * sqrt(NoV * (-rough2 * NoV + 1.0h) + rough2);
    real l = NoV * sqrt(NoL * (-rough2 * NoL + 1.0h) + rough2);
    return real(0.5) / (v + l);
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
real SLZSmithVisibilityAniso(real NoV, real NoL, real ToL, real BoL, real visLambdaView, real roughnessT, real roughnessB)
{
    NoL = abs(NoL) + 1e-5;
    real lambdaV = NoL * visLambdaView;
    real lambdaL = length(real3(roughnessT * ToL, roughnessB * BoL, NoL));
    return 0.5 / (lambdaL + lambdaV);
}

/** 
 * Schlick Fresnel function
 *
 * @param LoH       Dot product of light direction with half light-view vector  
 * @param specColor Base specular color
 */ 
real3 SLZSchlickFresnel(real LoH, real3 specColor)
{
    real iLoH = 1.0 - LoH;
    real iLoH5 = pow(iLoH, 5.0);
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

real SLZDirectBRDFSpecularMobile(real NoH, real LoH, real NxH2, real roughness)
{
    // GGX Distribution multiplied by combined approximation of Visibility and Fresnel
    // BRDFspec = (D * V * F) / 4.0
    // D = roughness^2 / ( NoH^2 * (roughness^2 - 1) + 1 )^2 // Wrong! Unity forgot the 1/pi term in their specular D!
    // V * F = 1.0 / ( LoH^2 * (roughness + 0.5) )
    // See "Optimizing PBR for Mobile" from Siggraph 2015 moving mobile graphics course
    // https://community.arm.com/events/1155

    // Final BRDFspec = roughness^2 / ( NoH^2 * (roughness^2 - 1) + 1 )^2 * (LoH^2 * (roughness + 0.5) * 4.0) 
   
    real NDF = SLZGGXSpecularDMobile(NoH, NxH2, roughness);
    real VF  = SLZFusedVFMobile(LoH, roughness);
    real specularTerm = (NDF * VF);

    
    #if defined(SHADER_API_MOBILE)
        // On platforms where half actually means something, the denominator has a risk of overflow
        // clamp below was added specifically to "fix" that, but dx compiler (we convert bytecode to metal/gles)
        // sees that specularTerm have only non-negative terms, so it skips max(0,..) in clamp (leaving only min(100,...))
        specularTerm = specularTerm - HALF_MIN;
        specularTerm = clamp(specularTerm, 0.0, 100.0); // Prevent FP16 overflow on mobiles
    #else
        specularTerm = max(0, specularTerm);
    #endif    

    return specularTerm;
}

/**
 * High quality specular BDRF, for use on PC. Uses the same GGX N, D, and F as google filament
 */
real3 SLZDirectBRDFSpecularHighQ(real NoH, real NoV, real NoL, real LoH, real roughness, real3 specColor)
{
    real N = SLZGGXSpecularD(NoH, roughness);
    real D  = SLZSmithVisibility(NoV, NoL, roughness);
    real3 F   = SLZSchlickFresnel(LoH, specColor);
    return N * D * F;
}

real3 SLZAnisoDirectBRDFSpecular(SLZDirectSpecLightInfo lightInfo, SLZSurfData surfData, real NoV, real visLambdaView)
{
#if defined(_SLZ_ANISO_SPECULAR)
    real N = SLZGGXSpecularDAniso(lightInfo.NoH2, lightInfo.ToH, lightInfo.BoH, surfData.roughnessT, surfData.roughnessB, surfData.anisoAspect);
    real D = SLZSmithVisibilityAniso(NoV, lightInfo.NoL, lightInfo.ToL, lightInfo.BoL, visLambdaView, surfData.roughnessT, surfData.roughnessB);
    real3 F = SLZSchlickFresnel(lightInfo.LoH, surfData.specular);
    real3 specularTerm = N * D * F;

#if defined(SHADER_API_MOBILE)
    // On platforms where half actually means something, the denominator has a risk of overflow
    // clamp below was added specifically to "fix" that, but dx compiler (we convert bytecode to metal/gles)
    // sees that specularTerm have only non-negative terms, so it skips max(0,..) in clamp (leaving only min(100,...))
    specularTerm = specularTerm - HALF_MIN;
    specularTerm = clamp(specularTerm, 0.0, 100.0); // Prevent FP16 overflow on mobiles
#else
    specularTerm = max(0, specularTerm);
#endif
    return specularTerm;
#else
    return real3(0, 0, 0);
#endif
}

/**
 * Specular BRDF, 
 *
 * @param specInfo Struct containing the dot products of the normal and light with the half light-view
 * @param surfData Surface information
 * @return Specular color
 */
real3 SLZDirectBRDFSpecular(SLZDirectSpecLightInfo specInfo, SLZSurfData surfData, SLZFragData fragData)
{
    real3 specular;
    #if defined(_SLZ_ANISO_SPECULAR)
    specular = SLZAnisoDirectBRDFSpecular(specInfo, surfData, fragData.NoV, fragData.visLambdaView);
    #elif defined(SHADER_API_MOBILE) || defined(USE_MOBILE_BRDF)
        specular = surfData.specular * SLZDirectBRDFSpecularMobile(specInfo.NoH, specInfo.LoH, specInfo.NxH2, surfData.roughness);
    #else
        specular = SLZDirectBRDFSpecularHighQ(specInfo.NoH, specInfo.NoV, specInfo.NoL, specInfo.LoH, surfData.roughness, surfData.specular);
    #endif

    #if defined(ANIME)
    #if defined(_BRDFMAP)
        real3 bdrfTerm = SAMPLE_TEXTURE2D_LOD(g_tBRDFMap, BRDF_linear_clamp_sampler, float2(specular.r, specInfo.NoH), 0).rgb;
        specular += bdrfTerm;
    #endif
    #endif

    return specular;
}



//------------------------------------------------------------------------
//------------------------------------------------------------------------
// Lighting Functions
//------------------------------------------------------------------------
//------------------------------------------------------------------------

/**
 * Multiplier to fade out specular highlights from non-realtime sources when the normal faces away from
 * the light source. The specular BRDF actually produces two highlights, one facing the light and one opposite it.
 * With realtime lights, multiplying by the diffuse light zeros out the unwanted highlight. When doing specular
 * highlights from non-unidirectional sources like lightmaps or spherical harmonics, the light recieved by pixels
 * facing away from the fake light direction is not 0 so the false highlight shows up. To get rid of it, we can
 * multiply by some factor of N dot L. This will unfortunately darken the real highlight, but if we take the square
 * root of N dot L the darkening will not be significant until very grazing angles. 1 - (1 - N dot L)^2 has a
 * similar shape but avoids the square root.
 * 
 * @param NoL Saturated dot product of the normal and light direction
 * @return scale to multiply the intensity of the specular by
 */
real SLZFakeSpecularFalloff(real NoL)
{
    real NoLMul = 1.0 - saturate(NoL); // On PC, the smith visibility function needs abs(NoL), so NoL is stored raw and needs to be saturated here 
    NoLMul = -NoLMul * NoLMul + 1.0;
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
real3 SLZApplyLightmapDirectionality(real3 lightmapColor, real3 lmDirection, real3 normal, real directionalityFactor)
{
    real halfLambert = dot(normal, 0.5h * lmDirection) + real(0.5);
    return lightmapColor * halfLambert / max(real(1e-4), directionalityFactor);
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
real3 SLZApplyLightmapDirectionalityBRDFLUT(const real3 lightmapColor, const real3 lmDirection, const real3 normal, const real directionalityFactor,
    const SLZFragData fragData, const SLZSurfData surfData)
{
#if defined(_BRDFMAP)
    real halfLambert = (dot(normal, 0.5h * lmDirection) + real(0.5)) / max(real(1e-4), directionalityFactor);
    real3 brdfLUT = SLZSampleBDRFLUTHalfLambert( fragData.NoV, halfLambert);
    return lightmapColor * brdfLUT;
#else
    return real3(0, 0, 0);
#endif
}

#define SLZ_LM_R_MIN  0.4
#define SLZ_LM_R_MAX  0.75
#define SLZ_LM_D_MIN  0.4
#define SLZ_LM_D_MAX  0.5

/**
 * Reads the lightmap, directional lightmap, and dynamic lightmap, and calculates the total diffuse lighting from them as well
 * as calculating a specular highlight using the directional map if present  
 *
 * @param[in,out] diffuse  Total diffuse lighting from the main and dynamic lightmaps
 * @param[in,out] specular Specular lighting, calculated if directional lightmapping is on
 * @param         frag     Struct containing all relevant fragment data (lightmap uvs, normal and view vectors, etc)
 * @param         surf     struct containing PBR surface information for the specular calculations
 */
void SLZGetLightmapLighting(inout real3 diffuse, inout real3 specular, const SLZFragData frag, inout SLZSurfData surf)
{


    real3 lmDiffuse = SAMPLE_TEXTURE2D(unity_Lightmap, samplerunity_Lightmap, frag.lightmapUV).rgb;
    //
    #if defined(DIRLIGHTMAP_COMBINED)
            real4 directionalMap = SAMPLE_TEXTURE2D(unity_LightmapInd, samplerunity_Lightmap, frag.lightmapUV);
            real3 lmDirection = real(2.0) * directionalMap.xyz - real(1.0);


            #if defined(_BRDFMAP)
            lmDiffuse = SLZApplyLightmapDirectionalityBRDFLUT(lmDiffuse, lmDirection, frag.normal, directionalMap.w, frag, surf);
            #else
            lmDiffuse = SLZApplyLightmapDirectionality(lmDiffuse,lmDirection, frag.normal, directionalMap.w);
            #endif
            
            #if !defined(_SLZ_DISABLE_BAKED_SPEC)
                lmDirection = SLZSafeHalf3Normalize(lmDirection); //length not 1
                SLZDirectSpecLightInfo lightInfo = SLZGetDirectLightInfo(frag, lmDirection);
                real3 lmSpecular = SLZDirectBRDFSpecular(lightInfo, surf, frag);
                specular += lmDiffuse * lmSpecular * lightInfo.NoL;
            #endif
    #endif
    
    diffuse += lmDiffuse;

    #if defined(DYNAMICLIGHTMAP_ON)
        real3 dynLmDiffuse = SAMPLE_TEXTURE2D(unity_DynamicLightmap, samplerunity_DynamicLightmap, frag.dynLightmapUV).rgb;
        #if defined(DIRLIGHTMAP_COMBINED) && !defined(SHADER_API_MOBILE)
            real4 dynDirectionalMap = SAMPLE_TEXTURE2D(unity_DynamicDirectionality, samplerunity_DynamicLightmap, frag.dynLightmapUV);
            real3 dynLmDirection = real(2.0) * dynDirectionalMap.rgb - real(1.0);
            dynLmDiffuse = SLZApplyLightmapDirectionality(dynLmDiffuse,dynLmDirection, frag.normal, dynDirectionalMap.w);
            #if !defined(_SLZ_DISABLE_BAKED_SPEC)
                dynLmDirection = SLZSafeHalf3Normalize(dynLmDirection); //length not 1
                SLZDirectSpecLightInfo dynLightInfo = SLZGetDirectLightInfo(frag, dynLmDirection);
                real3 dynLmSpecular = SLZDirectBRDFSpecular(dynLightInfo, surf, frag);
                specular += dynLmDiffuse * dynLmSpecular * dynLightInfo.NoL;
            #endif
        #endif
    
        diffuse += dynLmDiffuse;
    #endif
}

/**
 * Add to the diffuse the light from spherical harmonics
 *
 * @param[in,out] diffuse  Current total diffuse light
 * @param         normal   Worldspace normal vector
 */
void SLZSHDiffuse(inout real3 diffuse, half3 normal)
{
    #if !defined(LIGHTMAP_ON) 
        #if defined(EVALUATE_SH_VERTEX) // all of spherical harmonics are calculated in the vertex program
           // do nothing 
        #else // Calculate all or some of the SH in the frag
            real3 shL0L1 = SHEvalLinearL0L1(normal, unity_SHAr, unity_SHAg, unity_SHAb);
            #if defined(EVALUATE_SH_MIXED) // In mixed mode, the L2 component is calculated in the vertex
                real3 shL2 = real3(0,0,0);
            #else
                real3 shL2 = SHEvalLinearL2(normal, unity_SHBr, unity_SHBg, unity_SHBb, unity_SHC);
            #endif
            diffuse = shL2 + shL0L1;
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
real3 SLZSHSpecularDirection()
{
    real3 direction = (unity_SHAr.xyz + unity_SHAg.xyz + unity_SHAb.xyz);
    float lengthSq = max(float(dot(direction, direction)), REAL_MIN);
    float invLength = rsqrt(lengthSq);
    direction = direction * invLength;
    return direction;
}

real3 SLZProbeReflectionDir(SLZFragData fragData, SLZSurfData surfData)
{
#if defined(_SLZ_ANISO_SPECULAR)
    real3 anisoDir = surfData.anisoAspect > 0 ? fragData.bitangent : fragData.tangent;
    real viewSign = surfData.anisoAspect > 0 ? 1 : -1;
    real3 anisoTangent = cross(anisoDir, fragData.viewDir);
    real3 anisoNormal = cross(anisoTangent, anisoDir);
    real3 bentNormal = normalize(lerp(fragData.normal, anisoNormal, abs(surfData.anisoAspect)));
    
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
void SLZImageBasedSpecular(inout real3 specular, half3 reflectionDir, const SLZFragData fragData, const SLZSurfData surfData, half indSSAO)
{
    real3 reflectionProbe = GlossyEnvironmentReflection(reflectionDir, fragData.position, surfData.perceptualRoughness, 1.0h);
#if defined(SLZ_SSR)

#endif
    real surfaceReduction = 1.0h / (surfData.roughness * surfData.roughness + 1.0h);
    real3 grazingTerm = saturate((1.0h - surfData.perceptualRoughness) + surfData.reflectivity);
    real fresnelTerm = (1.0h - saturate(fragData.NoV));
    fresnelTerm *= fresnelTerm;
    fresnelTerm *= fresnelTerm; // fresnelTerm ^ 4
    real3 IBSpec = real3(surfaceReduction * lerp(surfData.specular, grazingTerm, fresnelTerm));
    
    #if defined(_SCREEN_SPACE_OCCLUSION)
        reflectionProbe *= indSSAO;
    #endif
    
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
 * @param[in,out] specular  Running total of the specular color
 * @param         normal    Worldspace normal vector
 * 
 */
void SLZSpecularHorizonOcclusion(inout real3 specular, half3 normal, half3 reflectionDir)
{
    real horizonOcclusion = min(1.0h + dot(reflectionDir, normal), 1.0h);
    specular *= horizonOcclusion * horizonOcclusion;
}

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
void SLZMainLight(inout real3 diffuse, inout real3 specular, const SLZFragData fragData, const SLZSurfData surfData, real directSSAO)
{
    Light mainLight = GetMainLight(fragData.shadowCoord, fragData.position, fragData.shadowMask);
    real3 diffuseBRDF = SLZDiffuseBDRF(fragData, surfData, mainLight);

    #if defined(_SCREEN_SPACE_OCCLUSION)
        diffuseBRDF *= directSSAO;
    #endif
    
    
    //If the object doesn't have a lightmap, do a specular highlight for EITHER the directional light, if it exists, or the spherical harmonics L1 band
    #if !defined(LIGHTMAP_ON) && !defined(SLZ_DISABLE_BAKED_SPEC)
        bool isMainLight = max(diffuseBRDF.x, max(diffuseBRDF.y, diffuseBRDF.z)) > HALF_MIN ? true : false;
        real3 shL1Dir = SLZSHSpecularDirection();
        real3 dominantDir = isMainLight ? mainLight.direction : shL1Dir;
        SLZDirectSpecLightInfo specInfo = SLZGetDirectLightInfo(fragData, dominantDir);
        real3 dominantColor = isMainLight ? diffuseBRDF : max(real(0.0), diffuse);
        real NoLMul = SLZFakeSpecularFalloff(specInfo.NoL);
        NoLMul = isMainLight ? 1.0 : NoLMul;
        specular += dominantColor * SLZDirectBRDFSpecular(specInfo, surfData, fragData) * NoLMul;
    #elif !defined(DIRLIGHTMAP_COMBINED) || defined(SLZ_DISABLE_BAKED_SPEC)
        SLZDirectSpecLightInfo specInfo = SLZGetDirectLightInfo(fragData, mainLight.direction);
        specular += diffuseBRDF * SLZDirectBRDFSpecular(specInfo, surfData, fragData);
    #endif
    diffuse += diffuseBRDF;
}

/**
 * Calculates diffuse and specular lighting from additional lights
 *
 * @param[in,out] diffuse   Running total of the diffuse light color
 * @param[in,out] specular  Running total of the specular color
 * @param         fragData  Struct containing all relevant fragment data (normal, position, etc)
 * @param         surfData  Struct containing physical properties of the surface (specular color, roughness, etc)
 * @param         addLight  Struct containing the information about a given light (color, attenuation, shadowing, etc)
 * @param         directSSAO Direct screen-space ambient occlusion factor    
 */
void SLZAddLight(inout real3 diffuse, inout real3 specular, const SLZFragData fragData, const SLZSurfData surfData, Light addLight, half directSSAO)
{
    real3 diffuseBRDF = SLZDiffuseBDRF(fragData, surfData, addLight);
    #if defined(_SCREEN_SPACE_OCCLUSION)
        diffuseBRDF *= directSSAO;
    #endif
    diffuse += diffuseBRDF;
    SLZDirectSpecLightInfo specInfo = SLZGetDirectLightInfo(fragData, addLight.direction);
    specular += diffuseBRDF * SLZDirectBRDFSpecular(specInfo, surfData, fragData);
}

/**
 * Full PBR lighting calculation
 *
 * @param  fragData  Struct containing all relevant fragment data (normal, position, etc)
 * @param  surfData  Struct containing physical properties of the surface (specular color, roughness, etc)
 * @return PBR lit surface color
 */
real3 SLZPBRFragment(SLZFragData fragData, SLZSurfData surfData)
{
    real3 diffuse = real3(0.0h, 0.0h, 0.0h);
    real3 specular = real3(0.0h, 0.0h, 0.0h);
    //real2 dfg = SLZDFG(fragData.NoV, surfData.roughness);
    #if defined(_SCREEN_SPACE_OCCLUSION)
        AmbientOcclusionFactor ao = CreateAmbientOcclusionFactor(fragData.screenUV, surfData.occlusion);
        surfData.occlusion = 1.0h; // we are already multiplying by the AO in the intermediate steps, don't do it at the end like normal
    #else
        AmbientOcclusionFactor ao;
        ao.indirectAmbientOcclusion = 1.0h;
        ao.directAmbientOcclusion = 1.0h;
    #endif
    
       
    #if defined(LIGHTMAP_ON) 
    //-------------------------------------------------------------------------------------------------
    // Lightmapping diffuse and specular calculations
    //-------------------------------------------------------------------------------------------------
        
        SLZGetLightmapLighting(diffuse, specular, fragData, surfData);
    
    #else 
    //-------------------------------------------------------------------------------------------------
    // Spherical harmonic diffuse calculations
    //-------------------------------------------------------------------------------------------------
        
        SLZSHDiffuse(diffuse, fragData.normal);
        
    #endif
    
    diffuse += fragData.vertexLighting; //contains both vertex lights and L2 coefficient of SH on mobile
    
    //Apply SSAO to "indirect" sources (not really indirect, but that's what unity calls baked and image based lighting) 
    #if defined(_SCREEN_SPACE_OCCLUSION)
        diffuse *= ao.indirectAmbientOcclusion;
        specular *= ao.indirectAmbientOcclusion;
    #endif
    
    //-------------------------------------------------------------------------------------------------
    // Realtime light calculations
    //-------------------------------------------------------------------------------------------------
    
    // For dynamic objects, this also does specular for probes if there is no main light, assuming the
    // diffuse only contains probe light (it also contains vertex lights, but we'll just ignore that)
    SLZMainLight(diffuse, specular, fragData, surfData, ao.directAmbientOcclusion); 
    
    #if defined(_ADDITIONAL_LIGHTS)
    uint pixelLightCount = GetAdditionalLightsCount();
    
    LIGHT_LOOP_BEGIN(pixelLightCount)
        Light light = GetAdditionalLight(lightIndex, fragData.position, fragData.shadowMask);
        SLZAddLight(diffuse, specular, fragData, surfData, light, ao.directAmbientOcclusion);
    LIGHT_LOOP_END
    #endif
    


    //-------------------------------------------------------------------------------------------------
    // Image-based specular
    //-------------------------------------------------------------------------------------------------
    real3 reflectionDir = SLZProbeReflectionDir(fragData, surfData);
    SLZImageBasedSpecular(specular, reflectionDir, fragData, surfData, ao.indirectAmbientOcclusion);
    SLZSpecularHorizonOcclusion(specular, fragData.normal, reflectionDir);
    
    //-------------------------------------------------------------------------------------------------
    // Combine the final lighting information
    //-------------------------------------------------------------------------------------------------
    
    return surfData.occlusion * (surfData.albedo * diffuse + specular) + surfData.emission;
}



#endif