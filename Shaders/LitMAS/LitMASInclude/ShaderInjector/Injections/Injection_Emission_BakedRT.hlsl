//#!INJECT_BEGIN UNIFORMS 0
Texture2D<float4> _BaseMap;
SamplerState sampler_BaseMap;
Texture2D<float4> _EmissionMap;
SamplerState sampler_EmissionMap;
//#!INJECT_END

//#!INJECT_BEGIN CLOSEST_HIT 0  
uint2 launchIdx = DispatchRaysIndex();

uint primitiveIndex = PrimitiveIndex();
uint3 triangleIndicies = UnityRayTracingFetchTriangleIndices(primitiveIndex);
Vertex v0, v1, v2;

float3 barycentrics = float3(1.0 - attributes.barycentrics.x - attributes.barycentrics.y, attributes.barycentrics.x, attributes.barycentrics.y);

// Fetch object-space vertex normals (must exist in mesh stream)
float3 n0OS = UnityRayTracingFetchVertexAttribute3(triangleIndicies.x, kVertexAttributeNormal);
float3 n1OS = UnityRayTracingFetchVertexAttribute3(triangleIndicies.y, kVertexAttributeNormal);
float3 n2OS = UnityRayTracingFetchVertexAttribute3(triangleIndicies.z, kVertexAttributeNormal);

float3 NshOS = normalize(n0OS * barycentrics.x + n1OS * barycentrics.y + n2OS * barycentrics.z);
float3 NshWS = normalize(TransformObjectToWorldNormal(NshOS));

float3 rayDirWS = WorldRayDirection();

if (dot(NshWS, -rayDirWS) <= 0.0f)
{
    payload.color = float4(0,0,0,1);
    return;
}

v0.texcoord = UnityRayTracingFetchVertexAttribute2(triangleIndicies.x, kVertexAttributeTexCoord0);
v1.texcoord = UnityRayTracingFetchVertexAttribute2(triangleIndicies.y, kVertexAttributeTexCoord0);
v2.texcoord = UnityRayTracingFetchVertexAttribute2(triangleIndicies.z, kVertexAttributeTexCoord0);
	
Vertex vInterpolated;
vInterpolated.texcoord = v0.texcoord * barycentrics.x + v1.texcoord * barycentrics.y + v2.texcoord * barycentrics.z;
float4 albedo = float4(_BaseMap.SampleLevel(sampler_BaseMap, vInterpolated.texcoord.xy * _BaseMap_ST.xy + _BaseMap_ST.zw, 0).rgb, 1) * _BaseColor;

float4 emission = _Emission * _EmissionMap.SampleLevel(sampler_EmissionMap, vInterpolated.texcoord * _BaseMap_ST.xy + _BaseMap_ST.zw, 0) * _EmissionColor;

emission.rgb *= lerp(albedo.rgb, 1, emission.a);
emission = max(emission * _BakedMutiplier,0);
payload.color.rgb = emission.rgb ;
//#!INJECT_END