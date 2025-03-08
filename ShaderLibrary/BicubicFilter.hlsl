#pragma once

// https://web.archive.org/web/20180927181721/http://www.java-gaming.org/index.php?topic=35123.0
// https://web.archive.org/web/20180925133343/http://vec3.ca/bicubic-filtering-in-fewer-taps/
// Also GPU Gems 2: Chapter 20. Fast Third-Order Texture Filtering

// 4 tap b-spline cubic filter that re-arranges the bicubic formula into a weighted sum of 4 weighted sums of 2x2 groups of pixels.
// The weighted sum of each 2x2 group can be calculated on the hardware texture unit. This is done by calculating a UV coordinate
// such that the bilinearly filtered weight of each pixel is equal to the weight needed for our bicubic sum. This quarters the
// number of texture fetches needed.

float4 BSplineWeights(float v)
{
	float4 n = float4(1.0, 2.0, 3.0, 4.0) - v;
	float4 s = n * n * n;
	float x = s.x;
	float y = s.y - 4.0 * s.x;
	float z = s.z - 4.0 * s.y + 6.0 * s.x;
	float w = 6.0 - x - y - z;
	return float4(x, y, z, w) * (1.0 / 6.0);
}

float3 SampleBSplineRGB_LOD (Texture2D tex, SamplerState ss, float2 uv, float4 mip_TexelSize, int mipLevel = 0)
{
	float2 texSize = mip_TexelSize.zw;
	float2 invTexSize = mip_TexelSize.xy;
   
	uv = uv * texSize - 0.5;

	float2 fracUV = frac(uv);
	float2 floorUV = floor(uv);

	float4 xcubic = BSplineWeights(fracUV.x);
	float4 ycubic = BSplineWeights(fracUV.y);

	float4 c = floorUV.xxyy + float2(-0.5, +1.5).xyxy;
    
	float4 s = float4(xcubic.xz + xcubic.yw, ycubic.xz + ycubic.yw);
	float4 offset = c + float4(xcubic.yw, ycubic.yw) / s;
    
	offset *= invTexSize.xxyy;
    
	float3 sample0 = tex.SampleLevel(ss, offset.xz, mipLevel).rgb;
	float3 sample1 = tex.SampleLevel(ss, offset.yz, mipLevel).rgb;
	float3 sample2 = tex.SampleLevel(ss, offset.xw, mipLevel).rgb;
	float3 sample3 = tex.SampleLevel(ss, offset.yw, mipLevel).rgb;

	float sx = s.x / (s.x + s.y);
	float sy = s.z / (s.z + s.w);

	return lerp(lerp(sample3, sample2, sx), lerp(sample1, sample0, sx), sy);
}

float3 SampleBSplineRGB(Texture2D tex, SamplerState ss, float2 uv)
{
	float2 resolution;
	float mipLevels;
	tex.GetDimensions(0u, resolution.x, resolution.y, mipLevels);
	
	float2 texcoord = uv * resolution;
	float2 dx = ddx(texcoord);
    float2 dy = ddy(texcoord);
    float sqLen = 0.5 * (dot(dx, dx) + dot(dy, dy));
	
	int mipLevel = clamp(floor(0.5 * log2(sqLen)), 0, mipLevels - 1);
	
	float2 texSize = float2(int2(resolution) >> mipLevel);
	float2 invTexSize = 1.0 / texSize;
   
	uv = uv * texSize - 0.5;

	float2 fracUV = frac(uv);
	float2 floorUV = floor(uv);

	float4 xcubic = BSplineWeights(fracUV.x);
	float4 ycubic = BSplineWeights(fracUV.y);

	float4 c = floorUV.xxyy + float2(-0.5, +1.5).xyxy;
    
	float4 s = float4(xcubic.xz + xcubic.yw, ycubic.xz + ycubic.yw);
	float4 offset = c + float4(xcubic.yw, ycubic.yw) / s;
    
	offset *= invTexSize.xxyy;
    
	float3 sample0 = tex.SampleLevel(ss, offset.xz, mipLevel).rgb;
	float3 sample1 = tex.SampleLevel(ss, offset.yz, mipLevel).rgb;
	float3 sample2 = tex.SampleLevel(ss, offset.xw, mipLevel).rgb;
	float3 sample3 = tex.SampleLevel(ss, offset.yw, mipLevel).rgb;

	float sx = s.x / (s.x + s.y);
	float sy = s.z / (s.z + s.w);

	return lerp(lerp(sample3, sample2, sx), lerp(sample1, sample0, sx), sy);
}