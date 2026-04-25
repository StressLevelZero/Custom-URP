//#!INJECT_BEGIN UNIFORMS 0
TEXTURE2D(_EmissionMap);
//#!INJECT_END

//#!INJECT_BEGIN EMISSION 10
	UNITY_BRANCH if (_Emission)
	{
		emission += SAMPLE_TEXTURE2D(_EmissionMap, sampler_BaseMap, uv_main) * _EmissionColor;
		emission.rgb *= lerp(albedo.rgb, half3(1, 1, 1), emission.a);
		half emNoV = _EmissionFalloff >= half(0) ? abs(fragData.NoV) : half(1.0) - abs(fragData.NoV);
		emission.rgb *= saturate(pow(emNoV, abs(_EmissionFalloff)));
		emission = max(emission,half(0));
	}
//#!INJECT_END