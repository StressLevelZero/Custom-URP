#if !defined(SLZ_FRACTAL)
#define SLZ_FRACTAL


// Returns something proportional to "how minified is this texture lookup right now?"
// Uses texel-space derivatives so it automatically adapts to UV tiling + object scale + texture resolution.
float ComputeFractalDepth(float2 uv, Texture2D tex)
{
    float _FractalDepthMul = 1.; // start with 1.0

    float2 texel_size = 0;
    uint MipLevel=0;  uint Width=128;   uint Height=128;	uint NumberOfLevels=0;
    tex.GetDimensions( MipLevel,Width,Height,NumberOfLevels);
    texel_size.x = Width;
    texel_size.y = Height;
    
    // texels per pixel (roughly)
    float2 duvdx = ddx(uv) * texel_size;
    float2 duvdy = ddy(uv) * texel_size;

    float rho = max(dot(duvdx, duvdx), dot(duvdy, duvdy)); // (texels/pixel)^2
    float footprint = sqrt(max(rho, 1e-12));               // texels/pixel

    // Map into the "depth" space expected by FractalTextureMip (must be > 0)
    return max(footprint * _FractalDepthMul, 1e-6);
}



// Unity/HLSL port of XorDev's original "fractal_texture_mip" (same math: log/exp are natural base-e)
//
// NOTE: Must be called in a pixel/fragment shader (ddx/ddy/derivatives required).

float4 FractalTextureMip(Texture2D tex, SamplerState samp, float2 uv, float depth)
{
    depth = max(depth, 1e-18);

    // Find the pixel level of detail (natural log)
    float LOD = log(depth);

    // Round LOD down
    float LOD_floor = floor(LOD);

    // Fract part for interpolating
    float LOD_fract = LOD - LOD_floor;

    // Compute scaled uvs (natural exp)
    float e1 = exp(LOD_floor - 1.0);
    float e2 = exp(LOD_floor + 0.0);
    float e3 = exp(LOD_floor + 1.0);

    float2 uv1 = uv / e1;
    float2 uv2 = uv / e2;
    float2 uv3 = uv / e3;

    // Compute continuous derivatives (matches original)
    float2 dx = ddx(uv) / depth * exp(1.0);
    float2 dy = ddy(uv) / depth * exp(1.0);

    // Sample at 3 scales
    float4 tex0 = tex.SampleGrad(samp, uv1, dx, dy);
    float4 tex1 = tex.SampleGrad(samp, uv2, dx, dy);
    float4 tex2 = tex.SampleGrad(samp, uv3, dx, dy);

    // Blend samples together
    return (tex1 + lerp(tex0, tex2, LOD_fract)) * 0.5;
}

//Drop in replacement for SAMPLE_TEXTURE2D
float4 SAMPLE_TEXTURE2D_FRACTAL(Texture2D tex, SamplerState ss,  float2 uv )
{
    float depth = ComputeFractalDepth(uv, tex);
    return FractalTextureMip(tex, ss, uv, depth);
}

//Shader Graph hook
void FractalTexture_float(Texture2D tex, SamplerState ss, float2 uv, out float4 textureOut)
{
    textureOut = SAMPLE_TEXTURE2D_FRACTAL( tex,  ss,   uv );    
}

#endif