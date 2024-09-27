//#!INJECT_BEGIN INCLUDES 0
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SLZVkExtensions.hlsl"
//#!INJECT_END

//#!INJECT_BEGIN MATERIAL_CBUFFER 0
	uint  _ShadingRate;
//#!INJECT_END

//#!INJECT_BEGIN INTERPOLATORS 10
	uint PrimitiveShadingRate : SV_ShadingRate;
//#!INJECT_END

//#!INJECT_BEGIN VERTEX_END 0
	o.PrimitiveShadingRate = 0u;
//#!INJECT_END