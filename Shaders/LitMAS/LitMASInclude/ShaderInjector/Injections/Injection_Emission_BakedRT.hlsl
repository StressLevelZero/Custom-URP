//#!INJECT_BEGIN UNIFORMS 0
Texture2D<float4> _BaseMap;
SamplerState sampler_BaseMap;
Texture2D<float4> _EmissionMap;
SamplerState sampler_EmissionMap;
//#!INJECT_END

//#!INJECT_BEGIN CLOSEST_HIT 0

//#ifndef _DOUBLE_SIDED_EMISSION
// Backface: occluded but no emission. Ray is done.
if (HitKind() == HIT_KIND_TRIANGLE_BACK_FACE)
{
	payload.color = float4(0, 0, 0, 1);
	return;
}
//#endif

uint2 launchIdx = DispatchRaysIndex();

uint primitiveIndex = PrimitiveIndex();
uint3 triangleIndicies = UnityRayTracingFetchTriangleIndices(primitiveIndex);
Vertex v0, v1, v2;

//uint3 tri = UnityRayTracingFetchTriangleIndices(PrimitiveIndex());
//float3 n0 = UnityRayTracingFetchVertexAttribute3(tri.x, kVertexAttributeNormal);
//float3 n1 = UnityRayTracingFetchVertexAttribute3(tri.y, kVertexAttributeNormal);
//float3 n2 = UnityRayTracingFetchVertexAttribute3(tri.z, kVertexAttributeNormal);

float3 bary = float3(1.0 - attributes.barycentrics.x - attributes.barycentrics.y,
                     attributes.barycentrics.x, attributes.barycentrics.y);
//float3 nOS = normalize(n0 * bary.x + n1 * bary.y + n2 * bary.z);
//float3 nWS = normalize(TransformObjectToWorldNormal(nOS));
	
//#ifndef _DOUBLE_SIDED_EMISSION
// Backface: occluded but no emission. Ray is done.
if (HitKind() == HIT_KIND_TRIANGLE_BACK_FACE)
{
    payload.color = float4(0, 0, 0, 1);
    return;
}
//#endif

v0.texcoord = UnityRayTracingFetchVertexAttribute2(triangleIndicies.x, kVertexAttributeTexCoord0);
v1.texcoord = UnityRayTracingFetchVertexAttribute2(triangleIndicies.y, kVertexAttributeTexCoord0);
v2.texcoord = UnityRayTracingFetchVertexAttribute2(triangleIndicies.z, kVertexAttributeTexCoord0);
		
Vertex vInterpolated;
vInterpolated.texcoord = v0.texcoord * bary.x + v1.texcoord * bary.y + v2.texcoord * bary.z;
float4 albedo = float4(_BaseMap.SampleLevel(sampler_BaseMap, vInterpolated.texcoord.xy * _BaseMap_ST.xy + _BaseMap_ST.zw, 0).rgb, 1) * _BaseColor;

float4 emission = _Emission * _EmissionMap.SampleLevel(sampler_EmissionMap, vInterpolated.texcoord * _BaseMap_ST.xy + _BaseMap_ST.zw, 0) * _EmissionColor;

emission.rgb *= lerp(albedo.rgb, 1, emission.a);
emission = max(emission * _BakedMutiplier,0);
payload.color.rgb = emission.rgb ;
//#!INJECT_END