
//#!INJECT_BEGIN INTERPOLATORS 4
	////#!TEXCOORD half4 tanXYZ_ 1
//#!INJECT_END

//#!INJECT_BEGIN VERTEX_NORMALS 0
	//VertexNormalInputs ntb = GetVertexNormalInputs(v.normal, v.tangent);
	half3 wNorm = (TransformObjectToWorldNormal(v.normal));
	half3 wTan = (TransformObjectToWorldDir(v.tangent.xyz));
	half tanSign = v.tangent.w * GetOddNegativeScale();
	o.normXYZ_tanZ = half4(wNorm, wTan.z);
	o.uv0XY_tanXY.zw = wTan.xy;
	o.SHVertLights_btSign.w = tanSign;
//#!INJECT_END

//#!INJECT_BEGIN NORMAL_MAP 0
	normalMap = SAMPLE_TEXTURE2D(_BumpMap, sampler_BaseMap, uv_main);
	normalTS = UnpackNormal(normalMap);
	normalTS = _Normals ? normalTS : half3(0, 0, 1);
	geoSmooth = _Normals ? 1.0 - normalMap.b : 1.0;
	smoothness = saturate(smoothness + geoSmooth - 1.0);
//#!INJECT_END

//#!INJECT_BEGIN DETAIL_MAP 0
		half4 detailMap = SAMPLE_TEXTURE2D(_DetailMap, sampler_DetailMap, uv_detail);
		half3 detailTS = UnpackNormalAG(detailMap);
		normalTS = normalize(BlendNormalRNM(normalTS, detailTS));
//#!INJECT_END

//#!INJECT_BEGIN NORMAL_TRANSFORM 0
	half3 normalWS = UNPACK_NORMAL(i);
	half3 tangentWS = UNPACK_TANGENT(i);
	half3 bitangentWS = cross(normalWS, tangentWS) * UNPACK_BITANGENT_SIGN(i);
	
	half3x3 TStoWS = half3x3(
		tangentWS.x, bitangentWS.x, normalWS.x,
		tangentWS.y, bitangentWS.y, normalWS.y,
		tangentWS.z, bitangentWS.z, normalWS.z
		);
	normalWS = mul(TStoWS, normalTS);
	normalWS = normalize(normalWS);
//#!INJECT_END

//#!INJECT_BEGIN SPEC_AA 0
	#if !defined(SHADER_API_MOBILE) && !defined(LITMAS_FEATURE_TP) // Specular antialiasing based on normal derivatives. Only on PC to avoid cost of derivatives on Quest
		smoothness = min(smoothness, SLZGeometricSpecularAA(normalWS));
	#endif
//#!INJECT_END