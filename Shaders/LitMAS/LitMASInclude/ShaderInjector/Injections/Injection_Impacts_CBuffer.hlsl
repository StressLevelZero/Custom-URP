//#!INJECT_BEGIN INCLUDES 0
#include "Packages/com.unity.render-pipelines.universal/Shaders/LitMAS/LitMASInclude/PosespaceImpacts.hlsl"
//#!INJECT_END

//#!INJECT_BEGIN MATERIAL_CBUFFER 0
	half4x4 EllipsoidPosArray[HitArrayCount];
	int _NumberOfHits;
	half4 _HitColor;
//#!INJECT_END