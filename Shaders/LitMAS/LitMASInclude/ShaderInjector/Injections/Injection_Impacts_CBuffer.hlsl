//#!INJECT_BEGIN INCLUDES 0
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/PosespaceImpacts.hlsl"
//#!INJECT_END

//#!INJECT_BEGIN MATERIAL_CBUFFER 100
    int _NumberOfHits;
    half4 _HitColor;
    half4 EllipsoidPosArray[HitMatrixRowCount];
//#!INJECT_END