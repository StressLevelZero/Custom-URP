#if !defined(SLZ_FRACTAL)
#define SLZ_FRACTAL


// Returns something proportional to "how minified is this texture lookup right now?"
// Uses texel-space derivatives so it automatically adapts to UV tiling + object scale + texture resolution.
half ComputeFractalDepth(float2 uv, Texture2D tex)
{
    half2 texel_size = 0;
    tex.GetDimensions(texel_size.x,texel_size.y);
    
    // texels per pixel (roughly)
    half2 duvdx = half2(ddx(uv) * texel_size);
    half magDdx = dot(duvdx, duvdx);
    half2 duvdy = half2(ddy(uv) * texel_size);
    half magDdy = dot(duvdy, duvdy);

    half rho = max(magDdx, magDdy); // (texels/pixel)^2
    half footprint = max(sqrt(rho), half(0.00006103515626)); // texels/pixel
    // Map into the "depth" space expected by FractalTextureMip (must be > 0)
    return footprint;// max(footprint, 1e-6);
}

half2 Blend3GaussianRA(half2 gaussian1, half2 gaussian2, half2 gaussian3,
	half3 weights)
{
		half2 gaussian = ((weights.x * gaussian1 + weights.y * gaussian2 + weights.z * gaussian3 - half2(0.5,0.5))
            / sqrt(weights.x * weights.x + weights.y * weights.y + weights.z * weights.z)) + half2(0.5, 0.5);
        return saturate(gaussian);
}

half4 Blend3GaussianRGBA(half4 gaussian1, half4 gaussian2, half4 gaussian3,
	half3 weights)
{
		half4 gaussian = ((weights.x * gaussian1 + weights.y * gaussian2 + weights.z * gaussian3 - half(0.5))
            / sqrt(weights.x * weights.x + weights.y * weights.y + weights.z * weights.z)) + half(0.5);
        return saturate(gaussian);
}

// Unity/HLSL port of XorDev's original "fractal_texture_mip" (same math: log/exp are natural base-e)
//
// NOTE: Must be called in a pixel/fragment shader (ddx/ddy/derivatives required).

float4 FractalTextureMip(Texture2D tex, SamplerState samp, float2 uv)
{

#if 1
    // Error.mdl note:
    // Replace ComputeFractalDepth with the CalculateLevelOfDetail function. This is faster by virtue of
    // giving us a quad-uniform value. This means there won't be a change in fractal detail scale within
    // the quad, and we don't have to calculate and store derivatives (which used several more full precision registers!)
    //
    // This has a slight quirk in that the return value is clamped to [0, log2(resolution)]. In order to 
    // get fractal details at scales below where mip 0 would be used, we just multiply the UV's by the texture
    // resolution to offset the mip level to a positive number then subtract out the log2 of the resolution after
    // to give us mip levels [-log2(resolution), 0]

    float2 texel_size = 0;
    tex.GetDimensions(texel_size.x,texel_size.y);
    //texel_size *= 0.5f;
    float maxSize = log2(texel_size.x);
    float LOD = tex.CalculateLevelOfDetail(sampler_TrilinearClamp, uv * texel_size) - maxSize;
#else
    // ComputeFractalDepth can still be used if we broadcast or average the LOD value within the quad.
    // Note that Non uniform quad intrinsics (VK_KHR_shader_quad_control / OpCapability GroupNonUniformQuad) seem 
    // to cause the Adreno compiler to run the shader at quarter rate? Claimed occupancy suddenly jumps from 50%
    // to 100% when GroupNonUniformQuad is declared. This makes me very suspicious that quad intrinsics are 
    // emulated in a way that drops the maximum number of threads per compute unit by 4. Thus the theoretical max
    // occupancy is 4x lower and the impact of register use is also 4x lower.

    float LOD = min(log2(ComputeFractalDepth(uv, tex)), 65503);
    #if !defined(SHADER_API_MOBILE) 
        LOD = QuadReadLaneAt(LOD, 0u);
    #endif
#endif
    
    float2 uv1 = uv / exp2(floor(LOD));

#if 1
    // Complete bullshit, but kinda works. Use a blending function for gaussians that preserves variance.
    // The distibution of colors in the detail map should be sorta gaussian, all channels are centered on 0.5. 
    // The real way to do this would be to follow something like Burley 2019 (https://www.jcgt.org/published/0008/04/02/paper-lowres.pdf)
    // and remap the contents of the detail map to be truly gaussian
    half4 tex0 = tex.Sample(samp, uv1 * 2.0f);
    half4 tex2 = tex.Sample(samp, uv1 * 0.5f);
    half4 tex1 = tex.Sample(samp, uv1);
    half4 output;
    output = Blend3GaussianRGBA(tex1, tex0, tex2, half3(0.5, 0.5 - 0.5*frac(LOD), 0.5*frac(LOD)));
    return output;
#else
    half4 tex0 = tex.Sample(samp, uv1 * 2.0f);
    half4 tex2 = tex.Sample(samp, uv1 * 0.5f);
    half4 blend = lerp(tex0, tex2, frac(LOD));

    half4 tex1 = tex.Sample(samp, uv1);
    return (tex1 + blend) * 0.5;
#endif
}

//Drop in replacement for SAMPLE_TEXTURE2D
float4 SAMPLE_TEXTURE2D_FRACTAL(Texture2D tex, SamplerState ss,  float2 uv )
{
    //float depth = ComputeFractalDepth(uv, tex);
    return FractalTextureMip(tex, ss, uv);
}

//Shader Graph hook
void FractalTexture_float(Texture2D tex, SamplerState ss, float2 uv, out float4 textureOut)
{
    textureOut = SAMPLE_TEXTURE2D_FRACTAL( tex,  ss,   uv );    
}

#endif