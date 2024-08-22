#ifndef SLZ_PLATFORM_COMPILER
#define SLZ_PLATFORM_COMPILER

#include "Packages/com.stresslevelzero.urpconfig/include/DXCUpdateState.hlsl"

#if defined(SLZ_DXC_UPDATED) || defined(SHADER_API_DESKTOP)
#define FORCE_NEW_ARTIFACTS 1
#pragma use_dxc vulkan
#endif

#endif