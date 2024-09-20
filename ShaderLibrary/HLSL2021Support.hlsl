#if !defined(SLZ_HLSL2021)
#define SLZ_HLSL2021

#include "Packages/com.stresslevelzero.urpconfig/include/DXCUpdateState.hlsl"

// Define 'select' function introduced into later versions of DXC
// Ternary operations on vectors are no longer legal, select should be used instead
#if !defined(SLZ_DXC_UPDATED) || !defined(UNITY_COMPILER_DXC) || (SLZ_DXC_VERSION_MAJOR <= 1 && SLZ_DXC_VERSION_MINOR <= 7)
	#define select(a, b, c) ((a) ? (b) : (c))
#endif

#endif // SLZ_HLSL2021