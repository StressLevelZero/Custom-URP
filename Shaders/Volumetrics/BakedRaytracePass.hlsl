#ifndef BAKED_RAYTRACE_PASS_INCLUDED
#define BAKED_RAYTRACE_PASS_INCLUDED

#define SHADERPASS SHADERPASS_RAYTRACE
/// Simplified Pre-defined shader outputting UVs to inject albedo and emission

#include "UnityRaytracingMeshUtils.cginc"
#pragma raytracing BakeHit

#include "Packages/com.unity.render-pipelines.universal/Shaders/Volumetrics/BakedRaytraceData.hlsl"
// Geometry sample at the hit point. Fields are in their final spaces:
// uv is texcoord0 interpolated, worldPos/worldNormal are world-space.
struct SurfaceHit
{
    float2 uv;
    float3 worldPos;
    float3 worldNormal;
    float  hitT;
};

//// TEMPLATE ////
// Contract: material file must define this before including this pass.
//   albedo   :  RGB diffuse reflectance (0..1, unbounded for HDR if you want)
//   emission :  RGB radiance to write to payload.color.rgb (already scaled,
//              already clamped non-negative, already tinted however the material wants)
//void EvaluateMaterial(float2 hitUV, out float3 albedo, out float3 emission );

// #define MATERIAL_PROVIDES_EVALUATE
// void EvaluateMaterial(float2 hitUV, out float3 albedo, out float3 emission)
// {
//     float2 uv = hitUV * _BaseMap_ST.xy + _BaseMap_ST.zw;
//     float3 baseRGB = _BaseMap.SampleLevel(sampler_BaseMap, uv, 0).rgb * _BaseColor.rgb;
//     albedo = baseRGB;
//     float4 em = _Emission * _EmissionMap.SampleLevel(sampler_EmissionMap, uv, 0) * _EmissionColor;
//     em.rgb  *= lerp(baseRGB, 1.0, em.a);   // alpha = how "untinted" the emission reads
//     emission = em.rgb * _BakedMutiplier;
// }

// In BakedRaytracePass.hlsl, before the closest-hit:

#ifdef MATERIAL_PROVIDES_EVALUATE
// Forward-declare the user's hook. 
void EvaluateMaterial(float2 hit, out float3 albedo, out float3 emission);

void SampleSurface(float2 hit, out float3 albedo, out float3 emission)
{
    EvaluateMaterial(hit, albedo, emission);
}
#else
#ifdef VOLBAKE_STRICT_MATERIAL
#error "BakedRaytracePass: material did not define EvaluateMaterial..."
#endif

void SampleSurface(float2 hit, out float3 albedo, out float3 emission)
{
    albedo   = float3(.5, .5, .5);   // missing-material 
    emission = float3(0, 0, 0);
}
#endif

[shader("closesthit")]
void MyClosestHit(inout RayPayload payload, AttributeData attributes : SV_IntersectionAttributes)
{
    payload.color = float4(0, 0, 0, 1);
    payload.dir   = float3(1, 0, 0);

    // Backface: occluded, no contribution. Single-sided by design here;
    // promote to a material-side decision later if you need _DOUBLE_SIDED_EMISSION.
    if (HitKind() == HIT_KIND_TRIANGLE_BACK_FACE)
        return;

    // ---- Geometry fetch ----
    uint  primitiveIndex = PrimitiveIndex();
    uint3 tri            = UnityRayTracingFetchTriangleIndices(primitiveIndex);

    float3 bary = float3(1.0 - attributes.barycentrics.x - attributes.barycentrics.y,
                         attributes.barycentrics.x,
                         attributes.barycentrics.y);

    float2 uv0 = UnityRayTracingFetchVertexAttribute2(tri.x, kVertexAttributeTexCoord0);
    float2 uv1 = UnityRayTracingFetchVertexAttribute2(tri.y, kVertexAttributeTexCoord0);
    float2 uv2 = UnityRayTracingFetchVertexAttribute2(tri.z, kVertexAttributeTexCoord0);

    float3 n0 = UnityRayTracingFetchVertexAttribute3(tri.x, kVertexAttributeNormal);
    float3 n1 = UnityRayTracingFetchVertexAttribute3(tri.y, kVertexAttributeNormal);
    float3 n2 = UnityRayTracingFetchVertexAttribute3(tri.z, kVertexAttributeNormal);

    SurfaceHit hit;
    hit.uv          = uv0 * bary.x + uv1 * bary.y + uv2 * bary.z;
    hit.hitT        = RayTCurrent();
    hit.worldPos    = WorldRayOrigin() + hit.hitT * WorldRayDirection();
    float3 nOS      = normalize(n0 * bary.x + n1 * bary.y + n2 * bary.z);
    hit.worldNormal = normalize(mul((float3x3)ObjectToWorld3x4(), nOS));

    // ---- Material injection ----
    float3 albedo   = 0.5;
    float3 emission = 0;
    SampleSurface(hit.uv , albedo, emission);

    // ---- Payload write ----
    payload.hitT        = hit.hitT;
    payload.hitPos      = hit.worldPos;
    payload.worldNormal = hit.worldNormal;
    payload.albedo      = max(albedo, 0);
    payload.color.rgb   = max(emission, 0);
}

#endif // BAKED_RAYTRACE_PASS_INCLUDED