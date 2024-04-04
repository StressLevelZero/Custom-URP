//#!INJECT_BEGIN VERTEX_IN 0
	float4 tangent : TANGENT;
	//#!TEXCOORD float2 uv0 0
//#!INJECT_END

//#!INJECT_BEGIN UNIFORMS 0
	TEXTURE2D(_BumpMap);
	SAMPLER(sampler_BumpMap);
//#!INJECT_END

//#!INJECT_BEGIN INTERPOLATORS 4
	//#!TEXCOORD float4 tanXYZ_btSign 1
	//#!TEXCOORD float2 uv0XY 1
//#!INJECT_END

//#!INJECT_BEGIN VERTEX_NORMAL 0
	half3 wNorm = (TransformObjectToWorldNormal(v.normal));
	half3 wTan = (TransformObjectToWorldDir(v.tangent.xyz));
	half tanSign = v.tangent.w * GetOddNegativeScale();
	o.normalWS = float4(wNorm, 1);
	o.tanXYZ_btSign = float4(wTan, tanSign);
	o.uv0XY.xy = TRANSFORM_TEX(v.uv0, _BaseMap);
//#!INJECT_END

//#!INJECT_BEGIN FRAG_NORMALS 0
	half4 normalMap = SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, i.uv0XY.xy);
	half3 normalTS = UnpackNormal(normalMap);
	normalTS = _Normals ? normalTS : half3(0, 0, 1);

	half3 normalWS = i.normalWS.xyz;
	half3 tangentWS = i.tanXYZ_btSign.xyz;
	half3 bitangentWS = cross(normalWS, tangentWS) * i.tanXYZ_btSign.w;
	half3x3 TStoWS = half3x3(
		tangentWS.x, bitangentWS.x, normalWS.x,
		tangentWS.y, bitangentWS.y, normalWS.y,
		tangentWS.z, bitangentWS.z, normalWS.z
		);
	normalWS = mul(TStoWS, normalTS);
	normalWS = normalize(normalWS);

	normals = half4(EncodeWSNormalForNormalsTex(normalWS),0);
//#!INJECT_END
