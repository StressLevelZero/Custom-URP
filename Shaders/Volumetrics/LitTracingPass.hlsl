#ifndef RAYTRACING_META_PASS
#define RAYTRACING_META_PASS

#include "UnityRayTracingMeshUtils.cginc"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
//#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SurfaceInput.hlsl"
#include "Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl"

#if !defined(DYNAMIC_EMISSION)
    #if !defined(_EMISSION)
        #define _EMISSION false
    #else
        #undef _EMISSION
        #define _EMISSION true
    #endif
#endif

struct RayPayload
{
    float4 color;
    float3 dir;
};

struct AttributeData
{
    float2 barycentrics;
}; 

struct Vertex
{
    float2 texcoord;
    float3 normal;
};


// Texture2D<float4> _MainTex;
// SamplerState sampler_MainTex;

Vertex FetchVertex(uint vertexIndex)
{ 
    Vertex v;
    v.normal = UnityRayTracingFetchVertexAttribute3(vertexIndex, kVertexAttributeNormal);
    return v;
}

Vertex InterpolateVertices(Vertex v0, Vertex v1, Vertex v2, float3 barycentrics)
{
    Vertex v;
#define INTERPOLATE_ATTRIBUTE(attr) v.attr = v0.attr * barycentrics.x + v1.attr * barycentrics.y + v2.attr * barycentrics.z
    INTERPOLATE_ATTRIBUTE(normal);
    return v;
}


//https://coty.tips/raytracing-in-unity/
[shader("closesthit")]
void BakedClosestHit(inout RayPayload payload,
    AttributeData attributes : SV_IntersectionAttributes) 
{
    if (_EMISSION)
    {
        uint2 launchIdx = DispatchRaysIndex();
        //    ShadingData shade = getShadingData( PrimitiveIndex(), attribs );

        uint primitiveIndex = PrimitiveIndex();
        uint3 triangleIndicies = UnityRayTracingFetchTriangleIndices(primitiveIndex);
        Vertex v0, v1, v2;

        v0.texcoord = UnityRayTracingFetchVertexAttribute2(triangleIndicies.x, kVertexAttributeTexCoord0);
        v1.texcoord = UnityRayTracingFetchVertexAttribute2(triangleIndicies.y, kVertexAttributeTexCoord0);
        v2.texcoord = UnityRayTracingFetchVertexAttribute2(triangleIndicies.z, kVertexAttributeTexCoord0);

        float3 barycentrics = float3(1.0 - attributes.barycentrics.x - attributes.barycentrics.y, attributes.barycentrics.x, attributes.barycentrics.y);

        Vertex vInterpolated;
        vInterpolated.texcoord = v0.texcoord * barycentrics.x + v1.texcoord * barycentrics.y + v2.texcoord * barycentrics.z;

        payload.color = float4(_EmissionMap.SampleLevel(sampler_EmissionMap, vInterpolated.texcoord, 0).rgb * _EmissionColor.rgb, 1);
        payload.dir = float3(1, 0, 0);
    }
    else
    {
        payload.color = float4(0, 0, 0, 1);
        payload.dir = float3(1, 0, 0);
    }

}


#endif
