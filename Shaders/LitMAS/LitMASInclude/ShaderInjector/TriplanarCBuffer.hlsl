/*-----------------------------------------------------------------------------------------------------*
 *-----------------------------------------------------------------------------------------------------*
 * WARNING: THIS FILE WAS CREATED WITH SHADERINJECTOR, AND SHOULD NOT BE EDITED DIRECTLY. MODIFY THE   *
 * BASE INCLUDE AND INJECTED FILES INSTEAD, AND REGENERATE!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!   *
 *-----------------------------------------------------------------------------------------------------*
 *-----------------------------------------------------------------------------------------------------*/



cbuffer UnityPerMaterial 
{
    float4 _BaseMap_ST;
// Begin Injection MATERIAL_CBUFFER_FLOAT_VECTORS from Injection_Triplanar_CBuffer.hlsl ----------------------------------------------------------
    float4 _DetailMap_ST;
// End Injection MATERIAL_CBUFFER_FLOAT_VECTORS from Injection_Triplanar_CBuffer.hlsl ----------------------------------------------------------
// Begin Injection MATERIAL_CBUFFER_FLOAT_VECTORS from Injection_SSR_CBuffer.hlsl ----------------------------------------------------------
	float4 _SSRSmoothnessRange;
// End Injection MATERIAL_CBUFFER_FLOAT_VECTORS from Injection_SSR_CBuffer.hlsl ----------------------------------------------------------
    half4 _BaseColor;
// Begin Injection MATERIAL_CBUFFER_HALF_VECTORS from Injection_Emission_CBuffer.hlsl ----------------------------------------------------------
	half4 _EmissionColor;
// End Injection MATERIAL_CBUFFER_HALF_VECTORS from Injection_Emission_CBuffer.hlsl ----------------------------------------------------------
    int _Surface;
// Begin Injection MATERIAL_CBUFFER_HALF_SCALARS from Injection_Triplanar_CBuffer.hlsl ----------------------------------------------------------
    half  _Details;
    half  _Normals;
    half  _DetailsuseLocalUVs;
    half _RotateUVs;
    half _UVScaler;
// End Injection MATERIAL_CBUFFER_HALF_SCALARS from Injection_Triplanar_CBuffer.hlsl ----------------------------------------------------------
// Begin Injection MATERIAL_CBUFFER_HALF_SCALARS from Injection_Emission_CBuffer.hlsl ----------------------------------------------------------
	half  _Emission;
	half  _EmissionFalloff;
	half  _BakedMutiplier;
// End Injection MATERIAL_CBUFFER_HALF_SCALARS from Injection_Emission_CBuffer.hlsl ----------------------------------------------------------
};