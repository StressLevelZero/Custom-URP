//#!INJECT_BEGIN VERTEX_IN 0
float4 color : COLOR;
//#!INJECT_END

//#!INJECT_BEGIN INTERPOLATORS 0
   float4 color : COLOR;
//#!INJECT_END

//#!INJECT_BEGIN VERTEX_END 0
	o.color = v.color;
//#!INJECT_END

//#!INJECT_BEGIN FRAG_READ_INPUTS 0
	// Need custom FRAG_READ_INPUTS so albedo.a doesn't get set to 1.0
	float2 uv0 = UNPACK_UV0(i);
	float2 uv_main = mad(uv0, _BaseMap_ST.xy, _BaseMap_ST.zw);
	float2 uv_detail = mad(uv0, _DetailMap_ST.xy, _DetailMap_ST.zw);
	half4 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv_main);
	half4 mas = SAMPLE_TEXTURE2D(_MetallicGlossMap, sampler_BaseMap, uv_main);
//#!INJECT_END

//#!INJECT_BEGIN PBR_VALUES 0
	albedo *= lerp(1, _BaseColor, albedo.a);
	half metallic = mas.r;
	half ao = mas.g;
	half smoothness = mas.b;
//#!INJECT_END

//#!INJECT_BEGIN PRE_FRAGDATA 0
	ao *= i.color.a;
	albedo.rgb *= i.color.rgb;
//#!INJECT_END