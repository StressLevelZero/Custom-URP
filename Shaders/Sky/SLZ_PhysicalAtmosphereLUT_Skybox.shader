Shader "SLZ/Skybox/SLZ Physical Atmosphere LUT"
{
    Properties
    {
        _Exposure("Exposure", Range(0, 8)) = 1.0

        [Header(Sun Disk)]
        _SunAngularRadiusDeg("Sun Angular Radius (deg)", Range(0.05, 1.0)) = 0.27
        _SunDiskBoost("Sun Disk Boost", Range(0, 10)) = 1.0
    }

    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
        Cull Off ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fragment _ _VOLUMETRICS_ENABLED

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/FullscreenSky.hlsl"
		    #include "Packages/com.unity.render-pipelines.universal/Shaders/Volumetrics/SLZ_AtmosphereShared.hlsl"

            float _Exposure;
            float _SunAngularRadiusDeg;
            float _SunDiskBoost;

            TEXTURE2D(_AtmosphereSkyViewLUT);   SAMPLER(sampler_AtmosphereSkyViewLUT);
            TEXTURE2D(_AtmosphereTransmittanceLUT); SAMPLER(sampler_AtmosphereTransmittanceLUT);

            // // Params pushed globally by the feature
            // float3 _Atmo_SunDir;        // point->sun
            // float3 _Atmo_SunColor;
            // float  _Atmo_PlanetRadius;
            // float  _Atmo_AtmosphereRadius;
            // float  _Atmo_CameraAltitude;

            half4 Volumetrics(half4 inColor, float3 wPos);

            struct appdata_t
            {
                uint vertexID : SV_VertexID;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos  : SV_POSITION;
                float3 wPos : TEXCOORD0;
                float3 cPos : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata_t v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                GetSkyVertexPos(v.vertexID, o.pos, o.wPos);
                o.cPos = o.wPos - _WorldSpaceCameraPos;
                return o;
            }

            float3 SampleSkyView(float3 viewDir, float3 sunDir)
            {
                float3 up = float3(0,1,0);
                float mu = dot(viewDir, up);
                float nu = dot(viewDir, sunDir);

                float2 uv = float2(nu * 0.5 + 0.5, mu * 0.5 + 0.5);
                return SAMPLE_TEXTURE2D_LOD(_AtmosphereSkyViewLUT, sampler_AtmosphereSkyViewLUT, uv, 0).rgb;
            }

            float3 SampleTransmittanceFromCamera(float3 viewDir)
            {
                float3 up = float3(0,1,0);
                float mu = dot(viewDir, up);
                float r = _PlanetRadius + _CameraAltitude;

                float h = saturate((r - _PlanetRadius) / max(_AtmosphereRadius - _PlanetRadius, 1.0));
                float u = saturate(mu * 0.5 + 0.5);
                float2 uv = float2(u, h);
                return SAMPLE_TEXTURE2D_LOD(_AtmosphereTransmittanceLUT, sampler_AtmosphereTransmittanceLUT, uv, 0).rgb;
            }

            half4 frag(v2f IN) : SV_Target
            {
                float3 viewDir = normalize(IN.cPos);
                float3 sunDir  = normalize(_SunDir);

                float3 col = SampleSkyView(viewDir, sunDir) * _Exposure;

                // Sun disk (cheap, separate). Attenuate by camera->space transmittance approx.
                float sunAngularRadius = radians(_SunAngularRadiusDeg);
                float cosSun = dot(viewDir, sunDir);
                float cosLimit = cos(sunAngularRadius);

                if (cosSun >= cosLimit)
                {
                    float disk = saturate((cosSun - cosLimit) / max(1e-4, (1.0 - cosLimit)));
                    disk = smoothstep(0.0, 1.0, disk);

                    float3 T = SampleTransmittanceFromCamera(viewDir);
                    col += _SunColor * T * (disk * _SunDiskBoost) * _Exposure;
                }

                half4 finalColor = Volumetrics(half4((half3)col, 1), IN.wPos);

                // dither (same matrix)
                const half ditherMatrix[4][4] =
                {
                    {   -0.5h,       0,  -0.375h,  0.125h},
                    {   0.25h,   -0.25h,   0.375h, -0.125h},
                    {-0.3125h,  0.1875h, -0.4375h, 0.0625h},
                    { 0.4375h, -0.0625h,  0.3125h,-0.1875h}
                };

                int2 dc = int2(fmod(IN.pos.xy, 4.0));
                half dither = ditherMatrix[dc.y][dc.x];
                finalColor.rgb += (1.0h / 255.0h) * dither;

                return finalColor;
            }
            ENDHLSL
        }
    }
    Fallback Off
}