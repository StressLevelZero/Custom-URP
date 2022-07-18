//-----------------------------------------------------------------------------------
// SCREEN SPACE REFLECTIONS
// 
// Original shader by error.mdl, Toocanz, and Xiexe. Modified by error.mdl for Bonelab's custom render pipeline
// to work with the SRP and take advantage of having a hierarchical depth texture and mips of the screen color
//
//-----------------------------------------------------------------------------------

#if !defined(SLZ_SSR)
#define SLZ_SSR

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SSRGlobals.hlsl"

#if defined(UNITY_STEREO_INSTANCING_ENABLED) || defined(UNITY_STEREO_MULTIVIEW_ENABLED)
Texture2DArray<float> _CameraHiZDepthTexture;
float4x4 SLZ_PreviousViewStereo[2];
#define prevVP SLZ_PreviousViewStereo[unity_StereoEyeIndex]
#else
Texture2D<float> _CameraHiZDepthTexture;
float4x4 SLZ_PreviousView;
#define prevVP SLZ_PreviousView
#endif
SamplerState sampler_CameraHiZDepthTexture;

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


struct SSRData
{
	float3	wPos;
	float3	viewDir;
	float3	rayDir;
	half3	faceNormal;
	float	hitRadius;
	float	stepSize;
	int		maxSteps;
	float	perceptualRoughness;
	float	edgeFade;
	float2	scrnParams;
#if defined(UNITY_STEREO_INSTANCING_ENABLED) || defined(UNITY_STEREO_MULTIVIEW_ENABLED)
	Texture2DArray GrabTextureSSR;
#else
	Texture2D GrabTextureSSR;
#endif
	SamplerState samplerGrabTextureSSR;
	float4 noise;
};


SSRData GetSSRDataWithGlobalSettings(
	float3	wPos,
	float3	viewDir,
	half3	rayDir,
	half3	faceNormal,
	half	perceptualRoughness,
	half4   noise)
{
	SSRData ssrData;
	ssrData.wPos = wPos;
	ssrData.viewDir = viewDir;
	ssrData.rayDir = rayDir;
	ssrData.faceNormal = faceNormal;
	ssrData.hitRadius = _SSRHitRadius;
	ssrData.maxSteps = _SSRSteps;
	ssrData.edgeFade = _SSREdgeFade;
	ssrData.perceptualRoughness = perceptualRoughness;
	ssrData.scrnParams = _ScreenParams.xy;
	ssrData.GrabTextureSSR = _CameraOpaqueTexture;
	ssrData.samplerGrabTextureSSR = sampler_trilinear_clamp;
	ssrData.noise = noise;
	return ssrData;
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


/** @brief Scales SSR step size based on distance and angle such that a step moves the ray by one pixel in 2D screenspace
 *
 *	@param rayDir Direction of the ray
 *  @param rayPos Camera space position of the ray
 *
 *  @return Step size scaled to move the ray 1/maxIterations of the vertical dimension of the screen
 */
float perspectiveScaledStep(const float3 rayDir, float3 rayPos)
{
	// Vector between rayDir and a ray from the camera to the ray's position scaled to have the same z value as raydir.
	// This is approximately the distance in flat screen coordinates the ray will move
	float screenLen = length(rayDir.xy - rayPos.xy * (rayDir.z / rayPos.z));
	// Create scaling factor, which when multiplied by the ray's Z position will give a step size that will move the ray by about 1 pixel in the X,Y plane of the screen
	// UNITY_MATRIX_P._m11 is the cotangent of the half-fov angle
	float distScale = 2.0 / (UNITY_MATRIX_P._m11 * _ScreenParams.y * max(screenLen, 0.0001));

	return distScale * rayPos.z;
}


/** @brief March a ray from a given position in a given direction
 *         until it intersects the depth buffer.
 *
 *  Given a starting location and direction, march a ray in steps scaled
 *  to the pixel size at the ray's current
 *  position. At each step convert the ray's position to screenspace
 *  coordinates and depth, and compare to the depth texture's value at that location.
 *  If the depth at the ray's current position is less than 4 times the step size,
 *  increase the mip level to sample the depth pyramid at and double the ray's step
 *  size to match. Otherwise drop the mip level by one and half the step size.
 *  If the ray is within two steps of the depth buffer, halve the step size.
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
 *         depth texture.
 *  @param stepSize Initial size of the steps the ray moves each
 *         iteration before it gets within largeRadius of the depth texture.
 *  @param noise Random noise added to offset the ray's starting position.
 *         This dramatically helps to hide repeating artifacts from the ray-
 *         marching process.
 *  @param maxIterations The maximum number of times we can step the ray
 *         before we give up.
 *  @return The final xyz position of the ray, with the number of iterations
 *          it took stored in the w component. If the function ran out of
 *          iterations or the ray went off screen, the xyz will be (0,0,0).
 */
float4 reflect_ray(float3 reflectedRay, float3 rayDir, float hitRadius, 
	float noise, const float maxIterations, half FdotR, half FdotV)
{
	/* 
     *  If we are in VR, we have effectively two screens side by side in a single texture. We want to stop the ray if it goes off screen. The problem is, we can't simply look at
	 *  the screen-space uv coordinates as a ray could pass from one eye to the other staying within the 0 to 1 uv range. Thus, we need to make sure the ray doesn't go off the
	 *  half of the screen that the eye rendering it occupies. Thus, the horizontal range is 0 to 0.5 for the left eye and 0.5 to 1 for the right.
	 */
	 
	#if UNITY_SINGLE_PASS_STEREO
		half x_min = 0.5*unity_StereoEyeIndex;
		half x_max = 0.5*unity_StereoEyeIndex + 0.5;
	#else
		half x_min = 0.0;
		half x_max = 1.0;
	#endif
	
	//Matrix that goes directly from world space to view space.
	//static const float4x4 worldToDepth = //mul(UNITY_MATRIX_MV, unity_WorldToObject);
	
	
	
	float totalIterations = 0;//For tracking how far this ray has gone for fading out later
	
	
	// Controls whether the ray is progressing forward or back along the ray
	// path. Set to 1, the ray goes forward. Set to -1, the ray goes back.
	float direction = 1;
	
	// Final position of the ray where it gets within the small radius of the depth buffer
	float3 finalPos = float3(1.#INF,0,0);

	float step_noise = mad(noise, 0.01, 0.05);
#define tanHalfFOV 1 //I'm not sure how to extract the FOV from the projection matrix, so instead just assume its 90
	/*

	float perspectiveLen = length(rayDir.xy - reflectedRay.xy * (rayDir.z / reflectedRay.z));
	float distScale = -(tanHalfFOV) / (0.5*maxIterations * max(perspectiveLen, 0.05));
	
	float dynStepSize = clamp(distScale * reflectedRay.z, stepSize, 30*stepSize);
	*/
	int mipLevel = 0;
	float stepMultiplier = 2.0f;

	float dynStepSize = perspectiveScaledStep(rayDir.xyz, reflectedRay.xyz);
	//hitRadius *= 1 + noise;
	hitRadius = mad(noise, hitRadius, hitRadius);
	float dynHitRadius = hitRadius * dynStepSize;
	float largeRadius = max(2.0 * dynStepSize * stepMultiplier, hitRadius);
	float defaultLR = largeRadius;
	//reflectedRay += rayDir * dynStepSize * noise;
	//return reflectedRay;

	//largeRadius *= 4.0f;
	float oddStep = 1;
	float totalDistance = 0.0f;
	reflectedRay += 1* defaultLR * (FdotV / (FdotR + 0.0000001)) * rayDir;

	for (float i = 0; i < maxIterations; i++)
	{
		//totalIterations = i;
		oddStep = oddStep == 0;
		//stepSize = stepSizeMult * abs(reflectedRay.z) * distScale;
		//largeRadius = max(stepSizeMult*largeRadius0, mad(stepSize, noise, stepSize));
		//hitRadius = largeRadius * 0.05 / stepSizeMult;

		float3 spos = ComputeGrabScreenPos(CameraToScreenPosCheap(reflectedRay));

		float2 uvDepth = spos.xy / spos.z;

		//If the ray is outside of the eye's view frustrum, we can stop there's no relevant information here
		if (uvDepth.x > x_max || uvDepth.x < x_min || uvDepth.y > 1 || uvDepth.y < 0)
		{
			break;
		}


		float rawDepth = SAMPLE_TEXTURE2D_X_LOD(_CameraHiZDepthTexture, sampler_CameraHiZDepthTexture, uvDepth, mipLevel).r;
		float linearDepth = Linear01Depth(rawDepth, _ZBufferParams);
		
		linearDepth = linearDepth >= 0.999999 ? 9999 : linearDepth;
		//float sampleDepth = -mul(worldToDepth, float4(reflectedRay.xyz, 1)).z;
		float sampleDepth = -reflectedRay.z;
		float realDepth = linearDepth * _ProjectionParams.z;

		float depthDifference = abs(sampleDepth - realDepth);

		
		if (depthDifference > 2*largeRadius)
		{
			mipLevel += 1 * oddStep;
			stepMultiplier += stepMultiplier * oddStep;
			largeRadius += largeRadius * oddStep;
		}
		else if (mipLevel > 0)
		{
			stepMultiplier *= 0.5;// /= mipLevel;
			largeRadius *= 0.5; ///= mipLevel;
			mipLevel -= 1;
		}
		

		// If the ray is within the large radius, check if it is within the small radius.
		// If it is, stop raymarching and set the final position. If it is not, decrease
		// the step size and possibly reverse the ray direction if it went past the small
		// radius


		UNITY_BRANCH if (direction == 1)
		{
				 
			if(depthDifference < largeRadius && sampleDepth > realDepth - dynHitRadius)
			{
				

				UNITY_BRANCH if(sampleDepth < realDepth + dynHitRadius && mipLevel == 0)
				{
					finalPos = reflectedRay;
					break;
				}
			direction = -1;
			stepMultiplier *= 0.5;
			}
		}
		else
		{
			if(sampleDepth < realDepth)
			{

				UNITY_BRANCH if(sampleDepth > realDepth - dynHitRadius && mipLevel == 0)
				{
					finalPos = reflectedRay;
					break;
				}
				
				direction = 1;
				stepMultiplier *= 0.5;
			}
		}

		float step = direction * dynStepSize * stepMultiplier;
		reflectedRay = mad(rayDir, step,  reflectedRay);
		totalDistance += step;

		dynStepSize = max(perspectiveScaledStep(rayDir.xyz, reflectedRay.xyz), hitRadius);
		dynHitRadius = hitRadius * dynStepSize;

		largeRadius = max(2.0 * dynStepSize * stepMultiplier, hitRadius);
	}
	return float4(finalPos.xyz, totalDistance);
}



float4 GetRayHit(const float3 wPos, const float3 wRay)
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

	float3 sOrigin = ComputeNormalizedDeviceCoordinatesWithZ(wPos); // origin of the ray
	sOrigin.xy *= _ScreenParams.xy; //make xy units pixels rather than 0-1 to make rounding to the closest pixel musch easier
	float3 sRayEnd = ComputeNormalizedDeviceCoordinatesWithZ(wPos + wRay, UNITY_MATRIX_VP); // second point along the ray
	sRayEnd.xy *= _ScreenParams.xy;
	float3 sRay = sRayEnd - sOrigin;
	float3 rcpSRay = rcp(sRay); // 1/(p_2 - p_1), used to find the interpolation factor

	/* our depth pyramid starts at mip 1. Thus, there is a 1 in 4 chance the ray's depth
	 * is already behind the depth stored for it in the depth pyramid. To fix this, replace
	 * the ray origin's depth with the depth sampled from mip 0 of the pyramid
	 */
	float2 rcpPixelDim = 2.0 / _ScreenParams.xy; // our depth pyramid starts at mip 1, so the base pixel size is doubled
	sOrigin.z = SAMPLE_TEXTURE2D_X_LOD(_CameraHiZDepthTexture, sampler_CameraHiZDepthTexture, rcpPixelDim * sOrigin.xy, 0).r;
	
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
	float2 voxelOffset;
	#define SSR_PIXEL_DELTA 1.0e-9 // lowest bit of 16,384 is 1
	voxelOffset.x = sRay.x > 0 ? 1.0 + SSR_PIXEL_DELTA : SSR_PIXEL_DELTA;
	voxelOffset.y = sRay.y > 0 ? 1.0 + SSR_PIXEL_DELTA : SSR_PIXEL_DELTA;

	/* Make an initial step without testing if we've intersected the depth since by
	 * definition we start on it. find the smallest interpolation factor that will take
	 * the ray to one of the walls (i.e. the closest wall)
	 */

	float s_x = (floor(sCurrPos.x + 0.5) + voxelOffset.x  - sOrigin.x) * rcpSRay.x;
	float s_y = (floor(sCurrPos.y + 0.5) + voxelOffset.y  - sOrigin.y) * rcpSRay.y;
	float s_min = min(s_x, s_y);
	int mipLevel = 0;
	float mipPower = 1;
	bool hit = false;
	for (int i = 0; i < _SSRSteps && !hit; i++)
	{
		sCurrPos = sOrigin + s_min * sRay; // interpolate/extrapolate the marcher's postion from the origin along the ray using the lerp factor calculated last iteration
		float depth = SAMPLE_TEXTURE2D_X_LOD(_CameraHiZDepthTexture, sampler_CameraHiZDepthTexture, rcpPixelDim * sCurrPos.xy, mipLevel).r;
		float2 s_xy = (floor(mipPower * (sCurrPos.xy + 0.5)) + voxelOffset.xy - sOrigin.xy) * rcpSRay.xy;
		s_min = min(s_xy.x, s_xy.y);
		float s_z = (depth - sOrigin.z) * rcpSRay.z;
		s_min = min(s_min, s_z);

	}

	return float4(0, 0, 0, 0);
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
	if (FdotR <= 0)
	{
		return float4(0, 0, 0, 0);
	}

	float FdotV = (dot(data.faceNormal, data.viewDir.xyz));

	float3 screenUVs = ComputeGrabScreenPos(mul(UNITY_MATRIX_VP, float4(data.wPos,1)).xyw);
	screenUVs.xy = screenUVs.xy / screenUVs.z;

	// Ray's starting position, in camera space
	float3 reflectedRay = mul(UNITY_MATRIX_V, float4(data.wPos.xyz, 1)).xyz;

	// Random offset to the ray, based on roughness
	float3 rayNoise = 1.25 * data.perceptualRoughness * data.perceptualRoughness * (data.noise.rgb - 0.5);
	rayNoise = rayNoise - dot(rayNoise, data.faceNormal) * data.faceNormal; // Make the offset perpendicular to the face normal so the ray can't be offset into the face
	data.rayDir += rayNoise;
	data.rayDir = mul(UNITY_MATRIX_V, float4(data.rayDir.xyz, 0));
	
	data.rayDir.xyz = normalize(data.rayDir.xyz);

	/*
	 * Do the raymarching against the depth texture. This returns a world-space position where the ray hit the depth texture,
	 * along with the number of iterations it took stored as the w component.
	 */
	
	float4 finalPos = reflect_ray(reflectedRay, data.rayDir, data.hitRadius, 
			data.noise.r, data.maxSteps, FdotR, FdotV);
	
	
	// get the total number of iterations out of finalPos's w component and replace with 1.
	float totalDistance = finalPos.w;
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
	 * just don't fade on X in VR. This isn't really a problem as we have tons
	 * of screen real estate that is not within the FOV of the headset and thus
	 * we can actually reflect some stuff that is technically off-screen.
	 */
	
	#if UNITY_SINGLE_PASS_STEREO
	float xfade = 1;
	#else
	float xfade = smoothstep(0, data.edgeFade, uvs.x)*smoothstep(1, 1-data.edgeFade, uvs.x);//Fade x uvs out towards the edges
	#endif
	float yfade = smoothstep(0, data.edgeFade, uvs.y)*smoothstep(1, 1-data.edgeFade, uvs.y);//Same for y
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
		float roughRadius = 1.33*totalDistance * ( 1.0 / (1.0 - data.perceptualRoughness) - 1); // 1 / (1 - roughness) - 1 is approx. tan(0.5pi * roughness)
		float blur = min(_CameraOpaqueTexture_Dim.w * roughRadius * abs(UNITY_MATRIX_P._m11) / length(finalPos), _CameraOpaqueTexture_Dim.z);
		float4 reflection = SAMPLE_TEXTURE2D_X_LOD(data.GrabTextureSSR, data.samplerGrabTextureSSR, uvs.xy, blur);//float4(getBlurredGP(PASS_SCREENSPACE_TEXTURE(GrabTextureSSR), scrnParams, uvs.xy, blurFactor),1);
		//reflection *= _ProjectionParams.z;
		//reflection.a *= smoothness*reflStr*fade;
		//return 	totalDistance < 0.1 ? float4(1, 0, 1, 1) : float4(reflection.rgb, fade);
		return float4(reflection.rgb, fade); //sqrt(1 - saturate(uvs.y)));
}

#endif