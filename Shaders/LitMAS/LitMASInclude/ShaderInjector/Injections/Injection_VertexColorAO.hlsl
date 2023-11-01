//#!INJECT_BEGIN VERTEX_IN 0
float4 color : COLOR;
//#!INJECT_END

//#!INJECT_BEGIN INTERPOLATORS 0
   float4 color : COLOR;
//#!INJECT_END

//#!INJECT_BEGIN VERTEX_END 0
	o.color = v.color;
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