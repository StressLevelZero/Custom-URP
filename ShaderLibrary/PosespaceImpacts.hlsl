#ifndef POSESPACE_INCLUDED
#define POSESPACE_INCLUDED

#define HitArrayCount 32
#define HitMatrixRowCount HitArrayCount * 3

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Macros.hlsl"

//Unity REQUIRES you to use the UnityPerMaterial cbuffer for batching. Seems like this could be done better.
// CBUFFER_START(hitBuffer)
// float4x4 EllipsoidPosArray[HitArrayCount];
// int _NumberOfHits;
// CBUFFER_END

TEXTURE2D(_HitRamp); SAMPLER(sampler_HitRamp);

inline half2 GetClosestImpactUV( half3 Posespace, half4 EllipsoidPosArray[HitMatrixRowCount], int NumberOfHits )
{
    half HitDistance = 1;
    half3 closestHit = half3(0,0,0);
    UNITY_LOOP for ( int i = 0; i < NumberOfHits; i++ ){
        half3x4 EllipsoidPos = half3x4(EllipsoidPosArray[3*i],EllipsoidPosArray[3*i+1],EllipsoidPosArray[3*i+2]); 
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