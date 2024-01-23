#ifndef POSESPACE_INCLUDED
#define POSESPACE_INCLUDED

#define HitArrayCount 32
#define HitMatrixRowCount HitArrayCount * 3

//#define PACKED_HITPOS

#if defined(PACKED_HITPOS)
    #define HitMatrixCount (HitMatrixRowCount) / 4  // (32 * 3) / 4 = 24
#else
    #define HitMatrixCount HitArrayCount
#endif


#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Macros.hlsl"

//Unity REQUIRES you to use the UnityPerMaterial cbuffer for batching. Seems like this could be done better.
// CBUFFER_START(hitBuffer)
// float4x4 EllipsoidPosArray[HitArrayCount];
// int _NumberOfHits;
// CBUFFER_END

TEXTURE2D(_HitRamp); SAMPLER(sampler_HitRamp);

inline half2 GetClosestImpactUV( half3 Posespace, half4x4 EllipsoidPosArray[HitMatrixCount], uint NumberOfHits )
{
    half HitDistance = 1;
    half3 closestHit = half3(0,0,0);
    uint minHits = min(NumberOfHits, HitArrayCount);
    for ( uint i = 0; i < minHits; i++ ) {
        //TODO: Unpack half3x4 from 4x4 array. This works, but just use 4x4 array for now since I don't have time to validate this
#if defined(PACKED_HITPOS)
        uint2 row1Coords = uint2((i * 3u) >> 2, (i * 3u) & 3u);
        half4 row1 = half4(
            EllipsoidPosArray[row1Coords.x][0][row1Coords.y],
            EllipsoidPosArray[row1Coords.x][1][row1Coords.y],
            EllipsoidPosArray[row1Coords.x][2][row1Coords.y],
            EllipsoidPosArray[row1Coords.x][3][row1Coords.y]
        );
        uint2 row2Coords = uint2((i * 3u + 1u) >> 2, (i * 3u + 1u) & 3u);
        half4 row2 = half4(
            EllipsoidPosArray[row2Coords.x][0][row2Coords.y],
            EllipsoidPosArray[row2Coords.x][1][row2Coords.y],
            EllipsoidPosArray[row2Coords.x][2][row2Coords.y],
            EllipsoidPosArray[row2Coords.x][3][row2Coords.y]
        );
        uint2 row3Coords = uint2((i * 3u + 2u) >> 2, (i * 3u + 2u) & 3u);
        half4 row3 = half4(
            EllipsoidPosArray[row3Coords.x][0][row3Coords.y],
            EllipsoidPosArray[row3Coords.x][1][row3Coords.y],
            EllipsoidPosArray[row3Coords.x][2][row3Coords.y],
            EllipsoidPosArray[row3Coords.x][3][row3Coords.y]
        );
        
        half3x4 EllipsoidPos = half3x4(row1,row2,row3); 
#else
        half4x4 EllipsoidPos = EllipsoidPosArray[i]; 
#endif
        half3 LocalPosP = half3(Posespace - half3(EllipsoidPos[0][3],EllipsoidPos[1][3],EllipsoidPos[2][3]));
        half3 localspace = mul( LocalPosP , (half3x3)EllipsoidPos ).xyz;
        half3 currentdist = saturate(  length( localspace));
        closestHit = currentdist < HitDistance ? localspace : closestHit;
        HitDistance =  min( HitDistance, currentdist );
    }
    half HitRadial = atan2(closestHit.x, closestHit.y) * INV_PI;
    return half2(HitDistance,HitRadial);
}


inline half4 SampleHitTexture(half2 ImpactsUV){
    return _HitRamp.SampleLevel(sampler_HitRamp,ImpactsUV,0);
}

#endif