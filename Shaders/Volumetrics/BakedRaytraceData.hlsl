#ifndef BAKED_RAYTRACE_DATA_INCLUDED
#define BAKED_RAYTRACE_DATA_INCLUDED
//Payload needs to be exact to match baker
struct RayPayload
{
    float4 color;        // emission (hit) | sky (miss) | shadow alpha
    float3 dir;          // ray dir in
    float3 hitPos;       // world-space hit
    float3 worldNormal;  // world-space normal at hit
    float3 albedo;       // diffuse albedo for indirect gather
    float  hitT;         // -1 = miss/sky, >0 = hit (backface or front)
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
#endif


