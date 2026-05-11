#ifndef BAKED_RAYTRACE_PARTS_INCLUDED
#define BAKED_RAYTRACE_PARTS_INCLUDED

/// Helper functions that do most of the raytracing work

#include "UnityRaytracingMeshUtils.cginc"
#include "Packages/com.unity.render-pipelines.universal/Shaders/Volumetrics/BakedRaytraceData.hlsl"

struct InterpolationData
{
    Vertex vertex;
    uint3 indicies;
    float3 barycentric;
};

// Early-out helper. Macro, not a function: must be able to `return` from the
// closest-hit shader itself. Writes the "occluded, no contribution" payload.
#define EARLY_OUT_ON_BACKFACE(payload)                                  \
if (HitKind() == HIT_KIND_TRIANGLE_BACK_FACE) { (payload).color = float4(0, 0, 0, 1); return; }; \


void InterpolatedSurface(AttributeData attributes, out InterpolationData interpData, inout RayPayload payload)
{
    payload.color = float4(0,0,0,1); //Initializing
    payload.dir = float3(1,0,0);

   // uint2 launchIdx = DispatchRaysIndex();
    uint primitiveIndex = PrimitiveIndex();
    interpData.indicies = UnityRayTracingFetchTriangleIndices(primitiveIndex);
    Vertex v0, v1, v2;

    float3 bary = float3(1.0 - attributes.barycentrics.x - attributes.barycentrics.y,
                         attributes.barycentrics.x, attributes.barycentrics.y);
	
    v0.texcoord = UnityRayTracingFetchVertexAttribute2(interpData.indicies.x, kVertexAttributeTexCoord0);
    v1.texcoord = UnityRayTracingFetchVertexAttribute2(interpData.indicies.y, kVertexAttributeTexCoord0);
    v2.texcoord = UnityRayTracingFetchVertexAttribute2(interpData.indicies.z, kVertexAttributeTexCoord0);
		
    float3 n0 = UnityRayTracingFetchVertexAttribute3(interpData.indicies.x, kVertexAttributeNormal);
    float3 n1 = UnityRayTracingFetchVertexAttribute3(interpData.indicies.y, kVertexAttributeNormal);
    float3 n2 = UnityRayTracingFetchVertexAttribute3(interpData.indicies.z, kVertexAttributeNormal);
    float3 nOS = normalize(n0*bary.x + n1*bary.y + n2*bary.z);

    interpData.vertex.texcoord = v0.texcoord * bary.x + v1.texcoord * bary.y + v2.texcoord * bary.z;
    interpData.barycentric = bary;

    // ---- Payload write ----
    payload.worldNormal = normalize(mul((float3x3)ObjectToWorld3x4(), nOS));
}

void WritePayloads(inout RayPayload payload, float3 albedo, float3 emission )
{
    payload.hitT       = RayTCurrent();
    payload.hitPos     = WorldRayOrigin() + RayTCurrent() * WorldRayDirection();
    
    payload.albedo = albedo;
    payload.color.rgb = emission;
}

#endif
