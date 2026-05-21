/*-----------------------------------------------------------------------------------------------------*
 *-----------------------------------------------------------------------------------------------------*
 * WARNING: THIS FILE WAS CREATED WITH SHADERINJECTOR, AND SHOULD NOT BE EDITED DIRECTLY. MODIFY THE   *
 * BASE INCLUDE AND INJECTED FILES INSTEAD, AND REGENERATE!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!   *
 *-----------------------------------------------------------------------------------------------------*
 *-----------------------------------------------------------------------------------------------------*/



cbuffer UnityPerMaterial 
{
    float4 _BaseMap_ST;
// Begin Injection MATERIAL_CBUFFER_FLOAT_VECTORS from Injection_Layered.hlsl ----------------------------------------------------------
float4 _LightmapScaleOffset;
float4 _BaseMap1_ST;
float4 _BaseMap2_ST;
float4 _BaseMap3_ST;
float4 _BaseMap4_ST;
// End Injection MATERIAL_CBUFFER_FLOAT_VECTORS from Injection_Layered.hlsl ----------------------------------------------------------
    half4 _BaseColor;
    int _Surface;
// Begin Injection MATERIAL_CBUFFER_HALF_SCALARS from Injection_NormalMap_CBuffer.hlsl ----------------------------------------------------------
    half  _Normals;
// End Injection MATERIAL_CBUFFER_HALF_SCALARS from Injection_NormalMap_CBuffer.hlsl ----------------------------------------------------------
// Begin Injection MATERIAL_CBUFFER_HALF_SCALARS from Injection_Cutout_CBuffer.hlsl ----------------------------------------------------------
    half _Cutoff;
// End Injection MATERIAL_CBUFFER_HALF_SCALARS from Injection_Cutout_CBuffer.hlsl ----------------------------------------------------------
// Begin Injection MATERIAL_CBUFFER_HALF_SCALARS from Injection_Layered.hlsl ----------------------------------------------------------
half _UseGRID;
// End Injection MATERIAL_CBUFFER_HALF_SCALARS from Injection_Layered.hlsl ----------------------------------------------------------
};