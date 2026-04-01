Shader "SLZ/Skybox/SLZ Physical Atmosphere"
{
    Properties
    {
        [Header(Planet , Atmosphere (meters))]
        _PlanetRadius("Planet Radius (m)", Float) = 6360000.
        _AtmosphereHeight("Atmosphere Height (m)", Float) = 80000.
        _CameraAltitude("Camera Altitude (m)", Float) = 2.

        [Header(Density Falloff)]
        _RayleighScaleHeight("Rayleigh Scale Height (m)", Float) = 8000.
        _MieScaleHeight("Mie Scale Height (m)", Float) = 1200.

        [Header(Scattering Coefficients (1 div m))]
        _BetaRayleigh("Beta Rayleigh (1 div m)", Vector) = (.0000058, .0000135, .0000331, 0)
        _BetaMie("Beta Mie (1 div m)", Vector) = (.000021, .000021, .000021, 0)

        [Header(Mie Phase)]
        _MieG("Mie g", Range(0, 0.999)) = 0.8

        [Header(Art , Exposure)]
        _AtmosphereThickness("Atmosphere Thickness", Range(0,5)) = 1
        [HDR]_GroundAlbedo("Ground Albedo", Color) = (0.369, 0.349, 0.341, 1)
        _GroundIntensity("Ground Intensity", Range(0, 4)) = 1
        _SunIntensity("Sun Intensity", Range(0, 50)) = 20
        _Exposure("Exposure", Range(0, 8)) = 1.0

        [Header(Sun Disk)]
        _SunAngularRadiusDeg("Sun Angular Radius (deg)", Range(0.05, 1.0)) = 0.27
        _SunDiskBoost("Sun Disk Boost", Range(0, 10)) = 1.0
    }

    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
        Cull Off ZWrite Off
       // Offset 0.0008148, 0

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            // quality keyword: set one on the skybox material
            #pragma multi_compile_local_fragment _ ATMOS_QUALITY_HIGH

            #pragma multi_compile_fragment _ _VOLUMETRICS_ENABLED_HQ _VOLUMETRICS_ENABLED

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            //#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/FullscreenSky.hlsl"

            // ---- Tunables ----
            #if defined(ATMOS_QUALITY_HIGH)
                #define VIEW_SAMPLES 16
                #define SUN_SAMPLES  8
            #else
                #define VIEW_SAMPLES 8
                #define SUN_SAMPLES  4
            #endif
            
            half4 _LightColorSun;
            half4 _WorldSpaceLightPosSun;

            // ---- Properties ----
            float _PlanetRadius;
            float _AtmosphereHeight;
            float _CameraAltitude;

            float _RayleighScaleHeight;
            float _MieScaleHeight;

            float4 _BetaRayleigh;
            float4 _BetaMie;
            float _MieG;

            float _AtmosphereThickness;
            float4 _GroundAlbedo;
            float _GroundIntensity;
            float _SunIntensity;
            float _Exposure;

            float _SunAngularRadiusDeg;
            float _SunDiskBoost;

            

            // Your project hook (same signature you already use)
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

            // ---- Math helpers ----

            float2 RaySphereIntersect(float3 o, float3 d, float r)
            {
                // |o + t d|^2 = r^2
                float b = dot(o, d);
                float c = dot(o, o) - r * r;
                float h = b * b - c;
                if (h < 0.0) return float2(-1.0, -1.0);
                h = sqrt(h);
                return float2(-b - h, -b + h); // near, far
            }

            bool RaySphereNearestPositive(float3 o, float3 d, float r, out float tHit)
            {
                float2 t = RaySphereIntersect(o, d, r);
                tHit = 1e20;
                if (t.x > 0.0) tHit = t.x;
                else if (t.y > 0.0) tHit = t.y;
                return (tHit < 1e19);
            }

            float RaySphereExit(float3 o, float3 d, float r)
            {
                // Assumes it intersects; returns far root (exit distance).
                float2 t = RaySphereIntersect(o, d, r);
                return t.y;
            }

            float RayleighPhase(float cosTheta)
            {
                // 3/(16*pi) * (1 + cos^2)
                const float inv4pi = 0.07957747154594767; // 1/(4*pi)
                return 0.75 * inv4pi * (1.0 + cosTheta * cosTheta);
            }

            float HGPhase(float cosTheta, float g)
            {
                // 1/(4*pi) * (1-g^2) / (1+g^2-2gcos)^(3/2)
                const float inv4pi = 0.07957747154594767;
                float g2 = g * g;
                float denom = pow(max(1.0 + g2 - 2.0 * g * cosTheta, 1e-4), 1.5);
                return inv4pi * (1.0 - g2) / denom;
            }

            void GetDensities(float heightMeters, out float densR, out float densM)
            {
                // height above surface, clamp below 0
                float h = max(0.0, heightMeters);
                densR = exp(-h / max(_RayleighScaleHeight, 1.0));
                densM = exp(-h / max(_MieScaleHeight, 1.0));
            }

            float3 ComputeSunTransmittance(float3 p, float3 sunDir, float planetR, float atmoR, float3 betaR, float3 betaM)
            {
                // Integrate optical depth from p toward sun until leaving atmosphere
                float tExit = RaySphereExit(p, sunDir, atmoR);
                float ds = tExit / (float)SUN_SAMPLES;

                float odR = 0.0;
                float odM = 0.0;

                float t = 0.5 * ds;
                [unroll] for (int i = 0; i < SUN_SAMPLES; i++)
                {
                    float3 sp = p + sunDir * t;
                    float height = length(sp) - planetR;

                    float dR, dM;
                    GetDensities(height, dR, dM);

                    odR += dR * ds;
                    odM += dM * ds;

                    t += ds;
                }

                float3 tau = betaR * odR + betaM * odM;
                return exp(-tau);
            }

            half4 frag(v2f IN) : SV_Target
            {
                // ---- Light (URP main light) ----
                // Light mainLight = GetMainLight();
                //  float3 sunDir = normalize(mainLight.direction);   // point -> sun
                //  float3 sunCol = (float3)mainLight.color * _SunIntensity;

                //Need to do it this way so it renders in cubemaps using nothing culling mask 
                float3 sunDir =_WorldSpaceLightPosSun.xyz;   // point -> sun
                float3 sunCol = _LightColorSun * _SunIntensity;
                
                // ---- “Planet space” anchored to camera for stability ----
                float planetR = _PlanetRadius;
                float atmoR   = _PlanetRadius + _AtmosphereHeight;

                float3 cam = float3(0.0, planetR + _CameraAltitude, 0.0);
                float3 viewDir = normalize(IN.cPos); // world direction, but we treat +Y as up (planet radial)

                // ---- Intersections ----
                float tExitAtmo = RaySphereExit(cam, viewDir, atmoR);

                float tGround;
                bool hitsGround = RaySphereNearestPositive(cam, viewDir, planetR, tGround);

                float tMax = tExitAtmo;
                if (hitsGround && tGround < tExitAtmo)
                    tMax = tGround;

                // If ray misses atmosphere entirely (shouldn't from inside), return black
                if (tMax <= 0.0)
                    return half4(0,0,0,1);

                // ---- Thickness mapping ----
                // Thickness scales scattering coefficients (simple but effective control)
                float thickness01 = saturate(_AtmosphereThickness / 5.0);
                float thickMul = lerp(0.25, 2.5, thickness01); // tune if desired

                float3 betaR = _BetaRayleigh.xyz * thickMul;
                float3 betaM = _BetaMie.xyz      * thickMul;

                // ---- Integrate single scattering along view ray ----
                float ds = tMax / (float)VIEW_SAMPLES;

                float odR = 0.0;
                float odM = 0.0;

                float3 L = 0.0;

                float t = 0.5 * ds;
                [unroll] for (int i = 0; i < VIEW_SAMPLES; i++)
                {
                    float3 p = cam + viewDir * t;
                    float height = length(p) - planetR;

                    float dR, dM;
                    GetDensities(height, dR, dM);

                    // accumulate view optical depth up to this sample
                    odR += dR * ds;
                    odM += dM * ds;

                    // transmittance camera->sample
                    float3 Tview = exp(-(betaR * odR + betaM * odM));

                    // transmittance sample->sun
                    float3 Tsun = ComputeSunTransmittance(p, sunDir, planetR, atmoR, betaR, betaM);

                    // phase (angle between incoming sunlight and outgoing toward camera)
                    float cosTheta = dot(sunDir, viewDir);
                    float phaseR = RayleighPhase(cosTheta);
                    float phaseM = HGPhase(cosTheta, _MieG);

                    float3 scatter = (dR * betaR * phaseR) + (dM * betaM * phaseM);

                    // single scattering contribution
                    L += Tview * Tsun * scatter * ds * sunCol;

                    t += ds;
                }

                // Final transmittance to the end of the ray segment (ground or space)
                float3 Tend = exp(-(betaR * odR + betaM * odM));

                // ---- Ground term (only if we hit ground) ----
                float3 groundTerm = 0.0;
                if (hitsGround && tGround < tExitAtmo)
                {
                    float3 pG = cam + viewDir * tGround;
                    float3 nG = normalize(pG); // radial normal

                    float NdotL = saturate(dot(nG, sunDir));
                    float3 TsunG = ComputeSunTransmittance(pG, sunDir, planetR, atmoR, betaR, betaM);

                    // Diffuse Lambert ( /pi ), then attenuate by view + sun transmittance
                    groundTerm = (_GroundAlbedo.rgb * _GroundIntensity) * (sunCol * TsunG) * (NdotL * (1.0 / 3.14159265)) * Tend;
                }

                float3 col = (L + groundTerm) * _Exposure;

                // ---- Sun disk (attenuated by atmosphere) ----
                // Only meaningful when looking above horizon; still safe either way.
                float sunAngularRadius = radians(_SunAngularRadiusDeg);
                float cosSun = dot(viewDir, sunDir);
                float cosLimit = cos(sunAngularRadius);

                if (cosSun >= cosLimit)
                {
                    // Use transmittance along view ray to top of atmosphere (approx Tend when not hitting ground).
                    // If we hit ground, sun is likely not visible anyway (below horizon), but this keeps it stable.
                    float disk = saturate((cosSun - cosLimit) / max(1e-4, (1.0 - cosLimit)));
                    disk = smoothstep(0.0, 1.0, disk);
                    col += sunCol * Tend * (disk * _SunDiskBoost);
                }

                // ---- Your volumetrics composite hook ----
                half4 finalColor = Volumetrics(half4((half3)col, 1.0h), IN.wPos.xyz);

                // ---- Dither (same as your old shader) ----
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

                return max(finalColor,0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}