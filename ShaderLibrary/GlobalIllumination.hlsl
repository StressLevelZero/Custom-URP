
#ifndef UNIVERSAL_GLOBAL_ILLUMINATION_INCLUDED
#define UNIVERSAL_GLOBAL_ILLUMINATION_INCLUDED

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/EntityLighting.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/ImageBasedLighting.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RealtimeLights.hlsl"

// SLZ MODIFIED
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SLZExtentions.hlsl"
// END SLZ MODIFIED

#if USE_FORWARD_PLUS
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"
#endif

// If lightmap is not defined than we evaluate GI (ambient + probes) from SH

// Renamed -> LIGHTMAP_SHADOW_MIXING
#if !defined(_MIXED_LIGHTING_SUBTRACTIVE) && defined(LIGHTMAP_SHADOW_MIXING) && !defined(SHADOWS_SHADOWMASK)
    #define _MIXED_LIGHTING_SUBTRACTIVE
#endif

// Samples SH L0, L1 and L2 terms
half3 SampleSH(half3 normalWS)
{
    // LPPV is not supported in Ligthweight Pipeline
    real4 SHCoefficients[7];
    SHCoefficients[0] = unity_SHAr;
    SHCoefficients[1] = unity_SHAg;
    SHCoefficients[2] = unity_SHAb;
    SHCoefficients[3] = unity_SHBr;
    SHCoefficients[4] = unity_SHBg;
    SHCoefficients[5] = unity_SHBb;
    SHCoefficients[6] = unity_SHC;

    return max(half3(0, 0, 0), SampleSH9(SHCoefficients, normalWS));
}

// SH Vertex Evaluation. Depending on target SH sampling might be
// done completely per vertex or mixed with L2 term per vertex and L0, L1
// per pixel. See SampleSHPixel
half3 SampleSHVertex(half3 normalWS)
{
#if defined(EVALUATE_SH_VERTEX)
    return SampleSH(normalWS);
#elif defined(EVALUATE_SH_MIXED)
    // no max since this is only L2 contribution
    return SHEvalLinearL2(normalWS, unity_SHBr, unity_SHBg, unity_SHBb, unity_SHC);
#endif

    // Fully per-pixel. Nothing to compute.
    return half3(0.0, 0.0, 0.0);
}

// SH Pixel Evaluation. Depending on target SH sampling might be done
// mixed or fully in pixel. See SampleSHVertex
half3 SampleSHPixel(half3 L2Term, half3 normalWS)
{
#if defined(EVALUATE_SH_VERTEX)
    return L2Term;
#elif defined(EVALUATE_SH_MIXED)
    half3 res = L2Term + SHEvalLinearL0L1(normalWS, unity_SHAr, unity_SHAg, unity_SHAb);
#ifdef UNITY_COLORSPACE_GAMMA
    res = LinearToSRGB(res);
#endif
    return max(half3(0, 0, 0), res);
#endif

    // Default: Evaluate SH fully per-pixel
    return SampleSH(normalWS);
}

// SLZ MODIFIED // Modified SH for toon shader

// SH Pixel Evaluation. Depending on target SH sampling might be done
// mixed or fully in pixel. See SampleSHVertex
half3 SampleSHPixelDir(half3 L2Term, half3 normalWS, half3 viewDir)
{
#if defined(ANIME)
    half3 shL1sum = unity_SHAr.xyz + unity_SHAg.xyz + unity_SHAb.xyz;
    float shL1Len2 = max(float(dot(shL1sum, shL1sum)), REAL_MIN);
    half3 shL1Dir = float3(shL1sum)*rsqrt(shL1Len2);
    half NoL = dot(normalWS, shL1Dir);
    normalWS = NoL > -0.75 ? shL1Dir : -shL1Dir;
#endif

#if defined(EVALUATE_SH_VERTEX)
    return L2Term;
#elif defined(EVALUATE_SH_MIXED)
    half3 res = SHEvalLinearL0L1(normalWS, unity_SHAr, unity_SHAg, unity_SHAb) + L2Term;
#ifdef UNITY_COLORSPACE_GAMMA
    res = LinearToSRGB(res);
#endif
    return max(half3(0, 0, 0), res);
#endif

    // Default: Evaluate SH fully per-pixel
    return SampleSH(normalWS);
}

// END SLZ MODIFIED

#if defined(UNITY_DOTS_INSTANCING_ENABLED)
#define LIGHTMAP_NAME unity_Lightmaps
#define LIGHTMAP_INDIRECTION_NAME unity_LightmapsInd
#define LIGHTMAP_SAMPLER_NAME samplerunity_Lightmaps
#define LIGHTMAP_SAMPLE_EXTRA_ARGS staticLightmapUV, unity_LightmapIndex.x
#else
#define LIGHTMAP_NAME unity_Lightmap
#define LIGHTMAP_INDIRECTION_NAME unity_LightmapInd
#define LIGHTMAP_SAMPLER_NAME samplerunity_Lightmap
#define LIGHTMAP_SAMPLE_EXTRA_ARGS staticLightmapUV
#endif

// Sample baked and/or realtime lightmap. Non-Direction and Directional if available.
half3 SampleLightmap(float2 staticLightmapUV, float2 dynamicLightmapUV, half3 normalWS)
{
#ifdef UNITY_LIGHTMAP_FULL_HDR
    bool encodedLightmap = false;
#else
    bool encodedLightmap = true;
#endif

    half4 decodeInstructions = half4(LIGHTMAP_HDR_MULTIPLIER, LIGHTMAP_HDR_EXPONENT, 0.0h, 0.0h);

    // The shader library sample lightmap functions transform the lightmap uv coords to apply bias and scale.
    // However, universal pipeline already transformed those coords in vertex. We pass half4(1, 1, 0, 0) and
    // the compiler will optimize the transform away.
    half4 transformCoords = half4(1, 1, 0, 0);

    float3 diffuseLighting = 0;

#if defined(LIGHTMAP_ON) && defined(DIRLIGHTMAP_COMBINED)
    diffuseLighting = SampleDirectionalLightmap(TEXTURE2D_LIGHTMAP_ARGS(LIGHTMAP_NAME, LIGHTMAP_SAMPLER_NAME),
        TEXTURE2D_LIGHTMAP_ARGS(LIGHTMAP_INDIRECTION_NAME, LIGHTMAP_SAMPLER_NAME),
        LIGHTMAP_SAMPLE_EXTRA_ARGS, transformCoords, normalWS, encodedLightmap, decodeInstructions);
#elif defined(LIGHTMAP_ON)
    diffuseLighting = SampleSingleLightmap(TEXTURE2D_LIGHTMAP_ARGS(LIGHTMAP_NAME, LIGHTMAP_SAMPLER_NAME), LIGHTMAP_SAMPLE_EXTRA_ARGS, transformCoords, encodedLightmap, decodeInstructions);
#endif

#if defined(DYNAMICLIGHTMAP_ON) && defined(DIRLIGHTMAP_COMBINED)
    diffuseLighting += SampleDirectionalLightmap(TEXTURE2D_ARGS(unity_DynamicLightmap, samplerunity_DynamicLightmap),
        TEXTURE2D_ARGS(unity_DynamicDirectionality, samplerunity_DynamicLightmap),
        dynamicLightmapUV, transformCoords, normalWS, false, decodeInstructions);
#elif defined(DYNAMICLIGHTMAP_ON)
    diffuseLighting += SampleSingleLightmap(TEXTURE2D_ARGS(unity_DynamicLightmap, samplerunity_DynamicLightmap),
        dynamicLightmapUV, transformCoords, false, decodeInstructions);
#endif

    return diffuseLighting;
}

// SLZ MODIFIED // Copy of SampleLightmap that additionally takes viewDir, and uses the SampleDirectionalLightmapSLZ function when sampling a directional lightmap

half4 SampleLightmapDir(float2 lightmapUV, float2 dynamicLightmapUV, half3 normalWS, half smoothness, float3 viewDirWS)
{
#ifdef UNITY_LIGHTMAP_FULL_HDR
    bool encodedLightmap = false;
#else
    bool encodedLightmap = true;
#endif

    half4 decodeInstructions = half4(LIGHTMAP_HDR_MULTIPLIER, LIGHTMAP_HDR_EXPONENT, 0.0h, 0.0h);

    // The shader library sample lightmap functions transform the lightmap uv coords to apply bias and scale.
    // However, universal pipeline already transformed those coords in vertex. We pass half4(1, 1, 0, 0) and
    // the compiler will optimize the transform away.
    half4 transformCoords = half4(1, 1, 0, 0);

#ifdef DIRLIGHTMAP_COMBINED
    return SampleDirectionalLightmapSLZ(TEXTURE2D_ARGS(unity_Lightmap, samplerunity_Lightmap),
        TEXTURE2D_ARGS(unity_LightmapInd, samplerunity_Lightmap),
        lightmapUV, transformCoords, normalWS, smoothness, viewDirWS, encodedLightmap, decodeInstructions);
#elif defined(LIGHTMAP_ON)
    return SampleSingleLightmap(TEXTURE2D_ARGS(unity_Lightmap, samplerunity_Lightmap), lightmapUV, transformCoords, encodedLightmap, decodeInstructions).rgbb;
#else
    return half4(0.0, 0.0, 0.0, 0.0);
#endif
}

// END SLZ MODIFIED

// Legacy version of SampleLightmap where Realtime GI is not supported.
half3 SampleLightmap(float2 staticLightmapUV, half3 normalWS)
{
    float2 dummyDynamicLightmapUV = float2(0,0);
    half3 result = SampleLightmap(staticLightmapUV, dummyDynamicLightmapUV, normalWS);
    return result;
}

// We either sample GI from baked lightmap or from probes.
// If lightmap: sampleData.xy = lightmapUV
// If probe: sampleData.xyz = L2 SH terms
#if defined(LIGHTMAP_ON) && defined(DYNAMICLIGHTMAP_ON)
#define SAMPLE_GI(staticLmName, dynamicLmName, shName, normalWSName) SampleLightmap(staticLmName, dynamicLmName, normalWSName)
#elif defined(DYNAMICLIGHTMAP_ON)
#define SAMPLE_GI(staticLmName, dynamicLmName, shName, normalWSName) SampleLightmap(0, dynamicLmName, normalWSName)
#elif defined(LIGHTMAP_ON)
#define SAMPLE_GI(staticLmName, shName, normalWSName) SampleLightmap(staticLmName, 0, normalWSName)
#else
#define SAMPLE_GI(staticLmName, shName, normalWSName) SampleSHPixel(shName, normalWSName)
#endif

// SLZ MODIFIED // Copies of the above macros using the directional equivalents

#if defined(LIGHTMAP_ON) && defined(DYNAMICLIGHTMAP_ON)
#define SAMPLE_GI_DIR(staticLmName, dynamicLmName, shName, normalWSName, smoothness, viewDirWS) SampleLightmapDir(staticLmName, dynamicLmName, normalWSName, smoothness, viewDirWS)
#elif defined(DYNAMICLIGHTMAP_ON)
#define SAMPLE_GI_DIR(staticLmName, dynamicLmName, shName, normalWSName, smoothness, viewDirWS) SampleLightmap(0, dynamicLmName, normalWSName)
#elif defined(LIGHTMAP_ON)
#define SAMPLE_GI_DIR(staticLmName, shName, normalWSName, smoothness, viewDirWS) SampleLightmapDir(staticLmName, 0, normalWSName, smoothness, viewDirWS)
#else
#define SAMPLE_GI_DIR(staticLmName, shName, normalWSName, smoothness, viewDirWS) SampleSHPixelDir(shName, normalWSName,viewDirWS)
#endif

// END SLZ MODIFIED

half3 BoxProjectedCubemapDirection(half3 reflectionWS, float3 positionWS, float4 cubemapPositionWS, float3 boxMin, float3 boxMax)
{
    // Is this probe using box projection?
    if (cubemapPositionWS.w > 0.0f)
    {
        float3 boxMinMax = (reflectionWS > 0.0f) ? boxMax.xyz : boxMin.xyz;
        half3 rbMinMax = half3(boxMinMax - positionWS) / reflectionWS;

        half fa = half(min(min(rbMinMax.x, rbMinMax.y), rbMinMax.z));

        half3 worldPos = half3(positionWS - cubemapPositionWS.xyz);

        half3 result = worldPos + reflectionWS * fa;
        return result;
    }
    else
    {
        return reflectionWS;
    }
}

half3 BoxProjectedCubemapDirection(half3 reflectionWS, float3 positionWS, float4 cubemapPositionWS, float4 boxMin, float4 boxMax)
{
    // Is this probe using box projection?
    if (cubemapPositionWS.w > 0.0f)
    {
        float3 boxMinMax = (reflectionWS > 0.0f) ? boxMax.xyz : boxMin.xyz;
        half3 rbMinMax = half3(boxMinMax - positionWS) / reflectionWS;

        half fa = half(min(min(rbMinMax.x, rbMinMax.y), rbMinMax.z));

        half3 worldPos = half3(positionWS - cubemapPositionWS.xyz);

        half3 result = worldPos + reflectionWS * fa;
        return result;
    }
    else
    {
        return reflectionWS;
    }
}

// Obligatory IQuilez SDF
float sdRoundBox(float3 worldPos, float3 boxMin, float3 boxMax, float radius)
{
    float3 center = 0.5 * (boxMin + boxMax);
    float3 relativePos = worldPos - center;
    float3 boxSize = max(0, 0.5 * (boxMax - boxMin) - radius);
    float3 q = abs(relativePos) - boxSize;
    return (length(max(q,0.0)) + min(max(q.x,max(q.y,q.z)),0.0));
}

float CalculateProbeWeight(float3 positionWS, float3 probeBoxMin, float3 probeBoxMax, float blendDistance)
{
    // SLZ MODIFIED // URP's blend distance is very stupid. This is mainly because it conflates blend distance and box projection.
    // This is likely done to minimize variables needed for the shader, but it causes issues.
    // The default behavior is that the box reflection direction is at the gizmo's edge, but the blend ENDs at that edge.
    // This means that you have a probe with a perfectly lined up box with the room to get accurate reflections, the walls will go dark with any blend distance.
    // So, do you just turn off blend distance? NO because that would cause harsh transition edges! >w<
    // The best thing to do was have a minimal amount of blending (~0.2) and pushing out the walls a bit. Now you have wrong reflections and little blending. :/
    // This lead to a juggle between two incorrect results of better reflections or better blending at design time. BAD, inconsistent, and hard to communicate best practices! 
    // This change biases towards the center of the box walls and offsets the edge slightly so it doesn't falloff unnaturally. It also avoids the sharp falloff on the edges.
    
    
    //float3 weightDir = max( (min(positionWS - probeBoxMin.xyz, probeBoxMax.xyz - positionWS) / blendDistance) + (blendDistance * .2f), 0);
    //return  saturate(weightDir.x * weightDir.y * weightDir.z);
    
    float sdBox = sdRoundBox(positionWS, probeBoxMin.xyz, probeBoxMax.xyz, blendDistance);
    return 1.0 - saturate(sdBox / blendDistance);
    //return saturate(min(weightDir.x, min(weightDir.y, weightDir.z))); //Yuck
    
    // END SLZ MODIFIED

}

float CalculateProbeWeight(float3 positionWS, float4 probeBoxMin, float4 probeBoxMax)
{
    return CalculateProbeWeight(positionWS, probeBoxMin.xyz, probeBoxMax.xyz, probeBoxMax.w);
}


half CalculateProbeVolumeSqrMagnitude(float4 probeBoxMin, float4 probeBoxMax)
{
    half3 maxToMin = half3(probeBoxMax.xyz - probeBoxMin.xyz);
    return dot(maxToMin, maxToMin);
}

half3 CalculateIrradianceFromReflectionProbes(half3 reflectVector, float3 positionWS, half perceptualRoughness, float2 normalizedScreenSpaceUV)
{
    half3 irradiance = half3(0.0h, 0.0h, 0.0h);
    half mip = PerceptualRoughnessToMipmapLevel(perceptualRoughness);
#if USE_FORWARD_PLUS
    float totalWeight = 0.0f;
    uint probeIndex;
    ClusterIterator it = ClusterInit(normalizedScreenSpaceUV, positionWS, 1);
    [loop] while (ClusterNext(it, probeIndex) && totalWeight < 0.99f)
    {
        probeIndex -= URP_FP_PROBES_BEGIN;

        float weight = CalculateProbeWeight(positionWS, urp_ReflProbes_BoxMin[probeIndex], urp_ReflProbes_BoxMax[probeIndex]);
        weight = min(weight, 1.0f - totalWeight);

        half3 sampleVector = reflectVector;
#ifdef _REFLECTION_PROBE_BOX_PROJECTION
        sampleVector = BoxProjectedCubemapDirection(reflectVector, positionWS, urp_ReflProbes_ProbePosition[probeIndex], urp_ReflProbes_BoxMin[probeIndex], urp_ReflProbes_BoxMax[probeIndex]);
#endif // _REFLECTION_PROBE_BOX_PROJECTION

        uint maxMip = (uint)abs(urp_ReflProbes_ProbePosition[probeIndex].w) - 1;
        half probeMip = min(mip, maxMip);
        float2 uv = saturate(PackNormalOctQuadEncode(sampleVector) * 0.5 + 0.5);

        float mip0 = floor(probeMip);
        float mip1 = mip0 + 1;
        float mipBlend = probeMip - mip0;
        float4 scaleOffset0 = urp_ReflProbes_MipScaleOffset[probeIndex * 7 + (uint)mip0];
        float4 scaleOffset1 = urp_ReflProbes_MipScaleOffset[probeIndex * 7 + (uint)mip1];

        half3 irradiance0 = half4(SAMPLE_TEXTURE2D_LOD(urp_ReflProbes_Atlas, samplerurp_ReflProbes_Atlas, uv * scaleOffset0.xy + scaleOffset0.zw, 0.0)).rgb;
        half3 irradiance1 = half4(SAMPLE_TEXTURE2D_LOD(urp_ReflProbes_Atlas, samplerurp_ReflProbes_Atlas, uv * scaleOffset1.xy + scaleOffset1.zw, 0.0)).rgb;
        irradiance += weight * lerp(irradiance0, irradiance1, mipBlend);
        totalWeight += weight;
    }
#else
    half probe0Volume = CalculateProbeVolumeSqrMagnitude(unity_SpecCube0_BoxMin, unity_SpecCube0_BoxMax);
    half probe1Volume = CalculateProbeVolumeSqrMagnitude(unity_SpecCube1_BoxMin, unity_SpecCube1_BoxMax);

    
    half volumeDiff = probe0Volume - probe1Volume;
    float importanceSign = unity_SpecCube1_BoxMin.w;

    // A probe is dominant if its importance is higher
    // Or have equal importance but smaller volume
    bool probe0Dominant = importanceSign > 0.0f || (importanceSign == 0.0f && volumeDiff < -0.0001h);
    bool probe1Dominant = importanceSign < 0.0f || (importanceSign == 0.0f && volumeDiff > 0.0001h);

    float3 paddedProbe0Min = unity_SpecCube0_BoxMin.xyz - 0.25 * unity_SpecCube0_BoxMax.w;
    float3 paddedProbe0Max = unity_SpecCube0_BoxMax.xyz + 0.25 * unity_SpecCube0_BoxMax.w;
    float desiredWeightProbe0 = CalculateProbeWeight(positionWS, paddedProbe0Min, paddedProbe0Max, unity_SpecCube0_BoxMax.w);
    float3 paddedProbe1Min = unity_SpecCube1_BoxMin.xyz - 0.25 * unity_SpecCube1_BoxMax.w;
    float3 paddedProbe1Max = unity_SpecCube1_BoxMax.xyz + 0.25 * unity_SpecCube1_BoxMax.w;
    float desiredWeightProbe1 = CalculateProbeWeight(positionWS, paddedProbe1Min, paddedProbe1Max, unity_SpecCube0_BoxMax.w);

    // Subject the probes weight if the other probe is dominant
    float weightProbe0 = probe1Dominant ? min(desiredWeightProbe0, 1.0f - desiredWeightProbe1) : desiredWeightProbe0;
    float weightProbe1 = probe0Dominant ? min(desiredWeightProbe1, 1.0f - desiredWeightProbe0) : desiredWeightProbe1;

    float totalWeight = weightProbe0 + weightProbe1;

    // If either probe 0 or probe 1 is dominant the sum of weights is guaranteed to be 1.
    // If neither is dominant this is not guaranteed - only normalize weights if totalweight exceeds 1.
    weightProbe0 /= max(totalWeight, 1.0f);
    weightProbe1 /= max(totalWeight, 1.0f);
    
    // Sample the first reflection probe
#if defined(UNITY_COMPILER_DXC) && defined(_SM6_WAVE)
    if (WaveActiveAnyTrue(weightProbe0 > 0.01f))
#else
    if (weightProbe0 > 0.01f)
#endif
    {
        half3 reflectVector0 = reflectVector;
#ifdef _REFLECTION_PROBE_BOX_PROJECTION
        reflectVector0 = BoxProjectedCubemapDirection(reflectVector, positionWS, unity_SpecCube0_ProbePosition, paddedProbe0Min, paddedProbe0Max);
#endif // _REFLECTION_PROBE_BOX_PROJECTION

        half4 encodedIrradiance = half4(SAMPLE_TEXTURECUBE_LOD(unity_SpecCube0, samplerunity_SpecCube0, reflectVector0, mip));

        irradiance += weightProbe0 * DecodeHDREnvironment(encodedIrradiance, unity_SpecCube0_HDR);
    }

    // Sample the second reflection probe
#if defined(UNITY_COMPILER_DXC) && defined(_SM6_WAVEVOTE)
    if (WaveActiveAnyTrue(weightProbe1 > 0.01f))
#else
    if (weightProbe1 > 0.01f)
#endif
    {
        half3 reflectVector1 = reflectVector;
#ifdef _REFLECTION_PROBE_BOX_PROJECTION
        reflectVector1 = BoxProjectedCubemapDirection(reflectVector, positionWS, unity_SpecCube1_ProbePosition, paddedProbe1Min, paddedProbe1Max);
#endif // _REFLECTION_PROBE_BOX_PROJECTION
        half4 encodedIrradiance = half4(SAMPLE_TEXTURECUBE_LOD(unity_SpecCube1, samplerunity_SpecCube1, reflectVector1, mip));

        irradiance += weightProbe1 * DecodeHDREnvironment(encodedIrradiance, unity_SpecCube1_HDR);
    }
#endif

 
#if !defined(SHADER_API_MOBILE) // Don't do 3 samples on mobile
    // Use any remaining weight to blend to environment reflection cube map
    if (totalWeight < 0.99f)
    {
        half4 encodedIrradiance = half4(SAMPLE_TEXTURECUBE_LOD(_GlossyEnvironmentCubeMap, sampler_GlossyEnvironmentCubeMap, reflectVector, mip));

        irradiance += (1.0f - totalWeight) * DecodeHDREnvironment(encodedIrradiance, _GlossyEnvironmentCubeMap_HDR);
    }
#endif

    return irradiance;
}

/* Complete failure. Can't assign to a TextureCube variable (causes compiler crash on both fxc and dxc), and can't access a constructed array of textures */
half3 CalculateIrradianceBlendDither(half3 reflectVector, float3 positionWS, half perceptualRoughness, float2 normalizedScreenSpaceUV)
{
    half3 irradiance = half3(0.0h, 0.0h, 0.0h);
    half mip = PerceptualRoughnessToMipmapLevel(perceptualRoughness);

    half probe0Volume = CalculateProbeVolumeSqrMagnitude(unity_SpecCube0_BoxMin, unity_SpecCube0_BoxMax);
    half probe1Volume = CalculateProbeVolumeSqrMagnitude(unity_SpecCube1_BoxMin, unity_SpecCube1_BoxMax);

    
    half volumeDiff = probe0Volume - probe1Volume;
    float importanceSign = unity_SpecCube1_BoxMin.w;

    // A probe is dominant if its importance is higher
    // Or have equal importance but smaller volume
    bool probe0Dominant = importanceSign > 0.0f || (importanceSign == 0.0f && volumeDiff < -0.0001h);
    bool probe1Dominant = importanceSign < 0.0f || (importanceSign == 0.0f && volumeDiff > 0.0001h);

    float3 paddedProbe0Min = unity_SpecCube0_BoxMin.xyz - 0.25 * unity_SpecCube0_BoxMax.w;
    float3 paddedProbe0Max = unity_SpecCube0_BoxMax.xyz + 0.25 * unity_SpecCube0_BoxMax.w;
    half desiredWeightProbe0 = CalculateProbeWeight(positionWS, paddedProbe0Min, paddedProbe0Max, unity_SpecCube0_BoxMax.w);
    float3 paddedProbe1Min = unity_SpecCube1_BoxMin.xyz - 0.25 * unity_SpecCube1_BoxMax.w;
    float3 paddedProbe1Max = unity_SpecCube1_BoxMax.xyz + 0.25 * unity_SpecCube1_BoxMax.w;
    half desiredWeightProbe1 = CalculateProbeWeight(positionWS, paddedProbe1Min, paddedProbe1Max, unity_SpecCube0_BoxMax.w);

    // Subject the probes weight if the other probe is dominant
    half weightProbe0 = probe1Dominant ? min(desiredWeightProbe0, 1.0f - desiredWeightProbe1) : desiredWeightProbe0;
    weightProbe0 += 0.00001;
    half weightProbe1 = probe0Dominant ? min(desiredWeightProbe1, 1.0f - desiredWeightProbe0) : desiredWeightProbe1;

    float totalWeight = weightProbe0 + weightProbe1;

    // If either probe 0 or probe 1 is dominant the sum of weights is guaranteed to be 1.
    // If neither is dominant this is not guaranteed - only normalize weights if totalweight exceeds 1.
    weightProbe0 /= totalWeight;
    weightProbe1 /= totalWeight;
    
    const half4x4 bayerWeight = half4x4(
     0.0/16.0, 12.0/16.0,  3.0/16.0, 15.0/16.0,
     8.0/16.0,  4.0/16.0, 11.0/16.0,  7.0/16.0,
     2.0/16.0, 14.0/16.0,  1.0/16.0, 13.0/16.0,
    10.0/16.0,  6.0/16.0,  9.0/16.0,  5.0/16.0);
    
    int2 screenCoords = clamp(fmod(normalizedScreenSpaceUV * _ScaledScreenParams.xy, 4.0), 0, 3);
    bool probe1 = weightProbe1 > bayerWeight[screenCoords.x][screenCoords.y];
    
    float4 probePos = probe1 ? unity_SpecCube1_ProbePosition : unity_SpecCube0_ProbePosition;
    float3 probeMin = probe1 ? paddedProbe1Min : paddedProbe0Min;
    float3 probeMax = probe1 ? paddedProbe1Max : paddedProbe0Max;
#ifdef _REFLECTION_PROBE_BOX_PROJECTION
    reflectVector = BoxProjectedCubemapDirection(reflectVector, positionWS, probePos, probeMin, probeMax);
#endif

	half4 encodedIrradiance = probe1 ?
        half4(SAMPLE_TEXTURECUBE_LOD(unity_SpecCube1, samplerunity_SpecCube0, reflectVector, mip)) :
        half4(SAMPLE_TEXTURECUBE_LOD(unity_SpecCube0, samplerunity_SpecCube0, reflectVector, mip));
    half4 decodeInstructions = probe1 ? unity_SpecCube1_HDR : unity_SpecCube0_HDR;
    irradiance = DecodeHDREnvironment(encodedIrradiance, decodeInstructions);
    return irradiance;
}


#if !USE_FORWARD_PLUS
half3 CalculateIrradianceFromReflectionProbes(half3 reflectVector, float3 positionWS, half perceptualRoughness)
{
    return CalculateIrradianceFromReflectionProbes(reflectVector, positionWS, perceptualRoughness, float2(0.0f, 0.0f));
}
#endif

half3 GlossyEnvironmentReflection(half3 reflectVector, float3 positionWS, half perceptualRoughness, half occlusion, float2 normalizedScreenSpaceUV)
{
#if !defined(_ENVIRONMENTREFLECTIONS_OFF)
    half3 irradiance;

#if defined(_REFLECTION_PROBE_BLENDING) || USE_FORWARD_PLUS
    #if defined(SPECULAR_PROBE_BLEND_USE_DITHER)
        irradiance = CalculateIrradianceBlendDither(reflectVector, positionWS, perceptualRoughness, normalizedScreenSpaceUV);
    #else
        irradiance = CalculateIrradianceFromReflectionProbes(reflectVector, positionWS, perceptualRoughness, normalizedScreenSpaceUV);
    #endif
#else


#ifdef _REFLECTION_PROBE_BOX_PROJECTION
    float3 paddedProbe0Min = unity_SpecCube0_BoxMin.xyz - 0.25 * unity_SpecCube0_BoxMax.w;
    float3 paddedProbe0Max = unity_SpecCube0_BoxMax.xyz + 0.25 * unity_SpecCube0_BoxMax.w;
    half3 reflectVectorProj = BoxProjectedCubemapDirection(reflectVector, positionWS, unity_SpecCube0_ProbePosition, paddedProbe0Min, paddedProbe0Max);
    half probeWeight =  CalculateProbeWeight(positionWS, paddedProbe0Min, paddedProbe0Max, unity_SpecCube0_BoxMax.w);
    //reflectVector = probeWeight > 0 ? reflectVectorProj : reflectVector;
    reflectVector = lerp(normalize(reflectVector), normalize(reflectVectorProj), probeWeight);
#endif // _REFLECTION_PROBE_BOX_PROJECTION
    half mip = PerceptualRoughnessToMipmapLevel(perceptualRoughness);
    half4 encodedIrradiance = half4(SAMPLE_TEXTURECUBE_LOD(unity_SpecCube0, samplerunity_SpecCube0, reflectVector, mip));

    irradiance = DecodeHDREnvironment(encodedIrradiance, unity_SpecCube0_HDR);
#endif // _REFLECTION_PROBE_BLENDING
    return irradiance * occlusion;

#else //_ENVIRONMENTREFLECTIONS_OFF
    return _GlossyEnvironmentColor.rgb * occlusion;
#endif // _ENVIRONMENTREFLECTIONS_OFF

}

#if !USE_FORWARD_PLUS
half3 GlossyEnvironmentReflection(half3 reflectVector, float3 positionWS, half perceptualRoughness, half occlusion)
{
    return GlossyEnvironmentReflection(reflectVector, positionWS, perceptualRoughness, occlusion, float2(0.0f, 0.0f));
}
#endif

half3 GlossyEnvironmentReflection(half3 reflectVector, half perceptualRoughness, half occlusion)
{
#if !defined(_ENVIRONMENTREFLECTIONS_OFF)
    half3 irradiance;
    half mip = PerceptualRoughnessToMipmapLevel(perceptualRoughness);
    half4 encodedIrradiance = half4(SAMPLE_TEXTURECUBE_LOD(unity_SpecCube0, samplerunity_SpecCube0, reflectVector, mip));

    irradiance = DecodeHDREnvironment(encodedIrradiance, unity_SpecCube0_HDR);

    return irradiance * occlusion;
#else

    return _GlossyEnvironmentColor.rgb * occlusion;
#endif // _ENVIRONMENTREFLECTIONS_OFF
}

half3 SubtractDirectMainLightFromLightmap(Light mainLight, half3 normalWS, half3 bakedGI)
{
    // Let's try to make realtime shadows work on a surface, which already contains
    // baked lighting and shadowing from the main sun light.
    // Summary:
    // 1) Calculate possible value in the shadow by subtracting estimated light contribution from the places occluded by realtime shadow:
    //      a) preserves other baked lights and light bounces
    //      b) eliminates shadows on the geometry facing away from the light
    // 2) Clamp against user defined ShadowColor.
    // 3) Pick original lightmap value, if it is the darkest one.


    // 1) Gives good estimate of illumination as if light would've been shadowed during the bake.
    // We only subtract the main direction light. This is accounted in the contribution term below.
    half shadowStrength = GetMainLightShadowStrength();
    half contributionTerm = saturate(dot(mainLight.direction, normalWS));
    half3 lambert = mainLight.color.rgb * contributionTerm; // SLZ MODIFIED // PS5 can't implicitly cast float4 to float3, explicitly specify rgb
    half3 estimatedLightContributionMaskedByInverseOfShadow = lambert * (1.0 - mainLight.shadowAttenuation);
    half3 subtractedLightmap = bakedGI - estimatedLightContributionMaskedByInverseOfShadow;

    // 2) Allows user to define overall ambient of the scene and control situation when realtime shadow becomes too dark.
    half3 realtimeShadow = max(subtractedLightmap, _SubtractiveShadowColor.xyz);
    realtimeShadow = lerp(bakedGI, realtimeShadow, shadowStrength);

    // 3) Pick darkest color
    return min(bakedGI, realtimeShadow);
}



half3 GlobalIllumination(BRDFData brdfData, BRDFData brdfDataClearCoat, float clearCoatMask,
    half3 bakedGI, half4 occlusion, float3 positionWS, // SLZ MODIFIED // made occlusion float4, not sure why? Colored occlusion?
    half3 normalWS, half3 viewDirectionWS, float2 normalizedScreenSpaceUV)
{
    half3 reflectVector = reflect(-viewDirectionWS, normalWS);
    half NoV = saturate(dot(normalWS, viewDirectionWS));
    half fresnelTerm = Pow4(1.0 - NoV);

    half3 indirectDiffuse = bakedGI;
    // SLZ MODIFIED // Specular occlusion. Adding a some hacky methods of using AO for spec occlusion. Not perfect, but helps a lot in shadowed areas.    
    half3 LitSpecularOcclusion = BakedLightingToSpecularOcclusion(bakedGI);
    half AOSpecularOcclusion = GetSpecularOcclusionFromAmbientOcclusion(NoV, occlusion, brdfData.roughness);
    half3 indirectSpecular = GlossyEnvironmentReflection(reflectVector, positionWS, brdfData.perceptualRoughness, AOSpecularOcclusion, normalizedScreenSpaceUV)*LitSpecularOcclusion ;
    // END SLZ MODIFIED

    half3 color = EnvironmentBRDF(brdfData, indirectDiffuse, indirectSpecular, fresnelTerm);

    // SLZ MODIFIED // Add fluorescence calculation
    BlendFluorescence(color, half4(bakedGI, 0), brdfData);
    // END SLZ MODIFIED
    // SLZ MODIFIED // This is in the function and does nothing unless this keyword is enabled
    #if defined(DEBUG_DISPLAY) 
    if (IsOnlyAOLightingFeatureEnabled()) color = half3(1,1,1); // "Base white" for AO debug lighting mode
    #endif
    // END SLZ MODIFIED

#if defined(_CLEARCOAT) || defined(_CLEARCOATMAP)
    half3 coatIndirectSpecular = GlossyEnvironmentReflection(reflectVector, positionWS, brdfDataClearCoat.perceptualRoughness, 1.0h, normalizedScreenSpaceUV);
    // TODO: "grazing term" causes problems on full roughness
    half3 coatColor = EnvironmentBRDFClearCoat(brdfDataClearCoat, clearCoatMask, coatIndirectSpecular, fresnelTerm);

    // Blend with base layer using khronos glTF recommended way using NoV
    // Smooth surface & "ambiguous" lighting
    // NOTE: fresnelTerm (above) is pow4 instead of pow5, but should be ok as blend weight.
    half coatFresnel = kDielectricSpec.x + kDielectricSpec.a * fresnelTerm;
    return (color * (1.0 - coatFresnel * clearCoatMask) + coatColor) * occlusion;
#else
    return color * occlusion;
#endif
}

#if !USE_FORWARD_PLUS
half3 GlobalIllumination(BRDFData brdfData, BRDFData brdfDataClearCoat, float clearCoatMask,
    half3 bakedGI, half occlusion, float3 positionWS,
    half3 normalWS, half3 viewDirectionWS)
{
    return GlobalIllumination(brdfData, brdfDataClearCoat, clearCoatMask, bakedGI, occlusion, positionWS, normalWS, viewDirectionWS, float2(0.0f, 0.0f));
}
#endif

// Backwards compatiblity
half3 GlobalIllumination(BRDFData brdfData, half3 bakedGI, half occlusion, float3 positionWS, half3 normalWS, half3 viewDirectionWS)
{
    const BRDFData noClearCoat = (BRDFData)0;
    return GlobalIllumination(brdfData, noClearCoat, 0.0, bakedGI, occlusion, positionWS, normalWS, viewDirectionWS, 0);
}

half3 GlobalIllumination(BRDFData brdfData, BRDFData brdfDataClearCoat, float clearCoatMask,
    half3 bakedGI, half occlusion,
    half3 normalWS, half3 viewDirectionWS)
{
    half3 reflectVector = reflect(-viewDirectionWS, normalWS);
    half NoV = saturate(dot(normalWS, viewDirectionWS));
    half fresnelTerm = Pow4(1.0 - NoV);

    half3 indirectDiffuse = bakedGI;
    half3 indirectSpecular = GlossyEnvironmentReflection(reflectVector, brdfData.perceptualRoughness, half(1.0));

    half3 color = EnvironmentBRDF(brdfData, indirectDiffuse, indirectSpecular, fresnelTerm);

#if defined(_CLEARCOAT) || defined(_CLEARCOATMAP)
    half3 coatIndirectSpecular = GlossyEnvironmentReflection(reflectVector, brdfDataClearCoat.perceptualRoughness, half(1.0));
    // TODO: "grazing term" causes problems on full roughness
    half3 coatColor = EnvironmentBRDFClearCoat(brdfDataClearCoat, clearCoatMask, coatIndirectSpecular, fresnelTerm);

    // Blend with base layer using khronos glTF recommended way using NoV
    // Smooth surface & "ambiguous" lighting
    // NOTE: fresnelTerm (above) is pow4 instead of pow5, but should be ok as blend weight.
    half coatFresnel = kDielectricSpec.x + kDielectricSpec.a * fresnelTerm;
    return (color * (1.0 - coatFresnel * clearCoatMask) + coatColor) * occlusion;
#else
    return color * occlusion;
#endif
}


half3 GlobalIllumination(BRDFData brdfData, half3 bakedGI, half occlusion, half3 normalWS, half3 viewDirectionWS)
{
    const BRDFData noClearCoat = (BRDFData)0;
    return GlobalIllumination(brdfData, noClearCoat, 0.0, bakedGI, occlusion, normalWS, viewDirectionWS);
}

void MixRealtimeAndBakedGI(inout Light light, half3 normalWS, inout half3 bakedGI)
{
#if defined(LIGHTMAP_ON) && defined(_MIXED_LIGHTING_SUBTRACTIVE)
    bakedGI = SubtractDirectMainLightFromLightmap(light, normalWS, bakedGI);
#endif
}

// Backwards compatibility
void MixRealtimeAndBakedGI(inout Light light, half3 normalWS, inout half3 bakedGI, half4 shadowMask)
{
    MixRealtimeAndBakedGI(light, normalWS, bakedGI);
}

void MixRealtimeAndBakedGI(inout Light light, half3 normalWS, inout half3 bakedGI, AmbientOcclusionFactor aoFactor)
{
    if (IsLightingFeatureEnabled(DEBUGLIGHTINGFEATUREFLAGS_AMBIENT_OCCLUSION))
    {
        bakedGI *= aoFactor.indirectAmbientOcclusion;
    }

    MixRealtimeAndBakedGI(light, normalWS, bakedGI);
}

#endif
