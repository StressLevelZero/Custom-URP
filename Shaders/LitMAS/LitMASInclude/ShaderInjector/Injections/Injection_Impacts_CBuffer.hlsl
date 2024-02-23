//#!INJECT_BEGIN INCLUDES 0
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/PosespaceImpacts.hlsl"
//#!INJECT_END

//#!INJECT_BEGIN MATERIAL_CBUFFER_EARLY 0
	half4x4 EllipsoidPosArray[HitMatrixCount];
	int _NumberOfHits;
	half4 _HitColor;
//#!INJECT_END