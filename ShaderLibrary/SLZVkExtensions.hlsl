#if !defined(SLZ_VK_EXTENSIONS)
#define SLZ_VK_EXTENSIONS

#include "Packages/com.stresslevelzero.urpconfig/include/DXCUpdateState.hlsl"

#if defined(SHADER_API_VULKAN) && defined(UNITY_COMPILER_DXC) && defined(SLZ_DXC_UPDATED)
    #define SLZ_VK_EXT_ENABLED

    // Fragment Invocation Density - combines qualcomm's per-tile fragment density and KHR fragment shading rate. Also works with NV shading rate 
    // Only valid for the fragment program. Note that attaching this to the fragment does not work with the default Unity 6 version of DXC, use RequestFragmentDensityEXT() instead
    #define SLZ_REQUEST_FRAG_SIZE_CAPS  [[vk::ext_extension("SPV_EXT_fragment_invocation_density")]] \
                                        [[vk::ext_capability(/*FragmentDensityEXT*/ 5291)]]

    // Read only, only valid in the fragment stage. This cannot go into the interpolator struct output by the vert function.
    // This should be added as an extra parameter to the frag program as the last parameter. Note the leading comma!
    // HLSL doesn't allow trailing commas in parameter lists, so if SLZ_DECLARE_FRAG_SIZE is defined to be empty the comma separating it needs to disappear too
    #define SLZ_DECLARE_FRAG_SIZE     , [[vk::ext_decorate(/*Builtin*/ 11, /*FragSizeEXT*/ 5292)]] uint2 FragSizeEXT : FRAGSIZE

    // static global for the fragment size that can be read from anywhere, similar to unity_StereoEyeIndex
    static uint2 SLZ_FragSize = uint2(1,1);

    // call this at the beginning of the fragment
    #define SLZ_SETUP_FRAG_SIZE SLZ_FragSize = FragSizeEXT;
    #define SLZ_FRAG_SIZE SLZ_FragSize
    
    // Write only, only valid in vertex and geo stages.
    #define SLZ_OUT_PRIMITIVE_SHADING_RATE , out uint PrimitiveShadingRate : SV_ShadingRate
    #define SLZ_SET_PRIMITIVE_SHADING_RATE(value) PrimitiveShadingRate = value;
    
    // Combined image sampler - unity can't figure out how to bind this, don't use.
    #define DECLARE_COMBINED_SAMPLER(_register_) [[vk::combinedImageSampler]][[vk::binding(_register_)]]
    
#else
    // Fragment Invocation Density
    #define SLZ_REQUEST_FRAG_SIZE_CAPS
    #define SLZ_DECLARE_FRAG_SIZE
    #define SLZ_INITIALIZE_FRAG_SIZE
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

// Unlike DXC 1.8, DXC 1.7 (used by Unity 6) only allows extension/capability attributes to be attached to special functions. This does not allow directly adding them to the vert or frag programs with the default compiler.
// However, functions overridden with a SPIR-V opcode are allowed to have these attributes, and there happens to be an opcode (OpNop) which by definition does nothing and is expected to be optimized out.
// Simply calling this function from anywhere within a shader program will instruct DXC to add the capability and extension to the program.
#if defined(SLZ_VK_EXT_ENABLED)
SLZ_REQUEST_FRAG_SIZE_CAPS
[[vk::ext_instruction(/*OpNop*/ 0)]]
#endif
void RequestFragmentDensityEXT() { }

// https://registry.khronos.org/SPIR-V/specs/unified1/SPIRV.html#Scope_-id-
// Only device (0) and subgroup (3) are valid for fragment shader invocations
#if defined(SLZ_VK_EXT_ENABLED)
[[vk::ext_extension("SPV_KHR_shader_clock")]]
[[vk::ext_capability(/*ShaderClockKHR*/ 5055)]]
[[vk::ext_instruction(/*OpReadClockKHR*/ 5056)]]
#endif
uint2 ReadClock(uint scope = 3) { return (uint2)0; }

// https://developer.nvidia.com/blog/profiling-dxr-shaders-with-timer-instrumentation/
uint DeltaShaderClockTime(uint start, uint end)
{
  return end < start ? (~0u - (start - end)) : (end - start);
}

#endif // SLZ_VK_EXTENSIONS