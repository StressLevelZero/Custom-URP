
/**
 *	Soft blending for particles/other transparent effects by writing an offset
 * depth and z-testing. Given a projection position Z, a noise value, and
 * a blending transition distance in meters, calculates a new Z value between
 * the given clip z value and the z value of a point blendDist meters behind it 
 * in worldspace. Writing this value as depth will result in an increasing
 * percentage of the pixels getting clipped by the ztest as objects
 * within blendDist approach the fragment from behind
 * 
 * @param clipPosZ	The clip-space position of the fragment passed by the
 *					interpolator as SV_POSITION. If a clip-space position
 *					is calculated within the fragment, this must be z / w
 *					to account for perspective
 * @param noise		A random value between 0 and 1, the fraction of blendDist
 *					to offset the pixel. Best used with screenspace bluenoise
 *					(See SLZBlueNoise.hlsl)
 * @param blendDist Max distance to offset the depth by in viewspace (equal in
 *					length to worldspace).  
 * @return A depth value, which if output as SV_Depth or SV_DepthLessEqual will
 *			make the fragments dither out as objects behind the surface approach 
 *			from blendDist away to in front of the fragments
 */
float SLZSoftBlendZTest(float clipPosZ, float noise, float blendDist)
{
	float depthOffset = blendDist * noise;
	float cameraZ = 1.0 / (_ZBufferParams.z * clipPosZ + _ZBufferParams.w);
	cameraZ += depthOffset;
	return (rcp(cameraZ) - _ZBufferParams.w) / _ZBufferParams.z;
}


/**
 * Soft blending for particles/other transparent effects using the depth
 * prepass to measure the distance to the closest opaque surface behind the
 * fragment. Requires the depth-prepass
 *
 *
 * @param screenUVs Normalized 0-1 UV screen position of the fragment
 * @param wPos		World-space postion of the fragment
 * @param blendDist Distance behind the fragment in view-space to start
 *					blending at
 * @return float between 1 and 0 indicating how visible the fragment is
 */
float SLZSoftBlendDepth(float2 screenUVs, float3 wPos, float blendDist)
{
	float rawDepth = SampleSceneDepth(screenUVs);
	float sceneZ = (unity_OrthoParams.w == 0) ? LinearEyeDepth(rawDepth, _ZBufferParams) : LinearDepthToEyeDepth(rawDepth);
	float thisZ = LinearEyeDepth(wPos, GetWorldToViewMatrix());
	float fade = saturate((1 / blendDist) * (sceneZ - thisZ));
	fade *= fade;
	return fade;
}


