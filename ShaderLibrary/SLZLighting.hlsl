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
};


struct SLZDirectSpecLightInfo
{
    #if defined(SHADER_API_MOBILE) || defined(USE_MOBILE_BRDF)
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


//------------------------------------------------------------------------
//------------------------------------------------------------------------
// Basic Functions
//------------------------------------------------------------------------
//------------------------------------------------------------------------

/** 
 * The default normalize function doesn't perfectly normalize real-precsion vectors. This can result in bizarre banding in some lighting calculations.
 * It seems that taking the rsqrt of a real is the issue, giving a slightly inaccurate result. Thus cast the length squared value to a float before
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
    half3 normalDdx = ddx(normal);
    half3 normalDdy = ddy(normal);
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


SLZSurfData SLZGetSurfDataMetallicGloss(float3 albedo, real metallic, real smoothness, real occlusion, real3 emission)
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

SLZDirectSpecLightInfo SLZGetDirectLightInfo(real3 normal, real3 viewDir, real NoV, real3 lightDir)
{

    SLZDirectSpecLightInfo data;
    #if defined(SHADER_API_MOBILE) || defined(USE_MOBILE_BRDF)
        real3 realDir = SLZSafeHalf3Normalize(lightDir + viewDir);
        data.NoH = saturate(dot(normal, realDir));
        data.LoH = saturate(dot(lightDir, realDir));
        real3 NxH = cross(normal, realDir);
        data.NxH2 = saturate(dot(NxH, NxH));
        data.NoL = saturate(dot(normal, lightDir));
    #else
        data.NoV = max(abs(NoV), 1e-7);
        data.NoL = saturate(dot(normal, lightDir));
        real3 halfDir = SLZSafeHalf3Normalize(lightDir + viewDir);
        data.NoH = saturate(dot(normal, halfDir));
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
real3 SLZLambertDiffuse(real3 attenLightColor, real3 normal, real3 lightDir)
{
    return attenLightColor * saturate(dot(normal, lightDir));
}


/**
 * Diffuse BDRF for realtime lights, right now just does lambert but could be modified to do a more complex diffuse BDRF
 *
 * @param fragData All relevant data relating to the fragment
 * @param surfData All relevant data relating to the surface properties at the fragment
 * @param lightColor Color of the realtime light
 */
real3 SLZDiffuseBDRF(const SLZFragData fragData, const SLZSurfData surfData, Light light)
{
    real3 attenuatedLight = light.color.rgb * (light.distanceAttenuation * light.shadowAttenuation);
    return SLZLambertDiffuse(attenuatedLight, fragData.normal, light.direction);
}

/** 
 * GGX normal distribution function optimized for half-precision. 
 * Uses Lagrange's identity (dot(cross(A, B), cross(A, B)) = length(A)^2 * length(B)^2 - dot(A, B)^2)
 * to avoid calculating 1 - dot(N, H)^2 which has severe precision issues when dot(N, H) is close to 1
 * See the Google Filament documentation https://google.github.io/filament/Filament.md.html#materialsystem/specularbrdf  
 *
 * @param NoH       Dot product of the normal with real view-light vector
 * @param NxH2      Cross-product of the normal and real view-light, dotted with itself  
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
 * @param NoH       Dot product of the normal with real view-light vector
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
 * Kelemen and Szirmay-Kalos (KSK) visibility with J. Hable's roughness term, acts as both the visibility and fresnel functions
 * This is unity's default for the URP. Extremely cheap, perfect for mobile.
 * See https://community.arm.com/events/1155 "Optimizing PBR for Mobile" for more details
 *
 * @param LoH         Dot product of the light direction with the real view-light vector
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
 * @param Nol       Normal-light dot product
 * @param roughness Non-perceptual roughness
 * @return Geometric shadowing term of the specular BDRF
 */
real SLZSmithVisibility(real NoV, real NoL, real roughness)
{
    real rough2 = roughness * roughness;

    real v = NoL * sqrt(NoV * NoV * (real(1.0) - rough2) + rough2);
    real l = NoV * sqrt(NoL * NoL * (real(1.0) - rough2) + rough2);
    return real(0.5) / max((v + l), 1e-7);
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

realor4 SLZDirectBRDFSpecularMobile(real NoH, real LoH, real NxH2, real roughness)
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
    realor4 specularTerm = (NDF * VF);

    #if defined(_BRDFMAP)
    specularTerm +=  SAMPLE_TEXTURE2D_LOD(g_tBRDFMap, BRDF_linear_clamp_sampler, float2(specularTerm.r ,NoH) ,0 )  ;
    #endif
    
    #if defined(SHADER_API_MOBILE)
        // On platforms where half actually means something, the denominator has a risk of overflow
        // clamp below was added specifically to "fix" that, but dx compiler (we convert bytecode to metal/gles)
        // sees that specularTerm have only non-negative terms, so it skips max(0,..) in clamp (leaving only min(100,...))
        specularTerm = specularTerm - HALF_MIN;
        specularTerm = clamp(specularTerm, 0.0, 100.0); // Prevent FP16 overflow on mobiles
    #endif    
    return max(specularTerm,0);
}

/**
 * High quality specular BDRF, for use on PC. Not currently used
 */
real3 SLZDirectBRDFSpecularHighQ(real NoH, real NoV, real NoL, real LoH, real roughness, real3 specColor)
{
    real NDF = SLZGGXSpecularD(NoH, roughness);
    real GS  = SLZSmithVisibility(NoV, NoL, roughness);
    real3 F   = SLZSchlickFresnel(LoH, specColor);
    return NDF* GS * F;
}

/**
 * Specular BRDF, 
 *
 * @param specInfo Struct containing the dot products of the normal and light with the half light-view
 * @param surfData Surface information
 * @return Specular color
 */
real3 SLZDirectBRDFSpecular(SLZDirectSpecLightInfo specInfo, SLZSurfData surfData)
{
    #if defined(SHADER_API_MOBILE) || defined(USE_MOBILE_BRDF)
        return surfData.specular * SLZDirectBRDFSpecularMobile(specInfo.NoH, specInfo.LoH, specInfo.NxH2, surfData.roughness);
    #else
        return SLZDirectBRDFSpecularHighQ(specInfo.NoH, specInfo.NoV, specInfo.NoL, specInfo.LoH, surfData.roughness, surfData.specular);
    #endif
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
    real NoLMul = 1.0 - NoL;
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
 * Reads the lightmap, directional lightmap, and dynamic lightmap, and calculates the total diffuse lighting from them as well
 * as calculating a specular highlight using the directional map if present  
 *
 * @param[in,out] diffuse  Total diffuse lighting from the main and dynamic lightmaps
 * @param[in,out] specular Specular lighting, calculated if directional lightmapping is on
 * @param         frag     Struct containing all relevant fragment data (lightmap uvs, normal and view vectors, etc)
 * @param         surf     struct containing PBR surface information for the specular calculations
 */
void SLZGetLightmapLighting(inout real3 diffuse, inout real3 specular, const SLZFragData frag, const SLZSurfData surf)
{
    
    real3 lmDiffuse = SAMPLE_TEXTURE2D(unity_Lightmap, samplerunity_Lightmap, frag.lightmapUV).rgb;
    diffuse += lmDiffuse;
    #if defined(DIRLIGHTMAP_COMBINED)
            real4 directionalMap = SAMPLE_TEXTURE2D(unity_LightmapInd, samplerunity_Lightmap, frag.lightmapUV);
            real3 lmDirection = real(2.0) * directionalMap.xyz - real(1.0);
            diffuse = SLZApplyLightmapDirectionality(diffuse,lmDirection, frag.normal, directionalMap.w);
            lmDirection = SLZSafeHalf3Normalize(lmDirection); //length not 1
            #if !defined(_SLZ_DISABLE_BAKED_SPEC)
                SLZDirectSpecLightInfo lightInfo = SLZGetDirectLightInfo(frag.normal, frag.viewDir, frag.NoV, lmDirection);
                real3 lmSpecular = SLZDirectBRDFSpecular(lightInfo, surf);
                specular += lmDiffuse * lmSpecular * lightInfo.NoL;
            #endif
    #endif
    
    #if defined(DYNAMICLIGHTMAP_ON)
        real3 dynDiffuse = SAMPLE_TEXTURE2D(unity_DynamicLightmap, samplerunity_DynamicLightmap, frag.dynLightmapUV).rgb;
        #if defined(DIRLIGHTMAP_COMBINED) && !defined(SHADER_API_MOBILE)
            real4 dynDirectionalMap = SAMPLE_TEXTURE2D(unity_DynamicDirectionality, samplerunity_DynamicLightmap, frag.dynLightmapUV);
            real3 dynLmDirection = real(2.0) * dynDirectionalMap.rgb - real(1.0);
            dynDiffuse = SLZApplyLightmapDirectionality(dynDiffuse,dynLmDirection, frag.normal, dynDirectionalMap.w);
            #if !defined(_SLZ_DISABLE_BAKED_SPEC)
                real3 dynNormLmDir = SLZSafeHalf3Normalize(dynLmDirection);
                SLZDirectSpecLightInfo dynLightInfo = SLZGetDirectLightInfo(frag.normal, frag.viewDir, frag.NoV, dynNormLmDir);
                specular += dynDiffuse * SLZDirectBRDFSpecular(dynLightInfo, surf);
            #endif
        #endif
    
        diffuse += dynDiffuse;
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
    diffuse += diffuseBRDF;
    
    //If the object doesn't have a lightmap, do a specular highlight for EITHER the directional light, if it exists, or the spherical harmonics L1 band
    #if !defined(LIGHTMAP_ON) && !defined(SLZ_DISABLE_BAKED_SPEC)
        bool isMainLight = max(diffuseBRDF.x, max(diffuseBRDF.y, diffuseBRDF.z)) > HALF_MIN ? true : false;
        real3 shL1Dir = SLZSHSpecularDirection();
        real3 dominantDir = isMainLight ? mainLight.direction : shL1Dir;
        real3 dominantColor = isMainLight ? diffuseBRDF : max(real(0.0), diffuse);
        SLZDirectSpecLightInfo specInfo = SLZGetDirectLightInfo(fragData.normal, fragData.viewDir, fragData.NoV, dominantDir);
        real NoLMul = SLZFakeSpecularFalloff(specInfo.NoL);
        NoLMul = isMainLight ? 1.0 : NoLMul;
        specular += dominantColor * SLZDirectBRDFSpecular(specInfo, surfData) * NoLMul;
    #elif !defined(DIRLIGHTMAP_COMBINED) || defined(SLZ_DISABLE_BAKED_SPEC)
        SLZDirectSpecLightInfo specInfo = SLZGetDirectLightInfo(fragData.normal, fragData.viewDir, fragData.NoV, mainLight.direction);
        specular += diffuseBRDF * SLZDirectBRDFSpecular(specInfo, surfData);
    #endif
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
    SLZDirectSpecLightInfo specInfo = SLZGetDirectLightInfo(fragData.normal, fragData.viewDir, fragData.NoV, addLight.direction);
    specular += diffuseBRDF * SLZDirectBRDFSpecular(specInfo, surfData);
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
    real3 reflectionDir = reflect(-fragData.viewDir, fragData.normal);
    SLZImageBasedSpecular(specular, reflectionDir, fragData, surfData, ao.indirectAmbientOcclusion);
    SLZSpecularHorizonOcclusion(specular, fragData.normal, reflectionDir);
    
    //-------------------------------------------------------------------------------------------------
    // Combine the final lighting information
    //-------------------------------------------------------------------------------------------------
    
    return surfData.occlusion * (surfData.albedo * diffuse + specular) + surfData.emission;
}
#endif