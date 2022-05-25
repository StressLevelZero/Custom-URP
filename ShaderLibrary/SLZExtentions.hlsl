
#ifndef SLZ_LightingExtend
#define SLZ_LightingExtend




#define M_PI  3.1415926535897932384626433832795		//Standard stored Pi.
#define PI_x4 12.566370614359172953850573533118		//For inverse square.
#define PI_R  0.31830988618f                        //Reciprocal 


//Extention Libary to add into pipeline. Should make future package upgrading simpler.


//o.vIndirectSpecular.rgb += BakeryDirectionalLightmapSpecular(vLightmapUV.xy, vNormalWs.xyz, normalize(vPositionWs.xyz - _WorldSpaceCameraPos), 1-vRoughness.x) * o.vIndirectDiffuse.rgb;

// float BakeryDirectionalLightmapSpecular(float2 lmUV, float3 normalWorld, float3 viewDir, float smoothness)
// {
// 	float3 dominantDir = UNITY_SAMPLE_TEX2D_SAMPLER(unity_LightmapInd, unity_Lightmap, lmUV).xyz * 2 - 1;
// 	half3 halfDir = Unity_SafeNormalize(normalize(dominantDir) - viewDir);
// 	half nh = saturate(dot(normalWorld, halfDir));
// 	half perceptualRoughness = SmoothnessToPerceptualRoughness(smoothness);
// 	half roughness = PerceptualRoughnessToRoughness(perceptualRoughness);
// 	half spec = GGXTerm(nh, roughness);
// 	return spec;
// }



//#if defined(_FLUORESCENCE)			
float3 FluorescenceEmission(float4 lightingTerms, float4 Absorbance, float4 Fluorescence ){
//Using alpha ch as UV color
    
// float3 LitFluorescence =  float3(
// 					/*RED*/		max(max(lightingTerms.vDiffuse.r + lightingTerms.vIndirectDiffuse.r , max( lightingTerms.vDiffuse.g + lightingTerms.vIndirectDiffuse.g, lightingTerms.vDiffuse.b + lightingTerms.vIndirectDiffuse.b)), lightingTerms.vDiffuse.a),
// 					/*GREEN*/	max((max(lightingTerms.vDiffuse.g + lightingTerms.vIndirectDiffuse.g, lightingTerms.vDiffuse.b + lightingTerms.vIndirectDiffuse.b)) , lightingTerms.vDiffuse.a),
// 					/*BLUE*/	max(lightingTerms.vDiffuse.b + lightingTerms.vIndirectDiffuse.b , lightingTerms.vDiffuse.a)
// 								) 
// 								* vFluorescence.rgb ;
// o.vColor.rgb = max(o.vColor.rgb, LitFluorescence.rgb);

float4 FluorescenceAbsorb = lightingTerms * Absorbance;					

//Combine each color from high to low frequency to account for dual-excitation
float Absorbed_B = FluorescenceAbsorb.b + FluorescenceAbsorb.a;
float Absorbed_G = Absorbed_B + FluorescenceAbsorb.g;
float Absorbed_R = Absorbed_G + FluorescenceAbsorb.r;

float3 LitFluorescence =  float3(Absorbed_R, Absorbed_G, Absorbed_B) * Fluorescence.rgb ;
return LitFluorescence.rgb;					
}
//#endif

void BlendFluorescence(inout half3 Diffuse, half3 LightColors, BRDFData brdfData )
{
    //Mainly used to shush an implicit casting complier error
    #if defined(_FLUORESCENCE)
    BlendFluorescence(Diffuse,half4(LightColors,0),brdfData);
    #endif
}
void BlendFluorescence(inout half3 Diffuse, half4 LightColors, BRDFData brdfData )
{
    #if defined(_FLUORESCENCE)
    Diffuse = max(Diffuse, FluorescenceEmission(LightColors, brdfData.absorbance ,brdfData.fluorescence ));
    #endif
}


//TEMP port of GGX until SRP replacement is implmented to BakeryDirectionalLightmapSpecular 
float GGXTerm (half3 N, half3 H, half NdotH, half roughness)
{
    half a2 = roughness * roughness;
     //float d = (NdotH * a2 - NdotH) * NdotH + 1.0f; // 2 mad
    half3 NxH = cross(N,H);
    half a = NdotH * roughness;
    half d = a2 + dot(NxH, NxH);
    return PI_R * a2 / (d * d + REAL_MIN); // This function is not intended to be running on Mobile,
                                                // therefore epsilon is smaller than what can be represented by half
}


////Baked Specular using directional baked maps
half BakeryDirectionalLightmapSpecular(float2 lightmapUV, float3 normalWorld, float3 viewDir, float smoothness)
{
	float3 dominantDir = LOAD_TEXTURE2D(unity_LightmapInd, lightmapUV).xyz * 2 - 1;
	half3 halfDir = normalize(normalize(dominantDir) - viewDir);
	half nh = saturate(dot(normalWorld, halfDir));
	half perceptualRoughness = 1-smoothness;
	half roughness = perceptualRoughness * perceptualRoughness;
	half spec = GGXTerm(normalWorld, halfDir, nh, roughness);
	return spec;
 //   return 1;
}
//Baked Specular using directional baked maps
half BakeryDirectionalLightmapSpecular(float4 direction, float3 normalWorld, float3 viewDir, float smoothness)
{
    float3 dominantDir = direction.xyz * 2 - 1;
    half3 halfDir = normalize(normalize(dominantDir) + viewDir);
    half nh = saturate(dot(normalWorld, halfDir));
    half perceptualRoughness = 1 - smoothness;
    half roughness = perceptualRoughness * perceptualRoughness;
    half spec = GGXTerm(normalWorld, halfDir, nh, roughness);
    return spec;
    //   return 1;
}

//uniform half4 GradientFogArray[(int)32.0];
//
//float FogGradient(){
//
//
//}

//half4 FogLinearInterpolation(half ramp)
//{				
//	half refactoredramp = clamp(ramp * 32, 0, 31) ;	
//	half4 interpolated =  lerp(GradientFogArray[refactoredramp],GradientFogArray[refactoredramp+1], frac(refactoredramp) ) ;
//	return interpolated;
//}
//Making a copy from the core to avoid sampling the directional map twice
 real4 SampleDirectionalLightmapSLZ(TEXTURE2D_PARAM(lightmapTex, lightmapSampler), TEXTURE2D_PARAM(lightmapDirTex, lightmapDirSampler), float2 uv, float4 transform, float3 normalWS, float smoothness, float3 viewDirWS, bool encodedLightmap, real4 decodeInstructions)
 {
     // In directional mode Enlighten bakes dominant light direction
     // in a way, that using it for half Lambert and then dividing by a "rebalancing coefficient"
     // gives a result close to plain diffuse response lightmaps, but normalmapped.

     // Note that dir is not unit length on purpose. Its length is "directionality", like
     // for the directional specular lightmaps.

     // transform is scale and bias
     uv = uv * transform.xy + transform.zw;

     real4 direction = SAMPLE_TEXTURE2D(lightmapDirTex, lightmapDirSampler, uv);
     // Remark: baked lightmap is RGBM for now, dynamic lightmap is RGB9E5
     real3 illuminance = real3(0.0, 0.0, 0.0);
     //if (encodedLightmap)
     //{
         real4 encodedIlluminance = SAMPLE_TEXTURE2D(lightmapTex, lightmapSampler, uv).rgba;
         illuminance = encodedLightmap ? DecodeLightmap(encodedIlluminance, decodeInstructions) : encodedIlluminance.rgb;
     //}
     //else
     //{
     //    illuminance = SAMPLE_TEXTURE2D(lightmapTex, lightmapSampler, uv).rgb;
     //}
     real halfLambert = dot(normalWS, direction.xyz - 0.5) + 0.5;
     real3 IndirectDiffuse = illuminance * halfLambert / max(1e-4, direction.w);
     real IndirectSpecular = BakeryDirectionalLightmapSpecular(direction, normalWS, viewDirWS, smoothness) ;
     return real4(IndirectDiffuse.xyz, IndirectSpecular) ;
  //   return 0;
 }

///////////////////////////////////////////////////////////////////////////////
//                          Dithering                                        //
///////////////////////////////////////////////////////////////////////////////

//#define NoisePixels 64
//#define NoiseArraySize 64
//uniform  TEXTURE2D_ARRAY(_SLZ_DitherTex2D);
//TEXTURE2D_ARRAY_PARAM(_SLZ_DitherTex2D, _SLZ_DitherTex2D_sampler);
//TEXTURE2D_ARRAY_ARGS(_SLZ_DitherTex2D, _SLZ_DitherTex2D_sampler)    
//TEXTURE2D_ARRAY(_SLZ_DitherTex2D);       SAMPLER(_SLZ_DitherTex2D_sampler);




// uniform int _SLZ_TexSel;


// float4 DitherTex(float2 UV)
// {
//  return SAMPLE_TEXTURE2D_ARRAY(_SLZ_DitherTex2D, _SLZ_DitherTex2D_sampler, UV.xy, _SLZ_TexSel );
// }

// float4 DitherTex(float2 UV, int FrameOffset)
// {
//     int frame = (_SLZ_TexSel + FrameOffset) ;
//     if (frame > NoiseArraySize ) frame = FrameOffset;
//  return SAMPLE_TEXTURE2D_ARRAY(_SLZ_DitherTex2D, _SLZ_DitherTex2D_sampler, UV.xy, _SLZ_TexSel );
// }


// float2 ScreenSpaceNoiseUVs(float4 projPos){

//  //	ComputeScreenPos(vertex_out);
//  //   projPos.z = -UnityObjectToViewPos(vertex_in).z

//  return	( (projPos.xy / projPos.w) *_ScreenParams.xy /NoisePixels).xy ;

//  }

//  float4 DepthFade_VS_ComputeProjPos(float4 vertex_in, float4 vertex_out)
// {
//     float4 projPos = ComputeScreenPos(vertex_out);
//     //projPos.z = -UnityObjectToViewPos(vertex_in).z; // = COMPUTE_EYEDEPTH
//     return projPos;
// }


#endif