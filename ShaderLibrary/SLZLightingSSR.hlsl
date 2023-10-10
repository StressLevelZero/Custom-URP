/* 
 * Stress Level Zero Lighting functions
 */   

#ifndef SLZ_PBR_LIGHTING_SSR
#define SLZ_PBR_LIGHTING_SSR

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SLZLighting.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SSR.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SSRGlobals.hlsl"

#if !defined(DYNAMIC_ADDITIONAL_LIGHTS) && !defined(_ADDITIONAL_LIGHTS)
#define _ADDITIONAL_LIGHTS false
#endif

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

//real SLZSpecularHorizonOcclusion(half3 normal, half3 reflectionDir)
//{
//    real horizonOcclusion = min(1.0h + dot(reflectionDir, normal), 1.0h);
//    return horizonOcclusion * horizonOcclusion;
//}

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
  * Specular from reflection probes and SSR split, so we can reverse the non-ssr color later
  *
  *
  * @param[in,out] specular  Running total of the specular color
  * @param         fragData  Struct containing all relevant fragment data (normal, position, etc)
  * @param         surfData  Struct containing physical properties of the surface (specular color, roughness, etc)
  * @param         indSSAO   Indirect screenspace ambient occlusion, not used if SSAO isn't enabled
  */
void SLZImageBasedSpecularSSR(inout real3 specular, inout real3 SSRColor, inout real SSRLerp, half3 reflectionDir, const SLZFragData fragData, const SLZSurfData surfData, SSRExtraData ssrExtra, half indSSAO)
{
    real3 reflectionProbe = GlossyEnvironmentReflection(reflectionDir, fragData.position, surfData.perceptualRoughness, 1.0h);

    
   
#if !defined(SHADER_API_MOBILE)
    //ssrData.perceptualRoughness = -fresnelTerm * ssrData.perceptualRoughness + ssrData.perceptualRoughness;
   
    half RdotV = saturate(0.95 * dot(reflectionDir, -fragData.viewDir.xyz) + 0.05);


    SSRData ssrData = GetSSRData(
        fragData.position,
        fragData.viewDir,
        reflectionDir,
        ssrExtra.meshNormal,
        surfData.perceptualRoughness,
        RdotV,
        ssrExtra.depthDerivativeSum,
        ssrExtra.noise);

    SSRLerp = saturate((surfData.perceptualRoughness - 0.5) / (0.4 - 0.5));
    //Piecewise function to make a sinusoidal falloff curve
#define SSR_FALLOFF_START 0.6666667
    RdotV = 2 * saturate( (1 / SSR_FALLOFF_START) * RdotV);
    RdotV = RdotV > 1 ? -0.5*(RdotV * RdotV) + (2*RdotV - 1) : 0.5 * RdotV * RdotV;
    SSRLerp *= RdotV;
    real4 SSR = real4(0, 0, 0, 0);
    #if defined(_SM6_QUAD)
    if (WaveActiveAnyTrue(SSRLerp > 0.008))
    #else
    if (SSRLerp > 0.008)
    #endif
    {
        SSR = getSSRColor(ssrData);
    }
    


    //reflectionProbe = lerp(reflectionProbe, SSRColor.rgb, SSRColor.a * SSRLerp);
    SSRColor = SSR.rgb;
    reflectionProbe *= (1.0 - SSR.a * SSRLerp);
    SSRColor *= SSR.a * SSRLerp;
#endif

    UNITY_BRANCH if (_SCREEN_SPACE_OCCLUSION)
    {
        reflectionProbe *= indSSAO;
        SSRColor.rgb *= indSSAO;
    }

    real surfaceReduction = 1.0h / (surfData.roughness * surfData.roughness + 1.0h);
    real3 grazingTerm = saturate((1.0h - surfData.perceptualRoughness) + surfData.reflectivity);
    real fresnelTerm = (1.0h - saturate(fragData.NoV));
    fresnelTerm *= fresnelTerm;
    fresnelTerm *= fresnelTerm; // fresnelTerm ^ 4
    real3 IBSpec = real3(surfaceReduction * lerp(surfData.specular, grazingTerm, fresnelTerm));

    reflectionProbe *= IBSpec;
    SSRColor.rgb *= IBSpec;

    specular += reflectionProbe;
}



real4 SLZPBRFragmentSSR(SLZFragData fragData, SLZSurfData surfData, SSRExtraData ssrExtra, int surfaceType = 0)
{
    real3 diffuse = real3(0.0h, 0.0h, 0.0h);
    real3 specular = real3(0.0h, 0.0h, 0.0h);
    //real2 dfg = SLZDFG(fragData.NoV, surfData.roughness);
    


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
    AmbientOcclusionFactor ao = (AmbientOcclusionFactor)0;

    UNITY_BRANCH if (_SCREEN_SPACE_OCCLUSION)
    {
        
        ao = CreateAmbientOcclusionFactor(fragData.screenUV, surfData.occlusion);
        if (surfaceType > 0) ao.indirectAmbientOcclusion = 1; 
        surfData.occlusion = 1.0h; // we are already multiplying by the AO here, don't do it at the end like normal
        diffuse *= ao.indirectAmbientOcclusion;
        specular *= ao.indirectAmbientOcclusion;
    }

    //-------------------------------------------------------------------------------------------------
    // Realtime light calculations
    //-------------------------------------------------------------------------------------------------

    // For dynamic objects, this also does specular for probes if there is no main light, assuming the
    // diffuse only contains probe light (it also contains vertex lights, but we'll just ignore that)
    SLZMainLight(diffuse, specular, fragData, surfData, ao.directAmbientOcclusion);

    UNITY_BRANCH if (_ADDITIONAL_LIGHTS)
    {
        uint pixelLightCount = GetAdditionalLightsCount();

        LIGHT_LOOP_BEGIN(pixelLightCount)
            Light light = GetAdditionalLight(lightIndex, fragData.position, fragData.shadowMask);
        SLZAddLight(diffuse, specular, fragData, surfData, light, ao.directAmbientOcclusion);
        LIGHT_LOOP_END
    }



    //-------------------------------------------------------------------------------------------------
    // Image-based specular
    //-------------------------------------------------------------------------------------------------
    real3 reflectionDir = reflect(-fragData.viewDir, fragData.normal);
    real3 SSR = real3(0, 0, 0);
    real SSRLerp = 0;

    float oldVertDepth = ssrExtra.lastClipPos.z / ssrExtra.lastClipPos.w;
    float ddzOld = GetDepthDerivativeSum(oldVertDepth);

    SLZImageBasedSpecularSSR(specular, SSR, SSRLerp, reflectionDir, fragData, surfData, ssrExtra, ao.indirectAmbientOcclusion);
    real horizOcclusion = SLZSpecularHorizonOcclusion(fragData.normal, reflectionDir);
    specular *= horizOcclusion;
    SSR.rgb *= horizOcclusion;
    
    //SSRLerp *= saturate(dot(-fragData.viewDir, ))
    float2 oldScreenUV = SLZComputeNDCFromClip(ssrExtra.lastClipPos);
   
    float oldDepth = LOAD_TEXTURE2D_X(_PrevHiZ0Texture, oldScreenUV.xy * _HiZDim.xy).r;
    
    bool isWithinDepthError = abs(oldDepth - oldVertDepth) < 2 * ddzOld + HALF_MIN;
    //float4 volColor = GetVolumetricColor(fragData.position);
    float3 output = surfData.occlusion * (surfData.albedo * diffuse) + surfData.emission;
    output = surfaceType == 1 ? output * surfData.alpha : output; //Premultiply diffuse by alpha if surface is transparent
    output += surfData.occlusion * specular;
    if (true)//UNITY_BRANCH if (ssrExtra.temporalWeight == 0 || !isWithinDepthError || SSRLerp < 0.0008 || oldScreenUV.x < 0 || oldScreenUV.y < 0 || oldScreenUV.x > 1 || oldScreenUV.y > 1)
    {
        output += surfData.occlusion * SSR.rgb;
    }
    /* Temporal averaging, replaced by across pixel quad-average
    else
    {

        float3 oldColor = SAMPLE_TEXTURE2D_X_LOD(_CameraOpaqueTexture, sampler_TrilinearClamp, UnityStereoTransformScreenSpaceTex(oldScreenUV), 0).rgb;

#if defined(_VOLUMETRICS_ENABLED)
        oldColor = (oldColor - volColor.rgb) / max(volColor.a, 0.0001);
#endif

#if defined(FOG_LINEAR) || defined(FOG_EXP) || defined(FOG_EXP2)
        half4 fogFactors = CalcFogFactors(-fragData.viewDir, ssrExtra.fogFactor);
        oldColor = invertFogLerp(fogFactors.w, fogFactors.rgb, oldColor);
#endif
        oldColor = max(0, oldColor - output);
        float frameTemp = ssrExtra.temporalWeight < 0.5 ? 
            lerp(1.0, _SSRTemporalWeight, ssrExtra.temporalWeight) :
            lerp(_SSRTemporalWeight, 0.0078, ssrExtra.temporalWeight - 1.0);
        SSR = SSR * surfData.occlusion;
        SSR = frameTemp * SSR + (1 - frameTemp) * oldColor;

        output += SSR;
        //output = frameTemp.xxx + 0.0001 * output;
    }
    */
    //-------------------------------------------------------------------------------------------------
    // Combine the final lighting information
    //-------------------------------------------------------------------------------------------------

    //Do fog and volumetrics here to avoid sampling the volumetrics twice

    output = MixFog(output, -fragData.viewDir, ssrExtra.fogFactor);

//#if defined(_VOLUMETRICS_ENABLED)
//    output = volColor.rgb + output * volColor.a;
//#endif
    if (surfaceType == 1)
	{
        surfData.alpha = lerp(surfData.alpha, 1, surfData.reflectivity);
		real fresnelTerm = (1.0h - saturate(fragData.NoV));
		fresnelTerm *= fresnelTerm;
		fresnelTerm *= fresnelTerm;
		surfData.alpha = lerp(surfData.alpha, 1, fresnelTerm);
        surfData.alpha *= horizOcclusion;
	}
	float4 finalColor = float4(output, surfData.alpha);
    finalColor = VolumetricsSurf(finalColor, fragData.position, surfaceType);

    return finalColor;//surfData.occlusion* (surfData.albedo * diffuse + specular) + surfData.emission;
}

#endif