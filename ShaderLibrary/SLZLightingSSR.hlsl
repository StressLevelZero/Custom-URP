/* 
 * Stress Level Zero Lighting functions
 */   

#ifndef SLZ_PBR_LIGHTING_SSR
#define SLZ_PBR_LIGHTING_SSR

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SLZLighting.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SSR.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SSRGlobals.hlsl"

half4 CalcFogFactors(real3 viewDirectionWS, real fogFactor)
{
    half4 fogFactors = half4(0, 0, 0, 0);
#if defined(FOG_LINEAR) || defined(FOG_EXP) || defined(FOG_EXP2)
    fogFactors.w = ComputeFogIntensity(fogFactor);
    fogFactors.xyz = MipFog(viewDirectionWS, fogFactor, 7);
    //fogFactors = lerp(mipFog, fragColor, fogIntensity);
#endif
    return fogFactors;
}

half3 invertFogLerp(half fogIntensity, half3 mipFog, half3 finalColor)
{
    return fogIntensity > 1e-7 ? (finalColor + (fogIntensity - 1) * mipFog) / fogIntensity : finalColor;
}

struct SSRExtraData
{
    real3 meshNormal; 
    float4 lastClipPos;
    float temporalWeight;
    float depthDerivativeSum;
    real4 noise;
    real fogFactor;
};

 /**
  * Specular from reflection probes mixed with SSR
  *
  *
  * @param[in,out] specular  Running total of the specular color
  * @param         fragData  Struct containing all relevant fragment data (normal, position, etc)
  * @param         surfData  Struct containing physical properties of the surface (specular color, roughness, etc)
  * @param         indSSAO   Indirect screenspace ambient occlusion, not used if SSAO isn't enabled
  */
void SLZImageBasedSpecularSSR(inout real3 specular, half3 reflectionDir, const SLZFragData fragData, const SLZSurfData surfData, SSRData ssrData, half indSSAO)
{
    real3 reflectionProbe = GlossyEnvironmentReflection(reflectionDir, fragData.position, surfData.perceptualRoughness, 1.0h);

    real surfaceReduction = 1.0h / (surfData.roughness * surfData.roughness + 1.0h);
    real3 grazingTerm = saturate((1.0h - surfData.perceptualRoughness) + surfData.reflectivity);
    real fresnelTerm = (1.0h - saturate(fragData.NoV));
    fresnelTerm *= fresnelTerm;
    fresnelTerm *= fresnelTerm; // fresnelTerm ^ 4
    real3 IBSpec = real3(surfaceReduction * lerp(surfData.specular, grazingTerm, fresnelTerm));
   
#if !defined(SHADER_API_MOBILE)
    //ssrData.perceptualRoughness = -fresnelTerm * ssrData.perceptualRoughness + ssrData.perceptualRoughness;
    real SSRLerp = smoothstep(0.3, 0.9, 1- surfData.roughness * surfData.roughness);
    real4 SSRColor = real4(0, 0, 0, 0);
    UNITY_BRANCH if (SSRLerp > 0.008)
    {
        SSRColor = getSSRColor(ssrData);
    }
    /**/
#if defined(UNITY_COMPILER_DXC) && defined(_SM6_QUAD)
    
    real4 colorX = QuadReadAcrossX(SSRColor);
    real4 colorY = QuadReadAcrossY(SSRColor);
    real4 colorD = QuadReadAcrossDiagonal(SSRColor);
    real alphaAvg = max(colorX.a + colorY.a + colorD.a, 1e-6);
    real4 colorAvg = real4((colorX.a * colorX + colorY.a * colorY + colorD.a*colorD) / (alphaAvg));//, alphaAvg);
    SSRColor = lerp(colorAvg, SSRColor,-SSRColor.a * saturate(2 * ssrData.perceptualRoughness) + SSRColor.a);
    
#endif
    reflectionProbe = lerp(reflectionProbe, SSRColor.rgb, SSRColor.a * SSRLerp);
#endif

#if defined(_SCREEN_SPACE_OCCLUSION)
    reflectionProbe *= indSSAO;
#endif

    specular += IBSpec * reflectionProbe;
}



real3 SLZPBRFragmentSSR(SLZFragData fragData, SLZSurfData surfData, SSRExtraData ssrExtra)
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

    

    SSRData ssrData = GetSSRData(
        fragData.position,
        fragData.viewDir,
        reflectionDir,
        ssrExtra.meshNormal,
        surfData.perceptualRoughness,
        ssrExtra.depthDerivativeSum,
        ssrExtra.noise);

    SLZImageBasedSpecularSSR(specular, reflectionDir, fragData, surfData, ssrData, ao.indirectAmbientOcclusion);
    SLZSpecularHorizonOcclusion(specular, fragData.normal, reflectionDir);

    float3 output;
    float2 oldScreenUV = SLZComputeNDCFromClip(ssrExtra.lastClipPos);
    real SSRLerp = smoothstep(0.3, 0.9, 1 - surfData.roughness * surfData.roughness);
    float4 volColor = GetVolumetricColor(fragData.position);
    UNITY_BRANCH if (SSRLerp < 0.0008 || oldScreenUV.x < 0 || oldScreenUV.y < 0 || oldScreenUV.x > 1 || oldScreenUV.y > 1)
    {
        output = surfData.occlusion * (surfData.albedo * diffuse + specular) + surfData.emission;
    }
    else
    {

        float3 oldColor = SAMPLE_TEXTURE2D_X_LOD(_CameraOpaqueTexture, sampler_trilinear_clamp, UnityStereoTransformScreenSpaceTex(oldScreenUV), 0).rgb;

#if defined(_VOLUMETRICS_ENABLED)
        oldColor = (oldColor - volColor.rgb) / volColor.a;
#endif

#if defined(FOG_LINEAR) || defined(FOG_EXP) || defined(FOG_EXP2)
        half4 fogFactors = CalcFogFactors(-fragData.viewDir, ssrExtra.fogFactor);
        oldColor = invertFogLerp(fogFactors.w, fogFactors.rgb, oldColor);
#endif


        float3 currentDiffuse = surfData.occlusion * surfData.albedo * diffuse + surfData.emission;
        oldColor = max(0, oldColor - currentDiffuse);

        float3 currentSpecular = specular * surfData.occlusion;
        currentSpecular = (1 - ssrExtra.temporalWeight) * currentSpecular + ssrExtra.temporalWeight * oldColor;

        output = (currentSpecular + currentDiffuse);
    }

    //-------------------------------------------------------------------------------------------------
    // Combine the final lighting information
    //-------------------------------------------------------------------------------------------------

    //Do fog and volumetrics here to avoid sampling the volumetrics twice

    output = MixFog(output, -fragData.viewDir, ssrExtra.fogFactor);

#if defined(_VOLUMETRICS_ENABLED)
    output = volColor.rgb + output * volColor.a;
#endif

    return output;//surfData.occlusion* (surfData.albedo * diffuse + specular) + surfData.emission;
}

#endif