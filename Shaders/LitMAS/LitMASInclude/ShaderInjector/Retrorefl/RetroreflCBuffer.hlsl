/*-----------------------------------------------------------------------------------------------------*
 *-----------------------------------------------------------------------------------------------------*
 * WARNING: THIS FILE WAS CREATED WITH SHADERINJECTOR, AND SHOULD NOT BE EDITED DIRECTLY. MODIFY THE   *
 * BASE INCLUDE AND INJECTED FILES INSTEAD, AND REGENERATE!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!   *
 *-----------------------------------------------------------------------------------------------------*
 *-----------------------------------------------------------------------------------------------------*/



cbuffer UnityPerMaterial 
{
    float4 _BaseMap_ST;
// Begin Injection MATERIAL_CBUFFER_FLOAT_VECTORS from Injection_DetailMap_CBuffer.hlsl ----------------------------------------------------------
    float4 _DetailMap_ST;
    //half4  _DetailScale;
// End Injection MATERIAL_CBUFFER_FLOAT_VECTORS from Injection_DetailMap_CBuffer.hlsl ----------------------------------------------------------
    half4 _BaseColor;
// Begin Injection MATERIAL_CBUFFER_HALF_VECTORS from Injection_Emission_CBuffer.hlsl ----------------------------------------------------------
	half4 _EmissionColor;
// End Injection MATERIAL_CBUFFER_HALF_VECTORS from Injection_Emission_CBuffer.hlsl ----------------------------------------------------------
// Begin Injection MATERIAL_CBUFFER_HALF_VECTORS from Injection_Fluorescence_CBuffer.hlsl ----------------------------------------------------------
    half4 _FluorColor;
    half4 _FluorAbsorbance;
// End Injection MATERIAL_CBUFFER_HALF_VECTORS from Injection_Fluorescence_CBuffer.hlsl ----------------------------------------------------------
    int _Surface;
// Begin Injection MATERIAL_CBUFFER_HALF_SCALARS from Injection_NormalMap_CBuffer.hlsl ----------------------------------------------------------
    half  _Normals;
// End Injection MATERIAL_CBUFFER_HALF_SCALARS from Injection_NormalMap_CBuffer.hlsl ----------------------------------------------------------
// Begin Injection MATERIAL_CBUFFER_HALF_SCALARS from Injection_DetailMap_CBuffer.hlsl ----------------------------------------------------------
    half  _Details;
    half  _DetailNormalScale;
// End Injection MATERIAL_CBUFFER_HALF_SCALARS from Injection_DetailMap_CBuffer.hlsl ----------------------------------------------------------
// Begin Injection MATERIAL_CBUFFER_HALF_SCALARS from Injection_Emission_CBuffer.hlsl ----------------------------------------------------------
	half  _Emission;
	half  _EmissionFalloff;
	half  _BakedMutiplier;
// End Injection MATERIAL_CBUFFER_HALF_SCALARS from Injection_Emission_CBuffer.hlsl ----------------------------------------------------------
// Begin Injection MATERIAL_CBUFFER_HALF_SCALARS from Injection_Cutout_CBuffer.hlsl ----------------------------------------------------------
    half _Cutoff;
// End Injection MATERIAL_CBUFFER_HALF_SCALARS from Injection_Cutout_CBuffer.hlsl ----------------------------------------------------------
// Begin Injection MATERIAL_CBUFFER_HALF_SCALARS from Injection_Fluorescence_CBuffer.hlsl ----------------------------------------------------------
    half  _FluorAlbedoTint;
// End Injection MATERIAL_CBUFFER_HALF_SCALARS from Injection_Fluorescence_CBuffer.hlsl ----------------------------------------------------------
// Begin Injection MATERIAL_CBUFFER_HALF_SCALARS from Injection_Retroreflections_CBuffer.hlsl ----------------------------------------------------------
half  _RetroReflIntensity;
half  _RetroReflSharpness;
// End Injection MATERIAL_CBUFFER_HALF_SCALARS from Injection_Retroreflections_CBuffer.hlsl ----------------------------------------------------------
};