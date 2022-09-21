#ifndef UNITY_DECLARE_ENCODE_NORMALS_TEXTURE_INCLUDED
#define UNITY_DECLARE_ENCODE_NORMALS_TEXTURE_INCLUDED
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"

half3 EncodeWSNormalForNormalsTex(half3 normalWS)
{
    half2 normal = PackNormalOctQuadEncode(normalWS);
    return half3(normal, 0.0h);
}

#endif
