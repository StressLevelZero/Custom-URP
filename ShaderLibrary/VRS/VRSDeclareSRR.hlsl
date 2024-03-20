/*****************************************************************************
*                                                                            *
* Parameters and Shading Rate Texture for Nvidia API Variable Rate Shading	 *
*                                                                            *
******************************************************************************/

#if !defined(SLZ_VRS_NVAPI)
#define(SLZ_VRS_NVAPI)

TEXTURE2D_X(<uint> _SRRTexture);

// Radii of the crossing ellipsoids that define the shading rate
float4 _VRSRadii;
#define vrsOuterRadiusU _VRSRadii.x
#define vrsOuterRadiusV _VRSRadii.y
#define vrsInnerRadiusU _VRSRadii.z
#define vrsInnerRadiusV _VRSRadii.w

#define vrsOuterRadiusMinor _VRSRadii.x
#define vrsOuterRadiusMajor _VRSRadii.y
#define vrsInnerRadiusMinor _VRSRadii.z
#define vrsInnerRadiusMajor _VRSRadii.w

float4 _SRImageTileSize; // Tile width, tile height, 1/tile width, 1/tile height

// Each pair of numbers represents a shading rate width, height for a given
// integer value from the shading rate lookup table. Packed into float4's
// so index 0 contains the values for 0 and 1, 1 contains 2 and 3, and so on
CBUFFER_START(VRS_SRR_LUT)
	float4 shadingRate[2]; 
CBUFFER_END
// MSAA limits the maximum shading rate, index of the max shading rate found in the last value of shadingRate
#define _MaxShadingRate shadingRate[1].w

uint GetShadingIndex(uint2 screenCoords)
{
	return LOAD_TEXTURE2D_X_LOD(_SRRTexture, screenCoords, 0u).r;
}

float2 ShadingRateFromIndex(uint shadingRateIndex)
{
	// Translate NVAPI table rates to vulkan bit-packed shading attachment rates. The NVAPI table was ordered such that the 
	// integer value of the bit-packed attachment rate was the index of that rate in the NVAPI table. However, the largest 
	// value possible is 10, and despite nvidia and vulkan claiming that the table can be as large as 16 it can only actually 
	// have 8 entries. Therefore, I compacted the table, getting rid of gaps to bring the total number of entries to 7. 
	// Thus the NVAPI table indices from the shading rate texture have to be shifted to get the correct attachment rate value 
	// which can then be unpacked to get the x and y shading tile size
#if !defined(VULKAN_ATTACHMENT_RATE)
	shadingRateIndex = shadingRateIndex > 4 ? shadingRateIndex + 2 : shadingRateIndex;
	shadingRateIndex = shadingRateIndex > 1 ? shadingRateIndex + 2 : shadingRateIndex;
#endif
	return float2(1<<((shadingRateIndex >> 2) & 3), 1 << (shadingRateIndex & 3));
}

uint PackedShadingRateToIndex(uint shadingRate)
{
	// Translate NVAPI table rates to vulkan bit-packed shading attachment rates. The NVAPI table was ordered such that the 
	// integer value of the bit-packed attachment rate was the index of that rate in the NVAPI table. However, the largest 
	// value possible is 10, and despite nvidia and vulkan claiming that the table can be as large as 16 it can only actually 
	// have 8 entries. Therefore, I compacted the table, getting rid of gaps to bring the total number of entries to 7. 
	// Thus the NVAPI table indices from the shading rate texture have to be shifted to get the correct attachment rate value 
	// which can then be unpacked to get the x and y shading tile size
#if !defined(VULKAN_ATTACHMENT_RATE)
	shadingRate = shadingRate > 6 ? shadingRate - 2 : shadingRate;
	shadingRate = shadingRate > 1 ? shadingRate - 2 : shadingRate;
#endif
	return shadingRate;
}

uint ShadingRateIndexToPacked(uint shadingRateIndex)
{
	// Translate NVAPI table rates to vulkan bit-packed shading attachment rates. The NVAPI table was ordered such that the 
	// integer value of the bit-packed attachment rate was the index of that rate in the NVAPI table. However, the largest 
	// value possible is 10, and despite nvidia and vulkan claiming that the table can be as large as 16 it can only actually 
	// have 8 entries. Therefore, I compacted the table, getting rid of gaps to bring the total number of entries to 7. 
	// Thus the NVAPI table indices from the shading rate texture have to be shifted to get the correct attachment rate value 
	// which can then be unpacked to get the x and y shading tile size
#if !defined(VULKAN_ATTACHMENT_RATE)
	shadingRateIndex = shadingRateIndex > 4 ? shadingRateIndex + 2 : shadingRateIndex;
	shadingRateIndex = shadingRateIndex > 1 ? shadingRateIndex + 2 : shadingRateIndex;
#endif
	return shadingRateIndex;
}



float2 GetShadingRateFromCoords(uint2 screenCoords)
{
	uint shadingRateIndex = GetShadingIndex(screenCoords);

	return ShadingRateFromIndex(shadingRateIndex);
}

float2 GetShadingRateNormalizedUV(float2 normalizedScreenUVs)
{
	uint2 screenCoords = normalizedScreenUVs * (_ScaledScreenParams.xy * _SRImageTileSize.zw);
	return GetShadingRateFromCoords(screenCoords);
}

uint GetShadingIndexNormalizedUV(float2 normalizedScreenUVs)
{
	uint2 screenCoords = normalizedScreenUVs * (_ScaledScreenParams.xy * _SRImageTileSize.zw);
	return GetShadingIndex(screenCoords);
}

#endif