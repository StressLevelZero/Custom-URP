#ifndef SLZ_MOBILE_ANTIBANDING
#define SLZ_MOBILE_ANTIBANDING


half3 ApplyBayerAntibanding(half3 color, float2 positionCS)
{
	const half4x4 bayerWeight = half4x4(
		 0.0 / 16.0, 12.0 / 16.0,  3.0 / 16.0, 15.0 / 16.0,
		 8.0 / 16.0,  4.0 / 16.0, 11.0 / 16.0,  7.0 / 16.0,
		 2.0 / 16.0, 14.0 / 16.0,  1.0 / 16.0, 13.0 / 16.0,
		10.0 / 16.0,  6.0 / 16.0,  9.0 / 16.0,  5.0 / 16.0);
	
	int2 ditherIdx = int2(fmod(positionCS.xy, 4.0));
	half ditherVal = bayerWeight[ditherIdx.x][ditherIdx.y];
	
	// Convert to (roughly) gamma space so that 1/255 directly 
	// corresponds to one quantization step
	color.rgb = sqrt(color.rgb);
	color.rgb += (1.0 / 255.0) * (ditherVal - 0.5);
	color.rgb *= color.rgb;
	return color;
}

half AntibandingNoise(float2 positionCS)
{
	// Jimenez, Jorge. Next Generation Post Processing in Call of Duty Advanced Warfare
	float3 magic = float3(0.06711056, 0.00583715, 52.9829189);
	half noise = (half) frac(magic.z * frac(dot(positionCS, magic.xy)));
	return noise;
}

half3 ApplyInterleavedAntibanding(half3 color, float2 positionCS)
{
	
	half3 magic = half3(0.06711056, 0.00583715, 52.9829189);
	half ditherVal = frac(magic.z * frac(dot(positionCS, magic.xy)));
	
	// Convert to (roughly) gamma space so that 1/255 directly 
	// corresponds to one quantization step
	color.rgb = sqrt(color.rgb);
	color.rgb += half(1.0h / 255.0h) * (ditherVal - half(0.5h));
	color.rgb *= color.rgb;
	return color;
}

void ApplyInterleavedAntibanding(inout half3 color, half noise)
{
	// Convert to (roughly) gamma space so that 1/255 directly 
	// corresponds to one quantization step
	color.rgb = sqrt(color.rgb);
	color.rgb += half(1.0h / 255.0h) * (noise - half(0.5h));
	color.rgb *= color.rgb;
}

#endif