void GetSkyVertexPos(in uint vertexID, out float4 clipPos, out float3 worldPos)
{
	float4 clipQuad = GetQuadVertexPosition(vertexID, UNITY_RAW_FAR_CLIP_VALUE);
	clipQuad.xy = 4.0f * clipQuad.xy - 1.0f;
	float4 wPos = mul(UNITY_MATRIX_I_VP, clipQuad);
	worldPos = wPos.xyz / wPos.w;
	clipPos = clipQuad;
}