//-----------------------------------------------------------------------------------
// SCREEN SPACE REFLECTIONS
// 
// Original shader by error.mdl, Toocanzs, and Xiexe. Modified by error.mdl for Bonelab's custom render pipeline
// to work with the SRP and take advantage of having a hierarchical depth texture and mips of the screen color
//
//-----------------------------------------------------------------------------------

#if !defined(SLZ_SSR)
#define SLZ_SSR

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareHiZTexture.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SSRGlobals.hlsl"

#if defined(UNITY_STEREO_INSTANCING_ENABLED) || defined(UNITY_STEREO_MULTIVIEW_ENABLED)
//Texture2DArray<float> _CameraHiZDepthTexture;
float4x4 SLZ_PreviousViewStereo[2];
#define prevVP SLZ_PreviousViewStereo[unity_StereoEyeIndex]
#else
//Texture2D<float> _CameraHiZDepthTexture;
float4x4 SLZ_PreviousView;
#define prevVP SLZ_PreviousView
#endif
//SamplerState sampler_CameraHiZDepthTexture;

/* 
 * Port of BiRP's old screeen position calcuation, URP contains no direct equivalent. Modified slightly to
 * only output a float3 instead of a float4 since the z-component is worthless, putting the w in the z instead
 * 
 * @param pos projection-space position to calculate the screen uv's of
 * @return float3 containing the screen uvs (xy) and the perspective factor (z)
 */
inline float3 ComputeGrabScreenPos(float3 pos) {
#if UNITY_UV_STARTS_AT_TOP
	float scale = -1.0;
#else
	float scale = 1.0;
#endif
	float3 o = pos * 0.5f;
	o.xy = float2(o.x, o.y * scale) + o.z;
	o.z = pos.z;
	return o;
}

float4 SLZScreenToClip(float3 pos)
{
	pos.xy = 2 * (pos.xy - 0.5);
	float w = (pos.z - UNITY_MATRIX_P._m23) / (UNITY_MATRIX_P._m22 * UNITY_MATRIX_P._m32);
	//pos *= w;
#if UNITY_UV_STARTS_AT_TOP
	pos.y = -pos.y;
#endif
	return float4(pos, w);
}

float2 SLZComputeNDCFromClip(float4 positionCS)
{
#if UNITY_UV_STARTS_AT_TOP
	// Our world space, view space, screen space and NDC space are Y-up.
	// Our clip space is flipped upside-down due to poor legacy Unity design.
	// The flip is baked into the projection matrix, so we only have to flip
	// manually when going from CS to NDC and back.
	positionCS.y = -positionCS.y;
#endif

	positionCS.xy *= rcp(positionCS.w);
	positionCS.xy = positionCS.xy * 0.5 + 0.5;

	return positionCS.xy;
}

float3 SLZComputeNDCFromClipWithZ(float4 positionCS)
{
#if UNITY_UV_STARTS_AT_TOP
	// Our world space, view space, screen space and NDC space are Y-up.
	// Our clip space is flipped upside-down due to poor legacy Unity design.
	// The flip is baked into the projection matrix, so we only have to flip
	// manually when going from CS to NDC and back.
	positionCS.y = -positionCS.y;
#endif

	positionCS *= rcp(positionCS.w);
	positionCS.xy = positionCS.xy * 0.5 + 0.5;

	return positionCS.xyz;
}

struct SSRData
{
	float3	wPos;
	float3	viewDir;
	float3	rayDir;
	half3	faceNormal;
	half	perceptualRoughness;
	half	RdotV;
	float   zDerivativeSum;
	float4 noise;
};


SSRData GetSSRData(
	float3	wPos,
	float3	viewDir,
	half3	rayDir,
	half3	faceNormal,
	half	perceptualRoughness,
	half	RdotV,
	float	zDerivativeSum,
	half4   noise)
{
	SSRData ssrData;
	ssrData.wPos = wPos;
	ssrData.viewDir = viewDir;
	ssrData.rayDir = normalize(rayDir);
	ssrData.faceNormal = normalize(faceNormal);
	ssrData.perceptualRoughness = perceptualRoughness;
	ssrData.RdotV = RdotV;
	ssrData.zDerivativeSum = zDerivativeSum;
	ssrData.noise = noise;
	return ssrData;
}

float GetDepthDerivativeSum(float depth)
{
	float2 slope;
	slope.x = ddx(depth);
	slope.y = ddy(depth);
	slope = abs(slope);
	return slope.x + slope.y;
}

/** @brief Partially transforms a given camera space point to screenspace in 7 operations for the purposes of computing its screen UV position
 *
 *  Normally, transforming from camera space to projection space involves multiplying a 4x4 matrix
 *  by a float4 for a total of 28 operations. However, most of the elements of the camera to projection
 *  matrix are 0's, we don't need the z component for getting screen coordinates, and the w component is
 *  is just the negative of the input's z. Just doing the necessary calculations reduces the operations down to just 7.
 *	NOTE: this assumes an orthogonal projection matrix, might not work for some headsets (pimax) with non-parallel near/far
 *  planes
 *
 *  @param pos camera space coordinate to transform
 *  @return float3 containing the x and y projection space coordinates, and the z component for perspective correction
 */

float3 CameraToScreenPosCheap(const float3 pos)
{
	return float3(pos.x * UNITY_MATRIX_P._m00 + pos.z * UNITY_MATRIX_P._m02, pos.y * UNITY_MATRIX_P._m11 + pos.z * UNITY_MATRIX_P._m12, -pos.z);
}


/** @brief Computes the tangent of the half-angle of the cone that encompasses the
 *  Phong specular lobe. Used for determining the range of random ray directions and
 *  the mip level from the color pyramid to sample. Formula is derived from 
 *  Lawrence 2002 "Importance Sampling of the Phong Reflectance Mode". Lawrence
 *  gives the angle between perfect specular and random ray as arccos(u^(1/(n+1))
 *  where u is a random value and n is the phong power. Uludag 2014 "Hi-Z 
 *  Screen-Space Cone-Traced Re?ections" sets u to a constant of 0.244 to 
 *  get the average angle. Karis 2013 "Specular BRDF Reference" converts the
 *  phong specular exponent to physical roughness as n = 2/r^2 - 2. Combining
 *  this with the formula for the angle, the angle is almost a linear function
 *  of roughness on the 0-1 range, where theta ~= 1.33 r. We want the tangent
 *  of the angle for our use case, and using the approximation 
 *  tan(x) ~= 1/(1-(2/pi)*x) - 1 plus some tweaking of constants to get a better
 *  fit we get our formula tan(theta) = 1/(1 - (2.5 / pi) * r^2) - 1.0;
 * 
 */
float TanPhongConeAngle(const float roughness)
{
	float roughness2 = roughness;
	float alpha = roughness2 / (2 - roughness2);
	return rcp(1 - (2.5 * INV_PI) * alpha) - 1.0;
}

/** @brief Scales SSR step size based on distance and angle such that a step moves the ray by about one pixel in 2D screenspace
 *
 *	@param rayDir Direction of the ray
 *  @param rayPos Camera space position of the ray
 *
 *  @return Step size scaled to move the ray by one pixel
 */
float perspectiveScaledStep(const float3 rayDir, float3 rayPos)
{
	// Vector between rayDir and a ray from the camera to the ray's position scaled to have the same z value as raydir.
	// This is approximately the xy-distance in perspective distorted space the ray will move with a step size of 1
	float2 screenRay = mad((-rayDir.z / rayPos.z), rayPos.xy, rayDir.xy);
	float invScreenLen = rsqrt(mad(screenRay.x, screenRay.x, (screenRay.y * screenRay.y)));
	// Create scaling factor, which when multiplied by the ray's Z position will give a step size that will move the ray by about 1 pixel in the X,Y plane of the screen
	// _SSRDistScale is tan(half FOV) / (half screen vertical resolution)
	float distScale = min(_SSRDistScale * invScreenLen, 1.0e12);

	return distScale * abs(rayPos.z);
}


/** @brief March a ray from a given position in a given direction
 *         until it intersects the depth buffer.
 *
 *  Given a starting location and direction, march a ray in steps scaled
 *  to the pixel size at the ray's current
 *  position. At each step convert the ray's position to screenspace
 *  coordinates and depth, and compare to the depth texture's value at that location.
 *  If the depth difference at the ray's current position is >2x the step size,
 *  increase the mip level to sample the depth pyramid at and double the ray's step
 *  size to match. Otherwise drop the mip level by one and half the step size.
 *  If the depth from the depth pyramid is also smaller
 *  than the rays current depth, also reverse the direction. Repeat until the ray
 *  is within hitRadius of the depth texture or the maximum number of
 *  iterations is exceeded. Additionally, the loop will be cut short if the
 *  ray passes out of the camera's view.
 *  
 *  @param reflectedRay Starting position of the ray, in camera space
 *  @param rayDir Direction the ray is going, in camera space
 *  @param hitRadius Distance above/below the depth texture the ray must be
 *         before it can be considered to have successfully intersected the
 *         depth texture, as a fraction of the step size.
 *  @param noise Random noise added to modify the hit radius. This helps to
 *		   hide stair-step artifacts from the ray-marching process.
 *  @return The final xyz position of the ray, with the number of iterations
 *          it took stored in the w component. If the function ran out of
 *          iterations or the ray went off screen, the xyz will be (0,0,0).
 */
float4 reflect_ray(float3 reflectedRay, float3 rayDir, float hitRadius, 
	float noise, half FdotR)
{
	bool movingForwards = true;
	float3 finalPos = float3(1.#INF,0,0);

	uint mipLevel = _SSRMinMip;
	float stepMultiplier = float(2u << _SSRMinMip);

	float dynStepSize = perspectiveScaledStep(rayDir.xyz, reflectedRay.xyz);
	hitRadius = mad(noise, hitRadius, hitRadius);
	float dynHitRadius = hitRadius * dynStepSize;
	float largeRadius = max(2 * dynStepSize * stepMultiplier, hitRadius);

	float totalDistance = 0.0f;
	float FdotR4 = FdotR * FdotR;
	FdotR4 *= FdotR4;
	reflectedRay += lerp(0, 0.5*largeRadius, 1 - FdotR4) * rayDir;
	bool storeLastPos = true;
	for (float i = 0; i < _SSRSteps; i++)
	{

		float3 spos = ComputeGrabScreenPos(CameraToScreenPosCheap(reflectedRay));

		float2 uvDepth = spos.xy / spos.z;

		//If the ray is outside of the eye's view frustrum, we can stop there's no relevant information here
		if (any(uvDepth.xy > 1) || any(uvDepth.xy < 0))
		{
			break;
		}

		int2 uvInt = int2(uvDepth * _HiZDim.xy) >> mipLevel;
		float rawDepth = LOAD_TEXTURE2D_X_LOD(_CameraHiZDepthTexture, uvInt, mipLevel).r;
		float linearDepth = Linear01Depth(rawDepth, _ZBufferParams);
		
		linearDepth = linearDepth > 0.999999 ? 9999 : linearDepth;
		//float sampleDepth = -mul(worldToDepth, float4(reflectedRay.xyz, 1)).z;
		float sampleDepth = -reflectedRay.z;
		float realDepth = linearDepth * _ProjectionParams.z;

		float depthDifference = abs(sampleDepth - realDepth);

		
		if ((depthDifference > 2 * largeRadius) && (mipLevel < _HiZHighestMip))
		{
				mipLevel += 1u;
				stepMultiplier += stepMultiplier;
				largeRadius += largeRadius;
		}
		else if (mipLevel > _SSRMinMip)
		{
			stepMultiplier *= 0.5;// /= mipLevel;
			largeRadius *= 0.5; ///= mipLevel;
			mipLevel -= 1u;
		}
		
		bool inLargeRadius = depthDifference < largeRadius;
		bool inHitRadius = depthDifference < dynHitRadius;
		bool isMinMip = mipLevel <= _SSRMinMip+1;
		bool isRayInFront = sampleDepth < realDepth;
		
		
		// Save first position the ray went behind an object as the final position
		// If the ray never hits, this position will be used instead of falling back
		// to the cubemap. This fills holes behind objects less obviously than sampling
		// from the cubemap
		if (!isRayInFront && storeLastPos)
		{
			finalPos = reflectedRay;
		}
		storeLastPos = isRayInFront ? true : false;
		// If we're within the hit radius, we're done
		UNITY_BRANCH if (inHitRadius && isMinMip)
		{
			finalPos = reflectedRay;
			totalDistance = -totalDistance;
			break;
		}

		// Swap directions if the ray is moving away from the depth surface, and if the mip level is 0 half the step size
		// to avoid issues with the ray never resolving
		if (movingForwards)
		{
			if (inLargeRadius && !isRayInFront)
			{
				movingForwards = false;
				stepMultiplier = isMinMip ? 0.5 * stepMultiplier : stepMultiplier;
			}
		}
		else
		{
			if (isRayInFront)
			{
				movingForwards = true;
				stepMultiplier = isMinMip ? 0.5 * stepMultiplier : stepMultiplier;
			}
		}

		// Move forward a step if the ray is above depth or if it is more than 2 steps behind the depth
		// or if it is at mip level 0. Dont move otherwise to prevent moving backwards towards a false
		// surface created by a high level mip
		if (isRayInFront || !inLargeRadius || isMinMip)
		{
			float step = movingForwards ? dynStepSize * stepMultiplier : -dynStepSize * stepMultiplier;
			reflectedRay = mad(rayDir, step, reflectedRay);
			totalDistance += step;

			dynStepSize = max(perspectiveScaledStep(rayDir.xyz, reflectedRay.xyz), hitRadius);
			dynHitRadius = hitRadius * dynStepSize;

			largeRadius = max(2.0 * dynStepSize * stepMultiplier, hitRadius);
		}
	}
	//underPos.w = abs(underPos.w);
	float4 outp = float4(finalPos.xyz, totalDistance);//finalPos.x == 1.#INF && underPos.w != 1.#INF ? underPos : float4(finalPos.xyz, totalDistance);
	return outp;
}





/** @brief Gets the reflected color for a pixel
 *
 *  @param data Struct containing all necessary data for the SSR marcher
 *  @return Color of the screenspace reflection. The alpha component contains a fade factor to be used for blending with normal cubemap reflections
 *			for when the ray fails to hit anything or the ray goes offscreen
 */

float4 getSSRColor(SSRData data)
{


	float FdotR = dot(data.faceNormal, data.rayDir.xyz);
	
	UNITY_BRANCH if (FdotR <= 0)
	{
		return float4(0, 0, 0, 0);
	}

	float FdotV = (dot(data.faceNormal, data.viewDir.xyz));

	float3 screenUVs = ComputeGrabScreenPos(mul(UNITY_MATRIX_VP, float4(data.wPos,1)).xyw);
	screenUVs.xy = screenUVs.xy / screenUVs.z;

	// Ray's starting position, in camera space
	float3 reflectedRay = mul(UNITY_MATRIX_V, float4(data.wPos.xyz, 1)).xyz;

	// Random offset to the ray, based on roughness
	// Expensive!
	float rayTanAngle = TanPhongConeAngle(data.perceptualRoughness * data.perceptualRoughness); //half the angle because random scatter looks bad, rely on the color pyramid for blur 
	float3 rayNoise = rayTanAngle * (2*data.noise.rgb - 1);
	rayNoise = rayNoise - dot(rayNoise, data.faceNormal) * data.faceNormal; // Make the offset perpendicular to the face normal so the ray can't be offset into the face
	data.rayDir += rayNoise;
	data.rayDir.xyz = normalize(data.rayDir.xyz);

	float RdotV = saturate(0.95 * dot(data.rayDir, -data.viewDir.xyz) + 0.05);

	UNITY_BRANCH if (RdotV <= 0)
	{
		return float4(0, 0, 0, 0);
	}

	data.rayDir = mul(UNITY_MATRIX_V, float4(data.rayDir.xyz, 0));
	
	float3 screenOffset = normalize(mul(UNITY_MATRIX_V, float4(data.faceNormal, 0)));
	
	reflectedRay += float(2u << _SSRMinMip) * screenOffset * perspectiveScaledStep(float3(screenOffset), reflectedRay);
	/*
	 * Do the raymarching against the depth texture. This returns a world-space position where the ray hit the depth texture,
	 * along with the number of iterations it took stored as the w component.
	 */
	
	float4 finalPos = reflect_ray(reflectedRay, data.rayDir, _SSRHitRadius,
			data.noise.r, FdotR);
	
	
	// get the total number of iterations out of finalPos's w component and replace with 1.
	float totalDistance = abs(finalPos.w);
	float rayHit = finalPos.w < 0;
	finalPos.w = 1;
	

	

	/*
	 * A position of 0, 0, 0 signifies that the ray went off screen or ran
	 * out of iterations before actually hitting anything.
	 */
	
	if (finalPos.x == 1.#INF) 
	{
		return float4(0,0,0,0);
	}
	
	/*
	 * Get the screen space coordinates of the ray's final position
	 */
	float3 uvs;			

	#if defined(SSR_POST_OPAQUE)
		uvs = ComputeGrabScreenPos(CameraToScreenPosCheap(finalPosClip));
	#else
		float4 finalPosWorld = mul(UNITY_MATRIX_I_V, finalPos);
		float4 finalPosClip = mul(prevVP, finalPosWorld);
		uvs = ComputeGrabScreenPos(finalPosClip.xyw);
	#endif

	uvs.xy = uvs.xy / uvs.z;
				

	/*
	 * Fade towards the edges of the screen. If we're in VR, we can't really
	 * fade horizontally all that well as that results in stereo mismatch (the
	 * reflection will begin to fade in different locations in each eye). Thus
	 * only fade on the outer edge of each eye
	 */
	
	#if defined(UNITY_STEREO_INSTANCING_ENABLED) || defined(UNITY_STEREO_MULTIVIEW_ENABLED)
	float xfade = smoothstep(0, 0.1, unity_StereoEyeIndex == 0 ? uvs.x : 1.0 - uvs.x);
	#else
	float xfade = smoothstep(0, 0.1, uvs.x)*smoothstep(1, 1- 0.1, uvs.x);//Fade x uvs out towards the edges
	#endif
	float yfade = smoothstep(0, 0.1, uvs.y)*smoothstep(1, 1- 0.1, uvs.y);//Same for y
	xfade *= xfade;
	yfade *= yfade;
	//float lengthFade = smoothstep(1, 0, 2*(totalSteps / data.maxSteps)-1);
	
	float fade = saturate(2*(RdotV)) * xfade * yfade;
	
	float rayTanAngle2 = TanPhongConeAngle(data.perceptualRoughness * data.perceptualRoughness);
	float roughRadius = rayTanAngle2 * totalDistance;
	
	float roughRatio = roughRadius * abs(UNITY_MATRIX_P._m11) / length(finalPos);
	//roughRatio = rayHit > 0 ? roughRatio : data.perceptualRoughness * data.perceptualRoughness;
	//uvs.xy += roughRatio * (2.0*data.noise.rg - 1.0);
	float blur = min(log2(_CameraOpaqueTexture_Dim.y * roughRatio), _CameraOpaqueTexture_Dim.z);
	
	float4 reflection = SAMPLE_TEXTURE2D_X_LOD(_CameraOpaqueTexture, sampler_TrilinearClamp, uvs.xy, blur);//float4(getBlurredGP(PASS_SCREENSPACE_TEXTURE(GrabTextureSSR), scrnParams, uvs.xy, blurFactor),1);
	
	#if defined(UNITY_COMPILER_DXC) && defined(_SM6_QUAD)
    
	reflection.a = rayHit;
	
    real4 colorX = QuadReadAcrossX(reflection);
    real4 colorY = QuadReadAcrossY(reflection);
    real4 colorD = QuadReadAcrossDiagonal(reflection);
    float4 kernel = float4(0.5 * reflection.a, 0.2 * colorX.a, 0.2 * colorY.a, 0.1 * colorD.a);
	float weight = kernel.x + kernel.y + kernel.z + kernel.w;
    float3 avgSSRColor = kernel.x * reflection.rgb +  kernel.y * colorX.rgb +  kernel.z * colorY.rgb + kernel.w * colorD.rgb;
       
    reflection.rgb = weight > 0.01 ? float3(avgSSRColor.rgb / weight) : reflection.rgb;
    
	float fadeX = QuadReadAcrossX(fade);
	float fadeY = QuadReadAcrossY(fade);
	float fadeD = QuadReadAcrossDiagonal(fade);
	
	
	fade = weight > 0.01 ? dot(float4(fade, fadeX, fadeY, fadeD), kernel) / weight : fade;
	
	#endif
	//fade = 1;
	reflection.a = fade;
	//reflection.rgb = rayHit ? reflection.rgb : float3(1,0,1);
	
	//reflection *= _ProjectionParams.z;
	//reflection.a *= smoothness*reflStr*fade;
	//return 	totalDistance < 0.1 ? float4(1, 0, 1, 1) : float4(reflection.rgb, fade);
	return reflection; //sqrt(1 - saturate(uvs.y)));
}





/*
 * ----------------------------------------------------------------------------------------------------
 * ----------------------------------------------------------------------------------------------------
 * ----------------------------------------------------------------------------------------------------
 * New SSR marching functions, not complete and not currently used.
 * ----------------------------------------------------------------------------------------------------
 * ----------------------------------------------------------------------------------------------------
 * ----------------------------------------------------------------------------------------------------
 */



float3 GetRayHit(const float3 wPos, const float3 wRay, const float3 viewDir, const int steps, inout float depthOut, const float ddz, const float noise = 0)
{
	/* Raymarching in screenspace requires special handling, as the depth is not
	 * simply the z distance from the camera, but rather is a function of 1/z,
	 * the near clip distance, and the far clip distance. Since the near and
	 * far clip planes are constant during the rendering of the object, the
	 * depth value can be reduced to a simple 1st degree polynomial that only
	 * depends on 1/z
	 *
	 * d = c * (1/z) + b
	 * where c = reversed z ? -1 / near clip : 1 / near clip,
	 * and b = reversed z ? 1 - (1/near clip) * (1/(far - near)) : (1/near clip) * (1/(far - near))
	 *
	 * "Perspective Correct Interpolation", Kok-Lim Low (2002) shows that given
	 * two points p1, p2 in projection space, the 1/z value of a third point t
	 * that is a fraction s from p1 to p2 in flattened xy screen coordinates
	 * can be found using simple linear interpolation.
	 *
	 * 1/z_t = 1/z_1 + s * ( 1/z_2 - 1/z_1)
	 *
	 * multiplying both sides of the equation by c and adding b:
	 *
	 * c*1/z_t + b	= c * (1/z_1 + s * ( 1/z_2 - 1/z_1)) + b
	 *			d_t = (c * 1/z_1 + b) + s * (c * 1/z_2 + b - c * 1/z_1 - b) // +b, -b cancels out
	 *				= d_1 + s * (d_2 - d_1)
	 *
	 * Therefore, the depth value of a point on the line can also be derived from
	 * linear interpolation. This also holds true for extrapolating a point beyond
	 * p1 and p2 as well using s values <0 or >1.
	 *
	 * If we have a known depth value for t and want to find its screen coordinates,
	 * derive s from the formula of d_t and find the screen x,y by linear
	 * interpolation
	 *
	 * s = (d_t - d_1) / (d_2 - d_1);
	 * xy_t = xy_1 + s * (xy_2 - xy_1);
	 */

	 //float4 mip0Dim = float4(_HiZDim.xy / mipPow, _HiZDim.zw * mipPow);
	float3 sOrigin = ComputeNormalizedDeviceCoordinatesWithZ(wPos, UNITY_MATRIX_VP); // origin of the ray
	sOrigin.xy *= _HiZDim.xy; //make xy units pixels rather than 0-1 to make rounding to the closest pixel much easier
	float3 sRayEnd = ComputeNormalizedDeviceCoordinatesWithZ(wPos + wRay, UNITY_MATRIX_VP); // second point along the ray



	sRayEnd.xy *= _HiZDim.xy;
	float3 sRay = sRayEnd - sOrigin;
	float3 rcpSRay = rcp(sRay); // 1/(p_2 - p_1), used to find the interpolation factor

	//float cos1 = dot(normalize(sNormal), normalize(sRay));
	/* our depth pyramid starts at mip 1. Thus, there is a 1 in 4 chance the ray's depth
	 * is already behind the depth stored for it in the depth pyramid. To fix this, replace
	 * the ray origin's depth with the depth sampled from mip 0 of the pyramid
	 */
	float2 rcpPixelDim = 2.0 / _ScreenParams.xy; // our depth pyramid starts at mip 1, so the base pixel size is doubled

	//Lowest level of depth pyramid is mip 1, which is the max of 4 pixels. For a flat plane, the surface defined
	// by the depth from the pyramid is very voxel-y. In cross-section, this looks like a saw, with the ray origins being on
	// or underneath the 'teeth'. Rays moving at glancing angles will hit the 'teeth' and return. The solution is to move
	// the rays back toward the camera to the plane

#ifndef PROGRAM_GS 
	/*
	float2 slope;
	slope.x = ddx_fine(sOrigin.z);
	slope.y = ddy_fine(sOrigin.z);
	slope = abs(slope);
	sOrigin.z += 2 * slope.x + FLT_MIN;
	sOrigin.z += 2 * slope.y + FLT_MIN;
	*/
	sOrigin.z = sOrigin.z + float((2 << _SSRMinMip)) * (ddz)+1e-6;
#endif	
	//float dot1 = abs(dot(wRay, viewDir));
	//sOrigin.z += HALF_MIN * noise;
	//sOrigin.xy = floor(sOrigin.xy) + 0.5;
	//sOrigin += sRay * rcpSRay.y * 16 * saturate(dot1);

	float3 sCurrPos = sOrigin;	// current position of the ray, starts at the origin

	/* For each step of the raymarching process, we advance the ray to the boundary
	 * of a screenspace voxel whose walls on the x and y are the bounds of the pixel
	 * the ray is currently in, and on the z by either the near clip and the camera
	 * depth if the ray's depth is above the camera depth, or the camera depth and
	 * the camera depth plus some epsilon if the ray is between the two.
	 *
	 * We want to move to the farthest wall of the voxel, so the walls opposite
	 * the ray direction really don't matter. In fact, trying test against them would
	 * cause issues since the ray should be directly on one of the walls having moved
	 * there in the previous step. Thus we only test against the farthest wall on X
	 * and on Y for the given ray direction
	 *
	 */


	 /* add a tiny delta, and if moving in the +x/+y one pixel, to the ray's coordinate so that when rounded we get the far wall instead of the
	 *
	  */
	float4 voxelOffset;
#define SSR_PIXEL_DELTA 0.000488281f
	voxelOffset.x = sRay.x >= 0 ? 1.0 + SSR_PIXEL_DELTA : -SSR_PIXEL_DELTA;
	voxelOffset.y = sRay.y >= 0 ? 1.0 + SSR_PIXEL_DELTA : -SSR_PIXEL_DELTA;
	voxelOffset.x = sRay.x >= 0 ? 1.0 + SSR_PIXEL_DELTA : -SSR_PIXEL_DELTA;
	voxelOffset.y = sRay.y >= 0 ? 1.0 + SSR_PIXEL_DELTA : -SSR_PIXEL_DELTA;



	float2 raySign;
	raySign.x = sRay.x > 0 ? 1.0 : -1.0;
	raySign.y = sRay.y > 0 ? 1.0 : -1.0;

	float2 posDelta = raySign * SSR_PIXEL_DELTA;


	/* Make an initial step without testing if we've intersected the depth since by
	 * definition we start on it. find the smallest interpolation factor that will take
	 * the ray to one of the walls (i.e. the closest wall)
	 */
	int mipLevel = _SSRMinMip;
	float2 mipDim = float2(int2(_HiZDim.xy) >> mipLevel);
	float2 mipRatio = mipDim * _HiZDim.zw;
	float2 pixelCoords = ((sCurrPos.xy) * mipRatio + posDelta);

	float2 s_xy0 = (floor(pixelCoords.xy) + 2 * voxelOffset.xy) * rcp(mipRatio);
	s_xy0 = (s_xy0 - sOrigin.xy) * rcpSRay.xy;
	float s_min = min(s_xy0.x, s_xy0.y);

	bool hit = false;
	bool onScreen = true;//sRay.z < 0;
	int maxMip = clamp(_HiZHighestMip, 0, 14);

	bool oddStep = true;
	for (int i = 0; (i < steps) && !hit && onScreen; i++)
	{
		sCurrPos = sOrigin + s_min * sRay; // interpolate/extrapolate the marcher's postion from the origin along the ray using the lerp factor calculated last iteration
		onScreen = sCurrPos.x >= 1 && sCurrPos.x <= _HiZDim.x && sCurrPos.y >= 1 && sCurrPos.y <= _HiZDim.y;

		float2 mipDim = float2(int2(_HiZDim.xy) >> mipLevel);
		float2 mipRatio = mipDim * _HiZDim.zw;
		int2 pixelCoords = int2((sCurrPos.xy) * mipRatio + posDelta);
		float depth = LoadHiZDepth(int3(pixelCoords, mipLevel)).r;
		float4 voxel;
		voxel.xy = (float2(pixelCoords.xy) + voxelOffset.xy) * rcp(mipRatio);
		voxel.zw = float2(depth.r, depth.r * _SSRHitScale + mipLevel * _SSRHitBias);
		float4 s = (voxel - sOrigin.xyzz) * rcpSRay.xyzz;

		s.w = s.w + (2 << (mipLevel - 1)) * (s.w - s.z);
		float s_min_new = min(s.x, s.y);
		hit = mipLevel == _SSRMinMip && s.z <= s_min && s.w >= s_min;
		bool increaseMip = s_min_new < s.z;
		bool decreaseMip = !increaseMip;
		increaseMip = increaseMip && oddStep;
		//decreaseMip = decreaseMip && s.w > s_min;
		mipLevel = increaseMip ? min(mipLevel + 1, maxMip) : mipLevel;
		mipLevel = decreaseMip ? max(mipLevel - 1, _SSRMinMip) : mipLevel;
		oddStep = !oddStep;

		s_min_new = min(s_min_new, s.z);
		s_min = max(s_min_new, s_min);

		depthOut = depth;
	}


	depthOut = hit ? 1 : depthOut;
	depthOut = !onScreen ? 0 : depthOut;
#ifndef PROGRAM_GS
	sCurrPos.x = hit && onScreen ? sCurrPos.x : 1.#INF;
#endif
	//depthOut = HiZDimBuffer[4].dim.zw == (HiZDimBuffer[0].dim.xy / HiZDimBuffer[4].dim.xy);
	return float3(sCurrPos.xy * _HiZDim.zw, sCurrPos.z);
}




float4 getSSRColorNew(SSRData data)
{


	//float RdotV = saturate(dot(data.rayDir, -data.viewDir.xyz));
	//if (RdotV <= 0)
	//{
	//	return float4(0, 0, 0, 0);
	//}


	// Random offset to the ray, based on roughness
	float rayTanAngle = TanPhongConeAngle(data.perceptualRoughness * data.perceptualRoughness); //half the angle because random scatter looks bad, rely on the color pyramid for blur 
	float3 rayNoise = rayTanAngle * (2 * data.noise.rgb - 1);
	rayNoise = rayNoise - dot(rayNoise, data.faceNormal) * data.faceNormal; // Make the offset perpendicular to the face normal so the ray can't be offset into the face
	data.rayDir += rayNoise;
	data.rayDir.xyz = normalize(data.rayDir.xyz);
	float RdotV = saturate(0.95 * dot(data.rayDir, -data.viewDir.xyz) + 0.05);

	UNITY_BRANCH if (RdotV <= 0)
	{
		return float4(0, 0, 0, 0);
	}

	
	/*
	 * Do the raymarching against the depth texture. This returns a world-space position where the ray hit the depth texture,
	 * along with the number of iterations it took stored as the w component.
	 */

	float4 finalPos;
	float finalDepth;
	finalPos.xyz = GetRayHit(data.wPos, data.rayDir, data.viewDir.xyz, _SSRSteps, finalDepth, data.zDerivativeSum, data.noise.r);

	// get the total number of iterations out of finalPos's w component and replace with 1.
	//float totalDistance = finalPos.w;
	finalPos.w = 1;



	/*
	 * A position of 0, 0, 0 signifies that the ray went off screen or ran
	 * out of iterations before actually hitting anything.
	 */

	if (finalPos.x == 1.#INF)
	{
		return float4(0, 0, 0, 0);
	}

	/*
	 * Get the screen space coordinates of the ray's final position
	 */
	float3 uvs;
	//float4 finalPosClip = SLZScreenToClip(finalPos.xyz);
	float3 finalPosWorld = ComputeWorldSpacePosition(finalPos.xy, finalPos.z, UNITY_MATRIX_I_VP);
//#if !defined(SSR_POST_OPAQUE)
	float4 finalPosClip = mul(prevVP, float4(finalPosWorld,1));
//#endif
	uvs = ComputeGrabScreenPos(finalPosClip.xyw);


	uvs.xy = uvs.xy / uvs.z;


	/*
	 * Fade towards the edges of the screen. If we're in VR, we can't really
	 * fade horizontally all that well as that results in stereo mismatch (the
	 * reflection will begin to fade in different locations in each eye). Thus
	 * just don't fade on X in VR. This isn't really a problem as we have tons
	 * of screen real estate that is not within the FOV of the headset and thus
	 * we can actually reflect some stuff that is technically off-screen.
	 */

#if UNITY_SINGLE_PASS_STEREO
	float xfade = 1;
#else
	float xfade = smoothstep(0, 0.1, uvs.x) * smoothstep(1, 1 - 0.1, uvs.x);//Fade x uvs out towards the edges
#endif
	float yfade = smoothstep(0, 0.1, uvs.y) * smoothstep(1, 1 - 0.1, uvs.y);//Same for y
	xfade *= xfade;
	yfade *= yfade;
	//float lengthFade = smoothstep(1, 0, 2*(totalSteps / data.maxSteps)-1);

	float fade = xfade * yfade;

	/*
	 * Get the color of the grabpass at the ray's screen uv location, applying
	 * an (expensive) blur effect to partially simulate roughness
	 * Second input for getBlurredGP is some math to make it so the max blurring
	 * occurs at 0.5 smoothness.
	 */
	 //float blurFactor = max(1,min(blur, blur * (-2)*(smoothness-1)));
	 /*
	 int mipLevels, dummy1, dummy2, dummy3;
 #if defined(UNITY_STEREO_INSTANCING_ENABLED) || defined(UNITY_STEREO_MULTIVIEW_ENABLED)
		 data.GrabTextureSSR.GetDimensions(0, dummy1, dummy2, dummy3, mipLevels);
 #else
		 data.GrabTextureSSR.GetDimensions(0, dummy1, dummy2, mipLevels);
 #endif
		 mipLevels += 3;
 */
	//float roughRadius = 1.33 * totalDistance * (1.0 / (1.0 - data.perceptualRoughness) - 1); // 1 / (1 - roughness) - 1 is approx. tan(0.5pi * roughness)
	float totalDistance = length(finalPosWorld.xyz - data.wPos.xyz);
	float rayTanAngle2 = TanPhongConeAngle(data.perceptualRoughness * data.perceptualRoughness);
	float roughRadius = rayTanAngle2 * totalDistance;

	float roughRatio = roughRadius * abs(UNITY_MATRIX_P._m11) / length(finalPosWorld - _WorldSpaceCameraPos);
	float blur = log2(_CameraOpaqueTexture_Dim.y * roughRatio);
	float4 reflection = SAMPLE_TEXTURE2D_X_LOD(_CameraOpaqueTexture, sampler_TrilinearClamp, uvs.xy, blur);//float4(getBlurredGP(PASS_SCREENSPACE_TEXTURE(GrabTextureSSR), scrnParams, uvs.xy, blurFactor),1);
	//reflection *= _ProjectionParams.z;
	//reflection.a *= smoothness*reflStr*fade;
	//return 	totalDistance < 0.1 ? float4(1, 0, 1, 1) : float4(reflection.rgb, fade);
	return float4(reflection.xyz, fade); //sqrt(1 - saturate(uvs.y)));
}

#endif