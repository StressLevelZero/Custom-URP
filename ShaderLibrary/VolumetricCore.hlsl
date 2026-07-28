#ifndef VOLUMETRIC_CORE_INCLUDED
#define VOLUMETRIC_CORE_INCLUDED

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/VolumeRendering.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/GlobalSamplers.hlsl"
//#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ShaderVariablesFunctions.hlsl"

//#if _VOLUMETRICS_ENABLED
//#pragma multi_compile_fragment _ _HiQSampling 
//#endif
#if defined(UNITY_SINGLE_PASS_STEREO) || defined(UNITY_STEREO_INSTANCING_ENABLED) || defined(UNITY_STEREO_MULTIVIEW_ENABLED)
    #define VOL_EYE_COUNT 2
#else
    #define VOL_EYE_COUNT 1
#endif

#if defined(_VOLUMETRICS_ENABLED) || defined(_VOLUMETRICS_ENABLED_HQ)
#define _VOLUMETRICS_ENABLED_ANY 1
#endif

TEXTURECUBE(_SkyTexture);
SAMPLER(sampler_SkyTexture);
const int _SkyMipCount;

const int    _FrameIndex;       
const  float2 _FoveaCenterUV;   // per-eye [0..1], default (0.5, 0.5)
const  float  _FoveaStrength;   // a >= 0, 0 disables (try 0.3..0.8 on Quest)

// SH Used for sky occlusion
// // Monochromatic Spherical Harmonics Coefficients
/*
CBUFFER_START(MonoSHBuffer)
    float _SHMonoCoefficients[9];
CBUFFER_END
*/

#if defined(_VOLUMETRICS_ENABLED_ANY)

CBUFFER_START(VolumetricsCB)
float4x4 TransposedCameraProjectionMatrix;
float4x4 CameraProjectionMatrix;
float4 _VBufferDistanceEncodingParams;
float4 _VolumetricResultDim;
float3 _VolCameraPos;
CBUFFER_END

TEXTURE3D(_VolumetricResult);

#else

#define TransposedCameraProjectionMatrix    ((float4x4)0)
#define CameraProjectionMatrix              ((float4x4)0)
#define _VBufferDistanceEncodingParams      ((float4)0)
#define _VolumetricResultDim                ((float4)0)
#define _VolCameraPos                       ((float3)0)

#endif


//float4 _VolumePlaneSettings; // Not used

// Interleaved Gradient Noise — 3D (isotropic)
// Use integer-ish coordinates (voxel/pixel indices or rounded world-space)
// inline float InterleavedGradientNoise3D(float3 p, int frameCount)
// {
//     // These “magic” values just need to be incommensurate; they’re not sacred.
//     const float4 MAGIC = float4(0.06711056f, 0.00583715f, 0.10100101f, 52.9829189f);
//     const float3 FRAME_SCALE = float3(2.083f, 4.867f, 7.173f);
//
//     p += FRAME_SCALE * frameCount;
//     return frac(MAGIC.w * frac(dot(p, MAGIC.xyz)));
// }



// Overlay blend: base overlaid by blend with a black bias
// base, blend in [0..1]
float4 DitherVolumetrics(float4 base, float4 blend)
{
    float4 baseInvert = 1-base ;
    float4 blendBiased = lerp(0.5,blend, baseInvert*baseInvert*baseInvert*baseInvert*baseInvert ) ; //Adding more dithering in the dark areas
    float4 lo = 2.0 * base * blendBiased;
    float4 hi = 1.0 - 2.0 * (1.0 - base) * (1.0 - blendBiased);
    return lerp(lo, hi, step(0.5, base));   // if base < 0.5 -> lo else hi
}

inline float4 CubicWeights_BSpline(float t)
{
    float t2 = t * t;
    float t3 = t2 * t;

    // weights for (-1,0,1,2)
    return float4(
        (1 - 3*t + 3*t2 - t3) / 6.0,          // (1 - t)^3 / 6
        (4 - 6*t2 + 3*t3) / 6.0,
        (1 + 3*t + 3*t2 - 3*t3) / 6.0,
        t3 / 6.0
    );
}

// Fast tricubic: 8 trilinear samples
inline float4 SampleTricubicLevel(Texture3D tex, SamplerState samp, float3 uvw, float lod)
{
    // Get mip dimensions (important if you ever change lod from 0)
    uint w=0, h=0, d=0, l=0;
    tex.GetDimensions((uint)lod, w, h, d,l);
    float3 dim    = float3((float)w, (float)h, (float)d);
    float3 invDim = 1.0 / dim;

    // Convert to "texel space" where integer coords land on texel centers
    float3 x  = uvw * dim - 0.5;
    float3 ix = floor(x);
    float3 fx = x - ix;

    // Per-axis cubic weights (4 each)
    float4 wx4 = CubicWeights_BSpline(fx.x);
    float4 wy4 = CubicWeights_BSpline(fx.y);
    float4 wz4 = CubicWeights_BSpline(fx.z);

    // Group into 2 weights per axis so we can use linear filtering:
    // (w0+w1)*lerp(T[-1],T[0]) + (w2+w3)*lerp(T[+1],T[+2])
    float2 wx = float2(wx4.x + wx4.y, wx4.z + wx4.w);
    float2 wy = float2(wy4.x + wy4.y, wy4.z + wy4.w);
    float2 wz = float2(wz4.x + wz4.y, wz4.z + wz4.w);

    float2 tx = float2(wx.x > 0 ? (wx4.y / wx.x) : 0, wx.y > 0 ? (wx4.w / wx.y) : 0);
    float2 ty = float2(wy.x > 0 ? (wy4.y / wy.x) : 0, wy.y > 0 ? (wy4.w / wy.y) : 0);
    float2 tz = float2(wz.x > 0 ? (wz4.y / wz.x) : 0, wz.y > 0 ? (wz4.w / wz.y) : 0);

    // Two sample positions per axis, in normalized UVW
    // p0 between (i-1, i), p1 between (i+1, i+2)
    float2 px = (ix.x + float2(-1.0 + tx.x, 1.0 + tx.y) + 0.5) * invDim.x;
    float2 py = (ix.y + float2(-1.0 + ty.x, 1.0 + ty.y) + 0.5) * invDim.y;
    float2 pz = (ix.z + float2(-1.0 + tz.x, 1.0 + tz.y) + 0.5) * invDim.z;

    // 8 samples
    float4 c000 = tex.SampleLevel(samp, float3(px.x, py.x, pz.x), lod);
    float4 c100 = tex.SampleLevel(samp, float3(px.y, py.x, pz.x), lod);
    float4 c010 = tex.SampleLevel(samp, float3(px.x, py.y, pz.x), lod);
    float4 c110 = tex.SampleLevel(samp, float3(px.y, py.y, pz.x), lod);

    float4 c001 = tex.SampleLevel(samp, float3(px.x, py.x, pz.y), lod);
    float4 c101 = tex.SampleLevel(samp, float3(px.y, py.x, pz.y), lod);
    float4 c011 = tex.SampleLevel(samp, float3(px.x, py.y, pz.y), lod);
    float4 c111 = tex.SampleLevel(samp, float3(px.y, py.y, pz.y), lod);

    // Combine with separable weights
    float4 a00 = c000 * wx.x + c100 * wx.y;
    float4 a10 = c010 * wx.x + c110 * wx.y;
    float4 a01 = c001 * wx.x + c101 * wx.y;
    float4 a11 = c011 * wx.x + c111 * wx.y;

    float4 b0 = a00 * wy.x + a10 * wy.y;
    float4 b1 = a01 * wy.x + a11 * wy.y;

    return b0 * wz.x + b1 * wz.y;
}

static float m_zSeq[7]	=
	{ 7.0f / 14.0f, 3.0f / 14.0f, 11.0f / 14.0f, 5.0f / 14.0f, 9.0f / 14.0f, 1.0f / 14.0f, 13.0f / 14.0f };

static void GetHexagonalClosePackedSpheres7(out float2 GetHexagonalClosePackedSpheres7[7] )
{
    float2 coords[7]; 

    float r = 0.17054068870105443882f;
    float d = 2 * r;
    float s = r * sqrt(3);

    // Try to keep the weighted average as close to the center (0.5) as possible.
    //  (7)(5)    ( )( )    ( )( )    ( )( )    ( )( )    ( )(o)    ( )(x)    (o)(x)    (x)(x)
    // (2)(1)(3) ( )(o)( ) (o)(x)( ) (x)(x)(o) (x)(x)(x) (x)(x)(x) (x)(x)(x) (x)(x)(x) (x)(x)(x)
    //  (4)(6)    ( )( )    ( )( )    ( )( )    (o)( )    (x)( )    (x)(o)    (x)(x)    (x)(x)
    coords[0] =  float2(0, 0);
    coords[1] =  float2(-d, 0);
    coords[2] =  float2(d, 0);
    coords[3] =  float2(-r, -s);
    coords[4] =  float2(r, s);
    coords[5] =  float2(r, -s);
    coords[6] =  float2(-r, s);

    // Rotate the sampling pattern by 15 degrees.
    const float cos15 = 0.96592582628906828675f;
    const float sin15 = 0.25881904510252076235f;

    for (int i = 0; i < 7; i++)
    {
        float2 coord = coords[i];

        coords[i].x = coord.x * cos15 - coord.y * sin15;
        coords[i].y = coord.x * sin15 + coord.y * cos15;
    }
        GetHexagonalClosePackedSpheres7 = coords;
}

inline float2 UVToSigned(float2 uv, float2 centerUV)
{
    float2 leftExtent  = max(centerUV, 1e-6);
    float2 rightExtent = max(1.0 - centerUV, 1e-6);

    float2 s;
    s.x = (uv.x < centerUV.x) ? ((uv.x - centerUV.x) / leftExtent.x)
                              : ((uv.x - centerUV.x) / rightExtent.x);
    s.y = (uv.y < centerUV.y) ? ((uv.y - centerUV.y) / leftExtent.y)
                              : ((uv.y - centerUV.y) / rightExtent.y);
    return s; // [-1..1]
}

inline float2 SignedToUV(float2 s, float2 centerUV)
{
    float2 leftExtent  = centerUV;
    float2 rightExtent = 1.0 - centerUV;

    float2 uv;
    uv.x = (s.x < 0) ? centerUV.x + s.x * leftExtent.x
                     : centerUV.x + s.x * rightExtent.x;
    uv.y = (s.y < 0) ? centerUV.y + s.y * leftExtent.y
                     : centerUV.y + s.y * rightExtent.y;
    return uv;
}

// r' = r / (1 + a(1-r))
// inv: r = r'(1+a) / (1 + a r')
inline float InvWarpRadius_Rational(float rw, float a)
{
    return (rw * (1.0 + a)) / (1.0 + a * rw);
}

// Chebyshev “radius” (square rings) so corners stay fully utilized.
inline float2 InvWarpSigned_SquareRadial(float2 sw, float a)
{
    float2 asw = abs(sw);
    float rw = max(asw.x, asw.y);  // 0..1 inside the square
    if (rw < 1e-6) return sw;

    float2 dir = sw / rw;          // max(|dir|) == 1
    float r = InvWarpRadius_Rational(saturate(rw), a);
    return dir * r;
}

inline float2 LinearUV_To_FroxelGridUV(float2 uvLinear, float2 centerUV, float a)
{
    // a==0 -> identity (avoid extra math cost if you want)
    if (a <= 0.0) return uvLinear;

    float2 s = UVToSigned(uvLinear, centerUV);
    float2 sGrid = InvWarpSigned_SquareRadial(s, a);
    return saturate(SignedToUV(sGrid, centerUV));
}

// Positive value pulls the volume sample toward the camera. Combats light leaking
#define VOLUMETRIC_SURFACE_DEPTH_BIAS_TEXELS 1.0

half4 GetVolumetricColor(float3 positionWS)
{
    half4 ls = half4(positionWS - _VolCameraPos, -1); //_WorldSpaceCameraPos
    ls = mul(ls, TransposedCameraProjectionMatrix);
    ls.xyz = ls.xyz / ls.w;

    float vdistance = distance(positionWS, _VolCameraPos);
    float W = EncodeLogarithmicDepthGeneralized(vdistance, _VBufferDistanceEncodingParams);

    // ls.xy is per-eye linear UV in [0..1]
    float2 uvLinear = (float2)ls.xy;

    // Convert linear UV -> froxel grid UV
    float2 uvGrid = LinearUV_To_FroxelGridUV(uvLinear, _FoveaCenterUV, _FoveaStrength);

    int eye = 0;
    #if defined(UNITY_STEREO_INSTANCING_ENABLED) || defined(UNITY_STEREO_MULTIVIEW_ENABLED) || defined(UNITY_SINGLE_PASS_STEREO)
    eye = (int)unity_StereoEyeIndex;
    #endif

    eye = clamp(eye, 0, VOL_EYE_COUNT - 1);

    float invEyeCount = rcp((float)VOL_EYE_COUNT);

    // Atlas-space UV for the 3D volume
    float2 uvPacked = float2(
        uvGrid.x * invEyeCount + (float)eye * invEyeCount,
        uvGrid.y
    );

    float3 sampleUVW = float3(uvPacked.x, uvPacked.y, W);

    float2 pixCoord = floor(uvPacked * _ScaledScreenParams.xy);

    // ---- Noise / jitter ----
    float noise = InterleavedGradientNoise(pixCoord, _FrameIndex);

    float2 xyoffset[7];
    GetHexagonalClosePackedSpheres7(xyoffset);

    float2 invXY = rcp(float2(_VolumetricResultDim.x, _VolumetricResultDim.y));
    float  invZ  = rcp(_VolumetricResultDim.z);

    // ---------------------------------------------------------------------
    // Surface depth bias:
    // Pull the sample toward the camera so opaque surfaces do not sample
    // the froxel cell behind the wall.
    //
    // Since W is the normalized froxel-depth coordinate, subtracting invZ
    // moves the sample by roughly one froxel slice toward the camera.
    // ---------------------------------------------------------------------
    sampleUVW.z -= VOLUMETRIC_SURFACE_DEPTH_BIAS_TEXELS * invZ;
    sampleUVW.z = saturate(sampleUVW.z);

    uint idx = min((uint)(noise * 7.0), 6u);

    float jitterRadiusTexelsXY = 0.3;
    float jitterRadiusTexelsZ  = 0.05;

    float2 jitterUV = xyoffset[idx] * (jitterRadiusTexelsXY * invXY);

    sampleUVW.xy += jitterUV;

    // Important:
    // Do not allow Z jitter to push the sample behind the surface again.
    // Either disable it:
    //
    // sampleUVW.z += 0;
    //
    // Or only jitter toward the camera:
    sampleUVW.z -= noise * (jitterRadiusTexelsZ * invZ);
    sampleUVW.z = saturate(sampleUVW.z);

    #if defined(_VOLUMETRICS_ENABLED_HQ)
        float4 volsample = SampleTricubicLevel(_VolumetricResult, sampler_LinearClamp, sampleUVW, 0);
    #elif defined(_VOLUMETRICS_ENABLED) 
        float4 volsample = SAMPLE_TEXTURE3D_LOD(_VolumetricResult, sampler_LinearClamp, sampleUVW, 0);
    #else
        float4 volsample = (float4)0;
    #endif

    volsample = DitherVolumetrics(volsample, noise * 0.08 + .5);

    return volsample;
}

half4 Volumetrics(half4 color, float3 positionWS) {

#if defined(_VOLUMETRICS_ENABLED) || defined(_VOLUMETRICS_ENABLED_HQ)
    
    color = max(half(0), color);  //clamping incoming colors so if negative values get here they don't cause issue   
    half4 FroxelColor = GetVolumetricColor(positionWS);
    color.rgb = FroxelColor.rgb + (color.rgb * FroxelColor.a);

#endif
    return color;
}

/* @brief Blend volumetrics with control for the surface type.
 *
 * @param color       Final surface color
 * @param positionWS  World-space position of the fragment
 * @param surfaceType Enum of the surface type, where 0: opaque, 1: transparent (alpha premultiplied), 2: fade (alpha blend) 
 * @return color blended towards the volumetric color if the surface is opaque, or blended towards transparency otherwise
 */
half4 VolumetricsSurf(half4 color, float3 positionWS, int surfaceType) {

#if defined(_VOLUMETRICS_ENABLED) || defined(_VOLUMETRICS_ENABLED_HQ)

    color = max(half(0), color);  //clamping incoming colors so if negative values get here they don't cause issue   
    half4 FroxelColor = GetVolumetricColor(positionWS);
	
    FroxelColor.rgb = surfaceType == 1 ? FroxelColor.rgb * color.a : FroxelColor.rgb;
	color.rgb *= FroxelColor.a;
	color.rgb += FroxelColor.rgb;

#endif
    return color;
}


float4 _MipFogParameters = float4(0,5,0.5,0);

half EvaluateMonochromaticSHL2(half3 normal)
{
    /* MonoSHBuffer not assigned by anything, removing it to prevent possible binding issues
    // Monochromatic SH evaluation using the coefficients array
	half shValue = _SHMonoCoefficients[0] + // L0 term (constant)
                    normal.y * _SHMonoCoefficients[1] +                  // L1 Y term (gradient)
                    normal.z * _SHMonoCoefficients[2] +                  // L1 Z term
                    normal.x * _SHMonoCoefficients[3] +                  // L1 X term
                    normal.x * normal.y * _SHMonoCoefficients[4] +       // L2 XY term
                    normal.y * normal.z * _SHMonoCoefficients[5] +       // L2 YZ term
                    (half(3.0) * normal.z * normal.z - half(1.0)) * _SHMonoCoefficients[6] + // L2 Z² term
                    normal.x * normal.z * _SHMonoCoefficients[7] +       // L2 XZ term
                    (normal.x * normal.x - normal.y * normal.y) * _SHMonoCoefficients[8];  // L2 X² - Y² term

    return shValue;
    */
    return 1;
}


//Cloning function for now
real3 DecodeHDREnvironmentMip(real4 encodedIrradiance, real4 decodeInstructions)
{
    // Take into account texture alpha if decodeInstructions.w is true(the alpha value affects the RGB channels)
    real alpha = max(decodeInstructions.w * (encodedIrradiance.a - 1.0) + 1.0, 0.0);

    // If Linear mode is not supported we can skip exponent part
    return (decodeInstructions.x * PositivePow(alpha, decodeInstructions.y)) * encodedIrradiance.rgb;
}

// Based on Uncharted 4 "Mip Sky Fog" trick: http://advances.realtimerendering.com/other/2016/naughty_dog/NaughtyDog_TechArt_Final.pdf
half3 MipFog(float3 viewDirectionWS, float depth, float numMipLevels) {

    float nearParam = _MipFogParameters.x;
    float farParam = _MipFogParameters.y;

#if defined(FOG_LINEAR)
    float mipLevel = ((depth )) * (_SkyMipCount - 1);
#else
    float mipLevel = ((1 -  (_MipFogParameters.z * saturate((depth - nearParam) / (farParam - nearParam)))  ) )  * (_SkyMipCount - 1);
#endif

//#if defined(REFLECTIONFOG)
  //  return DecodeHDREnvironmentMip(SAMPLE_TEXTURECUBE_LOD(unity_SpecCube0, samplerunity_SpecCube0, viewDirectionWS, mipLevel), unity_SpecCube0_HDR);
  //  return DecodeHDREnvironmentMip(SAMPLE_TEXTURECUBE_LOD(_SkyTexture, samplerunity_SpecCube0, viewDirectionWS, mipLevel), unity_SpecCube0_HDR);
    return (SAMPLE_TEXTURECUBE_LOD(_SkyTexture, sampler_TrilinearClamp, viewDirectionWS, mipLevel)).rgb * saturate(EvaluateMonochromaticSHL2(viewDirectionWS));

}

inline float2 GetVolumetricLinearUVFromWS(float3 positionWS)
{
    float4 ls = float4(positionWS - _VolCameraPos, -1.0);
    ls = mul(ls, TransposedCameraProjectionMatrix);
    return ls.xy / ls.w;
}

inline float3 BuildVolumetricUVW(float2 uvLinear, float distanceWS)
{
    float W = EncodeLogarithmicDepthGeneralized(distanceWS, _VBufferDistanceEncodingParams);

    float2 uvGrid = LinearUV_To_FroxelGridUV(uvLinear, _FoveaCenterUV, _FoveaStrength);

    int eye = 0;
    #if defined(UNITY_STEREO_INSTANCING_ENABLED) || defined(UNITY_STEREO_MULTIVIEW_ENABLED) || defined(UNITY_SINGLE_PASS_STEREO)
    eye = (int)unity_StereoEyeIndex;
    #endif
    eye = clamp(eye, 0, VOL_EYE_COUNT - 1);

    float invEyeCount = rcp((float)VOL_EYE_COUNT);
    float2 uvPacked = float2(uvGrid.x * invEyeCount + (float)eye * invEyeCount, uvGrid.y);

    return float3(uvPacked, W);
}

half4 SampleVolumetricUVW(float3 sampleUVW)
{
    float2 pixCoord = floor(sampleUVW.xy * _ScaledScreenParams.xy);
    float noise = InterleavedGradientNoise(pixCoord, _FrameIndex);

    float2 xyoffset[7];
    GetHexagonalClosePackedSpheres7(xyoffset);

    float2 invXY = rcp(float2(_VolumetricResultDim.x, _VolumetricResultDim.y));
    float invZ   = rcp(_VolumetricResultDim.z);

    int idx = (int)(noise * 7.0) % 7;
    float jitterRadiusTexelsXY = 0.3;
    float jitterRadiusTexelsZ  = 0.05;

    sampleUVW.xy += xyoffset[idx] * (jitterRadiusTexelsXY * invXY);
    sampleUVW.z  += (noise - 0.5) * (jitterRadiusTexelsZ * invZ);

    #if defined(_VOLUMETRICS_ENABLED_HQ)
        float4 volsample = SampleTricubicLevel(_VolumetricResult, sampler_LinearClamp, sampleUVW, 0);
    #elif defined(_VOLUMETRICS_ENABLED)
        float4 volsample = SAMPLE_TEXTURE3D_LOD(_VolumetricResult, sampler_LinearClamp, sampleUVW, 0);
    #else
        float4 volsample = (float4)0;
    #endif

    volsample = DitherVolumetrics(volsample, noise * 0.08 + 0.5);
    return volsample;
}

half4 GetVolumetricColorWS(float3 positionWS)
{
    float2 uvLinear   = GetVolumetricLinearUVFromWS(positionWS);
    float  distanceWS = distance(positionWS, _VolCameraPos);
    return SampleVolumetricUVW(BuildVolumetricUVW(uvLinear, distanceWS));
}

half4 GetVolumetricColorSky(float3 rayDirWS, float skyDistanceWS)
{
    // Any point along the ray works for XY projection.
    // The important part is that depth comes from skyDistanceWS, not far clip.
    float3 rayPointWS = _VolCameraPos + rayDirWS * 10.0;

    float2 uvLinear = GetVolumetricLinearUVFromWS(rayPointWS);
    return SampleVolumetricUVW(BuildVolumetricUVW(uvLinear, skyDistanceWS));
}

half4 VolumetricsSky(half4 color, float3 rayDirWS, float skyDistanceWS)
{
    #if defined(_VOLUMETRICS_ENABLED) || defined(_VOLUMETRICS_ENABLED_HQ)
    half4 froxel = GetVolumetricColorSky(rayDirWS, skyDistanceWS);
    color.rgb = froxel.rgb + color.rgb * froxel.a;
    #endif
    return color;
}

#endif
