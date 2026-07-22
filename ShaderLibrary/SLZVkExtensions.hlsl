#if !defined(SLZ_VK_EXTENSIONS)
#define SLZ_VK_EXTENSIONS

#include "Packages/com.stresslevelzero.urpconfig/include/DXCUpdateState.hlsl"

// static global for the fragment size that can be read from anywhere, similar to unity_StereoEyeIndex
static uint2 SLZ_FragSize = uint2(1,1);

// fallback method to figure out the shading rate from the derivatives of the screen coordinates
uint2 FallbackGetFragSize(float2 screenCoords)
{
	float2 dCoords = float2(ddx(screenCoords.x), ddy(screenCoords.y));
	return uint2(round(dCoords.x), round(dCoords.y));
}

#if defined(SHADER_API_VULKAN) && defined(UNITY_COMPILER_DXC) && defined(SLZ_DXC_UPDATED)
    #define SLZ_VK_EXT_ENABLED

    // Fragment Invocation Density - Gives the width/height in pixels that each fragment covers with Qualcomm's fragment density map and NVidia's variable rate shading. Also works with the Khronos fragment shading rate extension on nvidia hardware
    // However, we can't use it on PC since declaring the extension will prevent the shader from running on non-nvidia gpu's
    #if defined(SHADER_API_MOBILE)

        // Attributes for RequestFragmentDensityEXT()
        #define SLZ_REQUEST_FRAG_SIZE_CAPS  [[vk::ext_extension("SPV_EXT_fragment_invocation_density")]] \
                                            [[vk::ext_capability(/*FragmentDensityEXT*/ 5291)]]

        // Read only, only valid in the fragment stage. This cannot go into the interpolator struct output by the vert function.
        // This should be added as an extra parameter to the frag program as the last parameter. Note the leading comma!
        // HLSL doesn't allow trailing commas in parameter lists, so if SLZ_DECLARE_FRAG_SIZE is defined to be empty the comma separating it needs to disappear too
        #define SLZ_DECLARE_FRAG_SIZE     , [[vk::ext_decorate(/*Builtin*/ 11, /*FragSizeEXT*/ 5292)]] uint2 FragSizeEXT : FRAGSIZE

        #define SLZ_SETUP_FRAG_SIZE(screenCoords) RequestFragmentDensityEXT(); SLZ_FragSize = FragSizeEXT;

    #else
        // Cross platform VK_KHR_fragment_shading_rate not really possible in 2022.
        // This extension requires that the shading rate image be declared up front in the framebuffer and renderpass creation structs. 
        // While it is possible to hook the functions for creating those objects, it is impossible to tell if they should be modified 
        // to contain the shading rate attachment and data. Since vulkan obfusactes the contents of all objects, there is not enough 
        // context to figure out if it is appropriate just from the non-object parameters of the creation struct. Furthermore, unity 
        // caches and reuses renderpass objects for passes with identical parameters. If we modify a renderpass, unity will be unaware 
        // and can try to use it with the a framebuffer that does not contain the shading rate attachment which will result in a crash.

        // Instead, we are temporarily using the nvidia VK_NV_shading_rate_image extension, which predated VK_KHR_fragment_shading_rate and was
        // modeled after their D3D11 extension. As such, it does not conform to vulkan's conventions and instead declares the shading rate image
        // as a global outside the framebuffer and renderpass, and enabling/disabling it can be achieved with a dynamic state command. 

        // #define SLZ_REQUEST_FRAG_SIZE_CAPS   [[vk::ext_extension("SPV_KHR_fragment_shading_rate")]] \
        //                                      [[vk::ext_capability(/*FragmentShadingRateKHR*/ 4422)]]
        // #define SLZ_DECLARE_FRAG_SIZE     , [[vk::ext_decorate(/*Builtin*/ 11, /*ShadingRateKHR*/ 4444)]] uint ShadingRateKHR : FRAGSIZE
    	// #define SLZ_SETUP_FRAG_SIZE(screenCoords) VkSPIRV::s_FragSizeExt = min16uint2(1 << ((ShadingRateKHR >> 2) & 3), 1 << (ShadingRateKHR & 3));

        // SPV_EXT_fragment_invocation_density works with both VK_NV_shading_rate_image and VK_EXT_fragment_density_map (used by quest), but declaring the extension will cause other PC gpu vendors
        // to skip executing the shader entirely rather than just returning a sane default. Fall back to derivatives on the vertex screen coordinates to determine shading rate
        #define SLZ_REQUEST_FRAG_SIZE_CAPS
        #define SLZ_DECLARE_FRAG_SIZE
        #define SLZ_SETUP_FRAG_SIZE(screenCoords) RequestFragmentDensityEXT(); SLZ_FragSize = FallbackGetFragSize(screenCoords);
    #endif
    
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
    #define SLZ_INITIALIZE_FRAG_SIZE(screenCoords) SLZ_FragSize = FallbackGetFragSize(screenCoords);
    #define SLZ_FRAG_SIZE SLZ_FragSize
    
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