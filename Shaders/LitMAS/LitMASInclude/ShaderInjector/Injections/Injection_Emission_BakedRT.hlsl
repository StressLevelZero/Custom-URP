//#!INJECT_BEGIN UNIFORMS 0
Texture2D<float4> _BaseMap;
SamplerState sampler_BaseMap;
Texture2D<float4> _EmissionMap;
SamplerState sampler_EmissionMap;
//#!INJECT_END

//#!INJECT_BEGIN CLOSEST_HIT 0
albedo = float4(_BaseMap.SampleLevel(sampler_BaseMap, interpData.vertex.texcoord.xy * _BaseMap_ST.xy + _BaseMap_ST.zw, 0).rgb, 1) * _BaseColor;
emission = _Emission * _EmissionMap.SampleLevel(sampler_EmissionMap, interpData.vertex.texcoord * _BaseMap_ST.xy + _BaseMap_ST.zw, 0) * _EmissionColor;
emission.rgb *= lerp(albedo.rgb, 1, emission.a);
emission = max(emission * _BakedMutiplier,0);
//#!INJECT_END