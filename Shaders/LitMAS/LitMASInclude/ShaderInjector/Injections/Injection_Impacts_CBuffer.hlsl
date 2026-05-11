//#!INJECT_BEGIN INCLUDES 0
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/PosespaceImpacts.hlsl"
//#!INJECT_END

//#!INJECT_BEGIN MATERIAL_CBUFFER_EARLY 0
	half4x4 EllipsoidPosArray[HitMatrixCount];
//#!INJECT_END

//#!INJECT_BEGIN MATERIAL_CBUFFER_HALF_VECTORS 0
	half4 _HitColor;
	half4 _SSSColor;
//#!INJECT_END

//#!INJECT_BEGIN MATERIAL_CBUFFER_FLOAT_SCALARS 0
	int _NumberOfHits;
//#!INJECT_END