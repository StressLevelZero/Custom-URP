/*-----------------------------------------------------------------------------------------------------*
 *-----------------------------------------------------------------------------------------------------*
 * WARNING: THIS FILE WAS CREATED WITH SHADERINJECTOR, AND SHOULD NOT BE EDITED DIRECTLY. MODIFY THE   *
 * BASE INCLUDE AND INJECTED FILES INSTEAD, AND REGENERATE!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!   *
 *-----------------------------------------------------------------------------------------------------*
 *-----------------------------------------------------------------------------------------------------*/



cbuffer UnityPerMaterial 
{
    float4 _BaseMap_ST;
    half4 _BaseColor;
    int _Surface;
// Begin Injection MATERIAL_CBUFFER_HALF_SCALARS from Injection_NormalMap_CBuffer.hlsl ----------------------------------------------------------
    half  _Normals;
// End Injection MATERIAL_CBUFFER_HALF_SCALARS from Injection_NormalMap_CBuffer.hlsl ----------------------------------------------------------
// Begin Injection MATERIAL_CBUFFER_HALF_SCALARS from Injection_Anisotropic.hlsl ----------------------------------------------------------
	half _AnisoAspect;
// End Injection MATERIAL_CBUFFER_HALF_SCALARS from Injection_Anisotropic.hlsl ----------------------------------------------------------
};