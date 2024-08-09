#pragma once

#include_with_pragmas "Packages/com.stresslevelzero.urpconfig/include/DXCUpdateState.hlsl"

#if defined(SLZ_DXC_UPDATED) || defined(SHADER_API_DESKTOP)
#pragma use_dxc vulkan
#endif