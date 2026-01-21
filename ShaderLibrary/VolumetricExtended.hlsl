#ifndef VOLUMETRIC_SG_INCLUDED
#define VOLUMETRIC_SG_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/VolumetricCore.hlsl"

TEXTURE3D(InLightingTexture);

///Use this is to sample the pre-integrated volumetric lighting
void volumetricLighting_float(half3 positionWS, out float4 outputVolumetric)
{
        half4 ls = half4(positionWS - _VolCameraPos, -1); //_WorldSpaceCameraPos
        ls = mul(ls, TransposedCameraProjectionMatrix);
        ls.xyz = ls.xyz / ls.w;
        float vdistance = distance(positionWS, _VolCameraPos);
        float W = EncodeLogarithmicDepthGeneralized(vdistance, _VBufferDistanceEncodingParams);
        // Convert linear UV -> froxel grid UV (inverse of the warp used during froxel rendering)
        float2 uvGrid = LinearUV_To_FroxelGridUV(ls.xy, _FoveaCenterUV, _FoveaStrength); 
        //Sampling pre-integrated volume.  
        outputVolumetric = SAMPLE_TEXTURE3D_LOD(InLightingTexture, sampler_LinearClamp,float3 (uvGrid.xy,W) , 0);  
}

void volumetrics_additiveBlend_float(in half4 color, in float3 positionWS, out half4 outColor) {

       // #if defined(_VOLUMETRICS_ENABLED)

        half4 FroxelColor = GetVolumetricColor(positionWS);
       outColor.rgb = color.rgb * FroxelColor.a;
       outColor.a = FroxelColor.a;
       // // outColor.rgb = FroxelColor.rgb + (color.rgb * FroxelColor.a);
      //  outColor = FroxelColor;
       // #endif
      //  outColor = color;
}


#endif
