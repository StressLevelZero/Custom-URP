/* 
 * Stress Level Zero Lighting functions
 */   

#ifndef SLZ_PBR_LIGHTING_SSR
#define SLZ_PBR_LIGHTING_SSR

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SLZLighting.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SSR.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SSRGlobals.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/SpaceFillingCurves.hlsl"


#if !defined(DYNAMIC_ADDITIONAL_LIGHTS) && !defined(_ADDITIONAL_LIGHTS)
#define _ADDITIONAL_LIGHTS false
#endif


half4 CalcFogFactors(half3 viewDirectionWS, half fogFactor)
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

//half SLZSpecularHorizonOcclusion(half3 normal, half3 reflectionDir)
//{
//    half horizonOcclusion = min(1.0h + dot(reflectionDir, normal), 1.0h);
//    return horizonOcclusion * horizonOcclusion;
//}

struct SSRExtraData
{
    half3 meshNormal; 
    float4 lastClipPos;
    float temporalWeight;
    float depthDerivativeSum;
    half4 noise;
    half fogFactor;
	half2 roughnessRange;
};

half2 IGNVectorMorton2D(float2 pixCoord, int frameCount)
{
	const float3 magic = float3(0.06711056f, 0.00583715f, 52.9829189f);
	float2 frameMagicScale = float2(2.083f, 4.867f);
	pixCoord += frameCount * frameMagicScale;
	float noise1D = frac(magic.z * frac(dot(pixCoord, magic.xy)));
    
	uint noiseMortonCode = uint(round(noise1D * float(1u << 32)));
        //FloatToUFixed(noise1D, 32);
	float2 noise2d = float2(DecodeMorton2D(noiseMortonCode)) / (65535.0);

	return half2(noise2d);
}


half4 IGNVectorOffset(float2 pixCoord, int frameCount)
{
    const float3 magic = float3(0.06711056f, 0.00583715f, 52.9829189f);
    float2 frameMagicScale = float2(2.083f, 4.867f);
	pixCoord += frameCount * frameMagicScale;
    return half4(
        frac(magic.z * frac(dot(pixCoord, magic.xy))),
        frac(magic.z * frac(dot(pixCoord + float2(2, -1), magic.xy))),
        frac(magic.z * frac(dot(pixCoord.yx + float2(3, 1), magic.xy))),
        frac(magic.z * frac(dot(pixCoord + float2(-2, 2), magic.xy)))
    );
}

half4 SSRGetInterleavedGradientNoise(float2 pixCoord, int frameCount)
{
    pixCoord /= (float2)SLZ_FRAG_SIZE; 
	frameCount = unity_DeltaTime.w > half(59.0) ? (frameCount & 1) : 0;
    #if 1
	return IGNVectorOffset(pixCoord, frameCount);
    #else
    return half4(IGNVectorMorton2D(pixCoord, frameCount),0,0);
    #endif    
}

 /**
  * Specular from reflection probes and SSR split, so we can reverse the non-ssr color later
  *
  *
  * @param[in,out] specular  Running total of the specular color
  * @param         fragData  Struct containing all relevant fragment data (normal, position, etc)
  * @param         surfData  Struct containing physical properties of the surface (specular color, roughness, etc)
  * @param         indSSAO   Indirect screenspace ambient occlusion, not used if SSAO isn't enabled
  */
void SLZImageBasedSpecularSSR(half3 diffuse, inout half3 specular, half2 SSRRoughnessRange, half3 reflectionDir, const SLZFragData fragData, const SLZSurfData surfData, SSRExtraData ssrExtra, half indSSAO, int surfaceType = 0)
{
    
    //half3 LitSpecularOcclusion = (1,1,1);//BakedLightingToSpecularOcclusion(diffuse);
    half AOSpecularOcclusion = GetSpecularOcclusionFromAmbientOcclusion(fragData.NoV, surfData.occlusion, surfData.roughness);
	half3 reflectionProbe = GlossyEnvironmentReflection(reflectionDir, fragData.position, surfData.perceptualRoughness, AOSpecularOcclusion, fragData.screenUV);// * LitSpecularOcclusion;

    
   
#if !defined(SHADER_API_MOBILE)
    //ssrData.perceptualRoughness = -fresnelTerm * ssrData.perceptualRoughness + ssrData.perceptualRoughness;
   
    half RdotV = saturate(0.95 * dot(reflectionDir, -fragData.viewDir.xyz) + 0.05);


    SSRData ssrData = GetSSRData(
        fragData.position,
        fragData.screenUV,
        fragData.viewDir,
        reflectionDir,
        ssrExtra.meshNormal,
        surfData.perceptualRoughness,
        RdotV,
        ssrExtra.depthDerivativeSum,
        ssrExtra.noise,
        surfaceType > 0
    );

	half SSRLerp;
	//SSRLerp = 1.0 - surfData.perceptualRoughness;
	//SSRLerp *= SSRLerp;
	//SSRLerp *= SSRLerp;
	//SSRLerp = saturate((surfData.perceptualRoughness - SSRRoughnessRange.y) / (SSRRoughnessRange.x - SSRRoughnessRange.y));
    //SSRLerp = sqrt(SSRLerp);
	SSRLerp = smoothstep(SSRRoughnessRange.y, SSRRoughnessRange.x, surfData.perceptualRoughness);

    //Piecewise function to make a sinusoidal falloff curve
#define SSR_FALLOFF_START 0.6666667
    RdotV = 2 * saturate( (1 / SSR_FALLOFF_START) * RdotV);
    RdotV = RdotV > 1 ? -0.5*(RdotV * RdotV) + (2*RdotV - 1) : 0.5 * RdotV * RdotV;
	SSRLerp *= RdotV;
    half4 SSR = half4(0, 0, 0, 0);
    bool doSSR = SSRLerp > 0.008;
    
    #if defined(_SM6_WAVE_VOTE)
    if (WaveActiveAnyTrue(doSSR))
    #endif
    if (doSSR)
    {
        SSR = getSSRColor(ssrData);
		SSR = clamp(SSR, 0, 100);
	}
    


    //reflectionProbe = lerp(reflectionProbe, SSRColor.rgb, SSRColor.a * SSRLerp);
    half3 SSRColor = SSR.rgb * AOSpecularOcclusion;
    reflectionProbe *= (1.0 - SSR.a * SSRLerp);
    SSRColor *= SSR.a * SSRLerp;
	reflectionProbe += SSRColor;
#endif
    
    half surfaceReduction = 1.0h / (surfData.roughness * surfData.roughness + 1.0h);
    half3 grazingTerm = saturate((1.0h - surfData.perceptualRoughness) + surfData.reflectivity);
    half fresnelTerm = (1.0h - saturate(fragData.NoV));
    fresnelTerm *= fresnelTerm;
    fresnelTerm *= fresnelTerm; // fresnelTerm ^ 4
    half3 IBSpec = half3(surfaceReduction * lerp(surfData.specular, grazingTerm, fresnelTerm));

    reflectionProbe *= IBSpec;
    //SSRColor.rgb *= IBSpec;
    
	half bakedOcclusion = BakedLightingToSpecularOcclusionGray(diffuse);
	reflectionProbe *= bakedOcclusion;
	//SSRColor.rgb *= bakedOcclusion;
    
    UNITY_BRANCH if (_SCREEN_SPACE_OCCLUSION)
    {
        reflectionProbe *= indSSAO;
        SSRColor.rgb *= indSSAO;
    }

    specular += reflectionProbe;
}



half4 SLZPBRFragmentSSR(SLZFragData fragData, SLZSurfData surfData, SSRExtraData ssrExtra, int surfaceType = 0)
{
    diffuseLight diffuse = (diffuseLight)0;
    half3 specular = (half3)0;

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

#endif

    diffuse.rgb += fragData.vertexLighting; //contains both vertex lights and L2 coefficient of SH on mobile

    //Apply SSAO to "indirect" sources (not halfly indirect, but that's what unity calls baked and image based lighting)
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
    // halftime light calculations
    //-------------------------------------------------------------------------------------------------
    
    // For dynamic objects, this also does specular for probes if there is no main light, assuming the
    // diffuse only contains probe light (it also contains vertex lights, but we'll just ignore that)
    SLZMainLight(diffuse, specular, fragData, surfData, ao.directAmbientOcclusion);

    [branch]
    if (BRANCH_ADDITIONAL_LIGHTS)
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



    //-------------------------------------------------------------------------------------------------
    // Image-based specular
    //-------------------------------------------------------------------------------------------------
    half3 reflectionDir = reflect(-fragData.viewDir, fragData.normal);

	SLZImageBasedSpecularSSR(diffuse, specular, ssrExtra.roughnessRange, reflectionDir, fragData, surfData, ssrExtra, ao.indirectAmbientOcclusion, surfaceType);
    half horizOcclusion = SLZSpecularHorizonOcclusion(fragData.normal, reflectionDir);
    specular *= horizOcclusion;

    float3 output = surfData.occlusion * (surfData.albedo * diffuse.rgb) + surfData.emission;
    #if defined(_FLUORESCENCE)
        BlendFluorescence(output, diffuse, surfData.absorbance, surfData.fluorescence);
    #endif
    output = surfaceType == 1 ? output * surfData.alpha : output; //Premultiply diffuse by alpha if surface is transparent
    output += surfData.occlusion * specular;


    //-------------------------------------------------------------------------------------------------
    // Combine the final lighting information
    //-------------------------------------------------------------------------------------------------

    //Do fog and volumetrics here to avoid sampling the volumetrics twice
    
    if (surfaceType == 1)
    {
        surfData.alpha = lerp(surfData.alpha, 1, surfData.reflectivity);
        half fresnelTerm = (1.0h - saturate(fragData.NoV));
        fresnelTerm *= fresnelTerm;
        fresnelTerm *= fresnelTerm;
        surfData.alpha = lerp(surfData.alpha, 1, fresnelTerm);
        surfData.alpha *= horizOcclusion;
    }
    half4 finalColor = half4(output, surfData.alpha);
    finalColor = MixFogSurf(finalColor, -fragData.viewDir, ssrExtra.fogFactor, surfaceType);
    finalColor = VolumetricsSurf(finalColor, fragData.position, surfaceType);

    return finalColor;//surfData.occlusion* (surfData.albedo * diffuse + specular) + surfData.emission;
}

#endif