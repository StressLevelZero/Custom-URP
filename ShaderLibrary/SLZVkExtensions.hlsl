#if !defined(SLZ_VK_EXTENSIONS)
#define SLZ_VK_EXTENSIONS

#include "Packages/com.stresslevelzero.urpconfig/include/DXCUpdateState.hlsl"

#if defined(SHADER_API_VULKAN) && defined(UNITY_COMPILER_DXC) && defined(SLZ_DXC_UPDATED)
    #define SLZ_VK_EXT_ENABLED

    // Fragment Invocation Density - combines qualcomm's per-tile fragment density and KHR fragment shading rate. Also works with NV shading rate 
    // Only valid for the fragment program
    #define SLZ_REQUEST_FRAG_SIZE_CAPS  [[vk::ext_extension("SPV_EXT_fragment_invocation_density")]] \
                                        [[vk::ext_capability(/*FragmentDensityEXT*/ 5291)]]
    //Read only, only valid in the fragment stage
    #define SLZ_DECLARE_FRAG_SIZE     , [[vk::ext_decorate(/*Builtin*/ 11, /*FragSizeEXT*/ 5292)]] uint2 FragSizeEXT : FRAGSIZE
    #define SLZ_FRAG_SIZE FragSizeEXT
    
    // Write only, only valid in vertex and geo stages.
    #define SLZ_OUT_PRIMITIVE_SHADING_RATE , out uint PrimitiveShadingRate : SV_ShadingRate
    #define SLZ_SET_PRIMITIVE_SHADING_RATE(value) PrimitiveShadingRate = value;
    
    // Combined image sampler - unity can't figure out how to bind this.
    #define DECLARE_COMBINED_SAMPLER(_register_) [[vk::combinedImageSampler]][[vk::binding(_register_)]]
#else
    // Fragment Invocation Density
    #define SLZ_REQUEST_FRAG_SIZE_CAPS
    #define SLZ_DECLARE_FRAG_SIZE
    #define SLZ_FRAG_SIZE uint2(1,1)
    
    // Fragment Shading Rate
    #define SLZ_OUT_PRIMITIVE_SHADING_RATE
    #define SLZ_SET_PRIMITIVE_SHADING_RATE(value)
    
    // Combined image sampler
    #define DECLARE_COMBINED_SAMPLER(_register_)
#endif

#if defined(SLZ_VK_EXT_ENABLED)
[[vk::ext_instruction(/*OpConstantTrue*/ 41)]]
#endif
bool InlineSPIRVEnabled() { return false; }

#endif // SLZ_VK_EXTENSIONS