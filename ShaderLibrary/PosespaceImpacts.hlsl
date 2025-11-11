#ifndef POSESPACE_INCLUDED
#define POSESPACE_INCLUDED

#define HitArrayCount 32
#define HitMatrixRowCount HitArrayCount * 3

#define UNITY_ANDROID SHADER_API_MOBILE

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

TEXTURE2D(_HitRamp); //SAMPLER(sampler_HitRamp);

//Forcing a certain sampler state
#if UNITY_ANDROID 
#define sampler_hitsettings sampler_HitRamp_ClampU_RepeatV_Trilinear_Aniso4 
#else
#define sampler_hitsettings sampler_HitRamp_ClampU_RepeatV_Trilinear_Aniso16
#endif

//Define sampler 
SamplerState sampler_hitsettings; 
#define SAMPLE_HIT_RAMP(tex, uv, du, dv) tex.SampleGrad(sampler_hitsettings, uv, du, dv) 


inline half2 GetClosestImpactUV( half3 Posespace, half4x4 EllipsoidPosArray[HitMatrixCount], uint NumberOfHits )
{

#if UNITY_ANDROID
    half HitDistance = 1;
    half3 closestHit = half3(1,1,1);
#else
    // Initialize accumulators for weighted sums
    float totalWeight = 0;
    float weightedHitRadial = 0;
    float weightedHitDistance = 0;
#endif
    UNITY_LOOP for (uint i = 0; i < NumberOfHits; i++)
    {
        #if defined(PACKED_HITPOS)
        // unpacking
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

        // Transform the position into the local space of the hit
        half3 LocalPosP = Posespace - half3(EllipsoidPos[0][3], EllipsoidPos[1][3], EllipsoidPos[2][3]);
        half3 localspace = mul(LocalPosP, (half3x3)EllipsoidPos).xyz;

        // Compute the distance from the current position to the hit point
		half currentdist = saturate(length(localspace));

#if UNITY_ANDROID
        
        closestHit = currentdist < HitDistance ? localspace : closestHit;
        HitDistance =  min( HitDistance, currentdist );
    }
    half HitRadial = FastAtan2(closestHit.x, closestHit.y) * INV_PI;
    return half2(HitDistance,HitRadial);
    
#else
        
        ////
        // Calculate the radial texture coordinate for the current hit //atan2
        half HitRadial = FastAtan2(localspace.x, localspace.y) * INV_PI;

            // // Compute the weight based on the distance (using exponential falloff)
            // const half scale = 33.0; // Adjust this value to control the blending range
            // half weight = exp(-currentdist * scale);
            // Compute the weight
            float weight = saturate( 1.0 - currentdist );
            weight = pow(weight, 25);

            // Accumulate the weighted contributions
            totalWeight += weight;
            weightedHitRadial += weight * HitRadial;
            weightedHitDistance += weight * currentdist;
        }

        // Ensure we don't divide by zero
        if (totalWeight > 0)
        {
            // Normalize the accumulated sums to get the final blended values
            weightedHitRadial /= totalWeight;
            weightedHitDistance /= totalWeight;
        }
        else
        {
            // Default values if no hits contribute
            weightedHitRadial = 0;
            weightedHitDistance = 1;
        }
            
        weightedHitDistance = saturate(weightedHitDistance);
        return half2(weightedHitDistance, weightedHitRadial);

#endif   
    
}

float2 AdjustDerivativeForWrapping(float2 derivative) {
    return derivative - round(derivative);
}

inline half4 SampleHitTexture(half2 ImpactsUV){
    float2 uv = ImpactsUV;
    float2 du = AdjustDerivativeForWrapping(ddx(uv));
    float2 dv = AdjustDerivativeForWrapping(ddy(uv));
    return SAMPLE_HIT_RAMP(_HitRamp, uv, du, dv);
}

#endif