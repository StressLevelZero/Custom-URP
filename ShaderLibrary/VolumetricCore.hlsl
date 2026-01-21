#ifndef VOLUMETRIC_CORE_INCLUDED
#define VOLUMETRIC_CORE_INCLUDED

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/VolumeRendering.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/GlobalSamplers.hlsl"
//#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ShaderVariablesFunctions.hlsl"

#define HiQSampling false


TEXTURECUBE(_SkyTexture);
SAMPLER(sampler_SkyTexture);
const int _SkyMipCount;

const int    _FrameIndex;       
const  float2 _FoveaCenterUV;   // per-eye [0..1], default (0.5, 0.5)
const  float  _FoveaStrength;   // a >= 0, 0 disables (try 0.3..0.8 on Quest)

// SH Used for sky occlusion
// // Monochromatic Spherical Harmonics Coefficients
CBUFFER_START(MonoSHBuffer)
    float _SHMonoCoefficients[9];
CBUFFER_END

CBUFFER_START(VolumetricsCB)
float4x4 TransposedCameraProjectionMatrix;
float4x4 CameraProjectionMatrix;
float4 _VBufferDistanceEncodingParams;
float4 _VolumetricResultDim;
float3 _VolCameraPos;
CBUFFER_END



TEXTURE3D(_VolumetricResult);
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



float Mitchell1D(float x, float B, float C)
{
    x = abs(x);
    float x2 = x * x;
    float x3 = x2 * x;

    // Piecewise Mitchell–Netravali cubic
    if (x < 1.0)
    {
        return ((12.0 - 9.0*B - 6.0*C) * x3 +
                (-18.0 + 12.0*B + 6.0*C) * x2 +
                (6.0 - 2.0*B)) / 6.0;
    }
    else if (x < 2.0)
    {
        return ((-B - 6.0*C) * x3 +
                (6.0*B + 30.0*C) * x2 +
                (-12.0*B - 48.0*C) * x +
                (8.0*B + 24.0*C)) / 6.0;
    }

    return 0.0;
}

// Weights for taps [i-1, i, i+1, i+2] given fractional f in [0,1)
float4 CubicWeights_MitchellNetravali(float f)
{
    const float B = 1.0/3.0;
    const float C = 1.0/3.0;

    // distances from sample position (i+f) to each tap
    return float4(
        Mitchell1D(1.0 + f, B, C),  // i-1
        Mitchell1D(f,       B, C),  // i
        Mitchell1D(1.0 - f, B, C),  // i+1
        Mitchell1D(2.0 - f, B, C)   // i+2
    );
}


// 4 taps on a fixed Z slice (bilinear in XY), Mitchell weights reconstructed
float4 SampleVol_BicubicXY_4Tap_Slice_MN(float2 uv01, int zSlice, int3 dimI, float3 invDim, float lod)
{
    float2 dimXY = float2(dimI.x, dimI.y);
    float2 p = uv01 * dimXY - 0.5;
    int2  i = (int2)floor(p);
    float2 f = p - (float2)i;

    // clamp base so [-1..+2] neighborhood is valid
    int2 maxBase = int2(max(1, dimI.x - 3), max(1, dimI.y - 3));
    i = clamp(i, int2(1,1), maxBase);

    float4 wx = CubicWeights_MitchellNetravali(f.x);
    float4 wy = CubicWeights_MitchellNetravali(f.y);

    float2 sx = float2(wx.x + wx.y, wx.z + wx.w);
    float2 sy = float2(wy.x + wy.y, wy.z + wy.w);

    float ax0 = (sx.x > 1e-6) ? (wx.y / sx.x) : 0.0;
    float ax1 = (sx.y > 1e-6) ? (wx.w / sx.y) : 0.0;
    float ay0 = (sy.x > 1e-6) ? (wy.y / sy.x) : 0.0;
    float ay1 = (sy.y > 1e-6) ? (wy.w / sy.y) : 0.0;

    float px0 = (float)(i.x - 1) + ax0;
    float px1 = (float)(i.x + 1) + ax1;
    float py0 = (float)(i.y - 1) + ay0;
    float py1 = (float)(i.y + 1) + ay1;

    // force exact texel-center in Z so trilinear becomes bilinear in XY
    float z = ((float)zSlice + 0.5) * invDim.z;

    float3 uvw00 = float3((px0 + 0.5) * invDim.x, (py0 + 0.5) * invDim.y, z);
    float3 uvw10 = float3((px1 + 0.5) * invDim.x, (py0 + 0.5) * invDim.y, z);
    float3 uvw01 = float3((px0 + 0.5) * invDim.x, (py1 + 0.5) * invDim.y, z);
    float3 uvw11 = float3((px1 + 0.5) * invDim.x, (py1 + 0.5) * invDim.y, z);

    float4 s00 = SAMPLE_TEXTURE3D_LOD(_VolumetricResult, sampler_LinearClamp, uvw00, lod);
    float4 s10 = SAMPLE_TEXTURE3D_LOD(_VolumetricResult, sampler_LinearClamp, uvw10, lod);
    float4 s01 = SAMPLE_TEXTURE3D_LOD(_VolumetricResult, sampler_LinearClamp, uvw01, lod);
    float4 s11 = SAMPLE_TEXTURE3D_LOD(_VolumetricResult, sampler_LinearClamp, uvw11, lod);

    return (s00 * (sx.x * sy.x)) +
           (s10 * (sx.y * sy.x)) +
           (s01 * (sx.x * sy.y)) +
           (s11 * (sx.y * sy.y));
}

// 8 taps total: 4 taps at z0 + 4 taps at z1, then lerp in Z
float4 SampleVol_BicubicXY_LinearZ_8Tap_MN(float3 uvw01, float lod)
{
    uint w, h, d;
    _VolumetricResult.GetDimensions(w, h, d);

    int3 dimI = int3((int)w, (int)h, (int)d);
    float3 dimF = float3((float)w, (float)h, (float)d);
    float3 invDim = rcp(max(dimF, 1.0));

    // small safety fallback (optional)
    if (dimI.x < 4 || dimI.y < 4 || dimI.z < 2)
        return SAMPLE_TEXTURE3D_LOD(_VolumetricResult, sampler_LinearClamp, uvw01, lod);

    float pz = uvw01.z * dimF.z - 0.5;
    int   iz = (int)floor(pz);
    float fz = pz - (float)iz;

    iz = clamp(iz, 0, max(0, dimI.z - 2));

    float4 c0 = SampleVol_BicubicXY_4Tap_Slice_MN(uvw01.xy, iz,     dimI, invDim, lod);
    float4 c1 = SampleVol_BicubicXY_4Tap_Slice_MN(uvw01.xy, iz + 1, dimI, invDim, lod);

    return lerp(c0, c1, fz);
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

half4 GetVolumetricColor(float3 positionWS)
{
    half4 ls = half4(positionWS - _VolCameraPos, -1); //_WorldSpaceCameraPos
    ls = mul(ls, TransposedCameraProjectionMatrix);
    ls.xyz = ls.xyz / ls.w;
    float vdistance = distance(positionWS, _VolCameraPos);
    float W = EncodeLogarithmicDepthGeneralized(vdistance, _VBufferDistanceEncodingParams);

    // ls.xy is per-eye linear UV in [0..1] (as your current code assumes)
    float2 uvLinear = (float2)ls.xy;

    // Convert linear UV -> froxel grid UV (inverse of the warp used during froxel rendering)
    float2 uvGrid = LinearUV_To_FroxelGridUV(uvLinear, _FoveaCenterUV, _FoveaStrength);

    // Pack into side-by-side stereo atlas (X half per eye)
    float eye = (float)unity_StereoEyeIndex;
    float xPacked = uvGrid.x * 0.5 + eye * 0.5;
    float2 uvPacked = float2(uvGrid.x * 0.5 + eye * 0.5, uvGrid.y);
    float3 DoubleUV = float3(xPacked, uvGrid.y, W);
    float2 pixCoord = floor(uvPacked * _ScaledScreenParams.xy);
    float noise = InterleavedGradientNoise(pixCoord, _FrameIndex);
    float2 xyoffset[7];
    GetHexagonalClosePackedSpheres7(xyoffset);  
    float2 invXY = rcp(float2(_VolumetricResultDim.x, _VolumetricResultDim.y));
    float  invZ  = rcp(_VolumetricResultDim.z);
    
    int idx = (int)(noise * 7.0) % 7;    
    // jitterRadiusTexels = how many *texels* you want at maximum.
    // Start small: 0.20–0.40 texels is a good range. //Make variable or keep magic numbers?
    float jitterRadiusTexelsXY = .3;   // e.g. 0.30
    float jitterRadiusTexelsZ  = .05;    // e.g. 0.05 (optional, tiny)
    float2 jitterUV = xyoffset[idx] * (jitterRadiusTexelsXY * invXY);
    // Apply
    DoubleUV.xy += jitterUV;
    DoubleUV.z += (noise - 0.5) * (jitterRadiusTexelsZ * invZ);


    #if (HiQSampling) //
    //Get's rid of stair-stepping from bilinear 
    float4 volsample = SampleVol_BicubicXY_LinearZ_8Tap_MN( DoubleUV, 0) ;
    #else    
    float4 volsample = SAMPLE_TEXTURE3D_LOD(_VolumetricResult, sampler_LinearClamp, DoubleUV, 0) ;
    #endif
       
    volsample = DitherVolumetrics(volsample, noise * 0.08 + .5);    
    return volsample ;
}


// half4 GetVolumetricColorJittered(float3 positionWS, float2 noise)
// {
//     //float2 positionNDC = ComputeNormalizedDeviceCoordinates(positionWS, _PrevViewProjMatrix);//viewProjMatrix
//
//     half4 ls = half4(positionWS - _VolCameraPos, -1); //_WorldSpaceCameraPos
//
//     ls = mul(ls, TransposedCameraProjectionMatrix);
//     ls.xyz = ls.xyz / ls.w;
//
//     float vdistance = distance(positionWS, _VolCameraPos);
//
//     // vdistance = LinearEyeDepth(vdistance, GetWorldToViewMatrix());
//
//     float W = EncodeLogarithmicDepthGeneralized(vdistance, _VBufferDistanceEncodingParams);
//
//     half halfU = ls.x * 0.5;
//     // half halfU = positionNDC.x * 0.5;
//
//      //Figuring out both sides at once and zeroing out the other when blending. 
//      //Is this better than branching with an if statement? Andorid doesn't like if statements anyway.
//     half3 LUV = half3 (halfU.x, ls.y, W) * (1 - unity_StereoEyeIndex); //Left UV
//     half3 RUV = half3(halfU + 0.5, ls.y, W) * (unity_StereoEyeIndex); //Right UV
//     half3 DoubleUV = LUV + RUV; // Combined
//     DoubleUV.xy += 0.25*(noise - 0.5) / _VolumetricResultDim.z; // don't jitter on Z since 3d textures are accessed like 2d arrays (i.e. only pixels in the same z layer are cached)
//
//                                                                 
//      //TODO: Make sampling calulations run or not if they are inside or out of the clipped area
//     //float ClipUVW =
//     //    step(DoubleUV.x, 1) * step(0, DoubleUV.x) *
//     //    step(DoubleUV.y, 1) * step(0, DoubleUV.y) ;
//
// //    float random = GenerateHashedRandomFloat(DoubleUV * 4000) * 0.003;
//
//     half4 volumetricColor = SAMPLE_TEXTURE3D_LOD(_VolumetricResult, sampler_linear_clamp, DoubleUV, 0);
//
//     return volumetricColor;
// }

half4 Volumetrics(half4 color, float3 positionWS) {

#if defined(_VOLUMETRICS_ENABLED)

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

#if defined(_VOLUMETRICS_ENABLED)

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




    //viewDirectionWS
}


#endif
