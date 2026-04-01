using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public sealed class AtmosphereLutFeature : ScriptableRendererFeature
{
    [System.Serializable]
    public sealed class Settings
    {
        public ComputeShader atmosphereCompute;

        [Header("LUT Resolutions")]
        public Vector2Int transmittanceRes = new(256, 64);
        public Vector2Int multiScatteringRes = new(32, 32);
        public Vector2Int skyViewRes = new(192, 108);

        [Header("Planet / Atmosphere (m)")]
        public float planetRadius = 6360000f;
        public float atmosphereHeight = 80000f;

        [Header("Scale Heights (m)")]
        public float rayleighScaleHeight = 8000f;
        public float mieScaleHeight = 1200f;

        [Header("Scattering Coefficients (1/m)")]
        public Vector3 betaRayleigh = new(5.8e-6f, 13.5e-6f, 33.1e-6f);
        public Vector3 betaMie = new(21e-6f, 21e-6f, 21e-6f);

        [Range(0f, 0.999f)] public float mieG = 0.8f;

        [Header("Camera Altitude Mapping")]
        public float seaLevelWorldY = 0f;

        [Header("Update Thresholds")]
        [Range(0.01f, 5f)] public float sunDirUpdateDegrees = 0.25f;
        public float altitudeUpdateMeters = 5f;
        [Range(1, 120)] public int maxFramesBetweenSkyUpdates = 30;

        [Header("Art / Energy")]
        [Range(0f, 5f)] public float thickness = 1f;
        public Color groundAlbedo = new(0.369f, 0.349f, 0.341f, 1f);
        [Range(0f, 4f)] public float groundIntensity = 1f;

        [Header("Sun")]
        public float sunIntensity = 20f;
        
        [Header("Local Directional Light Transmittance")]
        [Min(1f)] public float localSunBelowCameraMeters = 1000f;
        [Min(1f)] public float localSunAboveCameraMeters = 4000f;
    }

    static float AngleDeg(Vector3 a, Vector3 b)
    {
        float d = Mathf.Clamp(Vector3.Dot(a.normalized, b.normalized), -1f, 1f);
        return Mathf.Acos(d) * Mathf.Rad2Deg;
    }

    public Settings settings = new();

    [StructLayout(LayoutKind.Sequential)]
    struct AtmosphereParamsCB
    {
        public Vector4 AtmoPlanet;        // x=planetRadius, y=atmosphereRadius, z=cameraAltitude, w=thickness
        public Vector4 AtmoScaleHeights;  // x=rayleighScaleHeight, y=mieScaleHeight, z=mieG, w=groundIntensity
        public Vector4 AtmoBetaRayleigh;  // xyz=betaRayleigh
        public Vector4 AtmoBetaMie;       // xyz=betaMie
        public Vector4 AtmoSunDir;        // xyz=sunDir
        public Vector4 AtmoSunColor;      // xyz=sunColor
        public Vector4 AtmoGroundAlbedo;  // xyz=groundAlbedo
        public Vector4 AtmoPlanetCenterWS;
    }

    class Pass : ScriptableRenderPass
    {
        Settings s;

        int kTrans, kMS, kSky;

        sealed class CameraResources
        {
            public Vector3 lastSunDir = new Vector3(0f, 1f, 0f);
            public float lastCamAlt = -999999f;
            public int framesSinceSky = 999999;
            public RenderTexture skyLut;
            public readonly Vector4[] localSunTauUpload = new Vector4[LocalSunSampleCount];

            public void Release()
            {
                if (skyLut != null)
                {
                    skyLut.Release();
                    skyLut = null;
                }
            }
        }

        RenderTexture transLut, msLut;
        int lastHash;

        readonly Dictionary<int, CameraResources> m_Resources = new();

        static readonly int ID_TransRW = Shader.PropertyToID("_RW_TransmittanceLUT");
        static readonly int ID_MSRW    = Shader.PropertyToID("_RW_MultiScatteringLUT");
        static readonly int ID_SkyRW   = Shader.PropertyToID("_RW_SkyViewLUT");

        static readonly int ID_Trans   = Shader.PropertyToID("_TransmittanceLUT");
        static readonly int ID_MS      = Shader.PropertyToID("_MultiScatteringLUT");

        // cbuffer name in HLSL: CBUFFER_START(AtmosphereParams)
        static readonly int ID_AtmosphereParamsCB = Shader.PropertyToID("AtmosphereParams");

        // globals
        static readonly int G_SkyView  = Shader.PropertyToID("_AtmosphereSkyViewLUT");
        static readonly int G_Trans    = Shader.PropertyToID("_AtmosphereTransmittanceLUT");
        static readonly int G_MS       = Shader.PropertyToID("_AtmosphereMultiScatteringLUT");
        
        const int LocalSunSampleCount = 8; //ATMOSPHERE_LOCAL_SUN_SAMPLE_COUNT

        static readonly int G_LocalSunTau    = Shader.PropertyToID("_AtmosphereLocalSunTau");
        static readonly int G_LocalSunParams = Shader.PropertyToID("_AtmosphereLocalSunParams");
        static readonly int G_LocalOriginWS  = Shader.PropertyToID("_AtmosphereLocalOriginWS");
        static readonly int G_LocalUpWS      = Shader.PropertyToID("_AtmosphereLocalUpWS");

        // optional legacy globals if you still want them
        // static readonly int G_SunDir   = Shader.PropertyToID("_Atmo_SunDir");
        // static readonly int G_SunColor = Shader.PropertyToID("_Atmo_SunColor");
        // static readonly int G_Rp       = Shader.PropertyToID("_Atmo_PlanetRadius");
        // static readonly int G_Ra       = Shader.PropertyToID("_Atmo_AtmosphereRadius");
        // static readonly int G_CamAlt   = Shader.PropertyToID("_Atmo_CameraAltitude");

        GraphicsBuffer atmosphereParamsBuffer;
        static readonly int AtmosphereParamsCBSize = Marshal.SizeOf<AtmosphereParamsCB>();
        readonly AtmosphereParamsCB[] atmosphereParamsUpload = new AtmosphereParamsCB[1];

        public Pass(Settings settings)
        {
            s = settings;
            renderPassEvent = RenderPassEvent.BeforeRenderingSkybox;
        }
        public static bool ShouldDriveAtmosphereGlobals(ref RenderingData renderingData)
        {
            ref var cameraData = ref renderingData.cameraData;
            Camera cam = cameraData.camera;

            if (cam == null)
                return false;

            // Never let utility cameras stomp the globals.
            if (cameraData.isPreviewCamera)
                return false;

            if (cam.cameraType == CameraType.Preview ||
                cam.cameraType == CameraType.Reflection)
                return false;

            // Overlay cameras should not own global atmosphere state.
            if (cameraData.renderType != CameraRenderType.Base)
                return false;

#if UNITY_EDITOR
            // In play mode, prefer the game camera only.
            if (Application.isPlaying)
                return cam.cameraType == CameraType.Game && cameraData.resolveFinalTarget;

            // Outside play mode, let SceneView drive it so editing still works.
            if (cam.cameraType == CameraType.SceneView)
                return true;
#endif

            return cam.cameraType == CameraType.Game && cameraData.resolveFinalTarget;
        }
        static RenderTexture Alloc(Vector2Int res, string name)
        {
            var rt = new RenderTexture(res.x, res.y, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
            rt.name = name;
            rt.enableRandomWrite = true;
            rt.filterMode = FilterMode.Bilinear;
            rt.wrapMode = TextureWrapMode.Clamp;
            rt.useMipMap = false;
            rt.Create();
            return rt;
        }
        
        static float ComputeAirMass(float mu)
        {
            mu = Mathf.Clamp(mu, -1f, 1f);

            // No direct sun once it goes below the local horizon.
            if (mu <= 0f)
                return 1e6f;

            // Kasten-Young style fit, much nicer near the horizon than raw 1/mu.
            float zenithDeg = Mathf.Acos(mu) * Mathf.Rad2Deg;
            float denom = mu + 0.15f * Mathf.Pow(Mathf.Max(93.885f - zenithDeg, 0.001f), -1.253f);

            if (denom <= 1e-5f)
                return 1e6f;

            return 1f / denom;
        }

        static float IntegrateExponentialLayer(float altitudeMeters, float scaleHeight, float atmosphereHeight)
        {
            if (scaleHeight <= 0f || atmosphereHeight <= 0f)
                return 0f;

            float h = Mathf.Clamp(altitudeMeters, 0f, atmosphereHeight);
            float remaining = Mathf.Max(atmosphereHeight - h, 0f);

            // Integral of exp(-x/H) from h to atmosphere top.
            return Mathf.Exp(-h / scaleHeight) * scaleHeight * (1f - Mathf.Exp(-remaining / scaleHeight));
        }

        Vector3 EvalLocalSunTau(float altitudeMeters, float muSun)
        {
            if (muSun <= 0f)
                return new Vector3(1e6f, 1e6f, 1e6f);

            float airMass = ComputeAirMass(muSun);

            float rayleighIntegral = IntegrateExponentialLayer(
                altitudeMeters,
                s.rayleighScaleHeight,
                s.atmosphereHeight);

            float mieIntegral = IntegrateExponentialLayer(
                altitudeMeters,
                s.mieScaleHeight,
                s.atmosphereHeight);

            Vector3 tau =
                s.betaRayleigh * rayleighIntegral +
                s.betaMie * mieIntegral;

            return tau * (airMass * s.thickness);
        }

        void UpdateLocalSunTransmittanceGlobals(
            CommandBuffer cmd,
            Vector3 cameraPositionWS,
            float cameraAltitudeMeters,
            Vector3 sunDirWS,
            Vector4[] localSunTauUpload)
        {
            Vector3 planetCenterWS = new Vector3(
                0f,
                s.seaLevelWorldY - s.planetRadius,
                0f);

            Vector3 localUp = cameraPositionWS - planetCenterWS;
            if (localUp.sqrMagnitude < 1e-10f)
                localUp = Vector3.up;
            else
                localUp.Normalize();

            float minAlt = cameraAltitudeMeters - Mathf.Max(1f, s.localSunBelowCameraMeters);
            float maxAlt = cameraAltitudeMeters + Mathf.Max(1f, s.localSunAboveCameraMeters);

            if (maxAlt <= minAlt)
                maxAlt = minAlt + 1f;

            float muSun = Vector3.Dot(localUp, sunDirWS);

            for (int i = 0; i < LocalSunSampleCount; i++)
            {
                float t = i / (float)(LocalSunSampleCount - 1);
                float altitude = Mathf.Lerp(minAlt, maxAlt, t);
                Vector3 tau = EvalLocalSunTau(altitude, muSun);
                localSunTauUpload[i] = new Vector4(tau.x, tau.y, tau.z, 0f);
            }

            Vector4 localSunParams = new Vector4(
                minAlt,
                1f / (maxAlt - minAlt),
                LocalSunSampleCount - 1,
                cameraAltitudeMeters);

            cmd.SetGlobalVectorArray(G_LocalSunTau, localSunTauUpload);
            cmd.SetGlobalVector(G_LocalSunParams, localSunParams);
            cmd.SetGlobalVector(G_LocalOriginWS, new Vector4(
                cameraPositionWS.x,
                cameraPositionWS.y,
                cameraPositionWS.z,
                0f));
            cmd.SetGlobalVector(G_LocalUpWS, new Vector4(
                localUp.x,
                localUp.y,
                localUp.z,
                0f));
        }

        CameraResources GetOrCreateCameraResources(Camera cam)
        {
            int cameraId = cam.GetInstanceID();
            if (!m_Resources.TryGetValue(cameraId, out CameraResources resources))
            {
                resources = new CameraResources();
                m_Resources.Add(cameraId, resources);
            }

            return resources;
        }

        void EnsureCameraSkyLut(CameraResources resources)
        {
            if (resources.skyLut == null ||
                resources.skyLut.width != s.skyViewRes.x ||
                resources.skyLut.height != s.skyViewRes.y)
            {
                resources.Release();
                resources.skyLut = Alloc(s.skyViewRes, "Atmosphere_SkyViewLUT");
            }
        }

        void EnsureAtmosphereConstantBuffer()
        {
            if (atmosphereParamsBuffer != null && atmosphereParamsBuffer.IsValid())
                return;

            atmosphereParamsBuffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Constant,
                1,
                AtmosphereParamsCBSize);

            atmosphereParamsBuffer.name = "AtmosphereParamsCB";
        }

        void UpdateAtmosphereConstantBuffer(float camAlt, Vector3 sunDirWS, Color sunColor)
        {
            float rp = s.planetRadius;
            float ra = s.planetRadius + s.atmosphereHeight;
            Vector3 planetCenterWS = new Vector3(
                0f,
                s.seaLevelWorldY - s.planetRadius,
                0f
            );
            
            atmosphereParamsUpload[0] = new AtmosphereParamsCB
            {
                AtmoPlanet = new Vector4(
                    rp,
                    ra,
                    camAlt,
                    s.thickness),

                AtmoScaleHeights = new Vector4(
                    s.rayleighScaleHeight,
                    s.mieScaleHeight,
                    s.mieG,
                    s.groundIntensity),

                AtmoBetaRayleigh = new Vector4(
                    s.betaRayleigh.x,
                    s.betaRayleigh.y,
                    s.betaRayleigh.z,
                    0f),

                AtmoBetaMie = new Vector4(
                    s.betaMie.x,
                    s.betaMie.y,
                    s.betaMie.z,
                    0f),

                AtmoSunDir = new Vector4(
                    sunDirWS.x,
                    sunDirWS.y,
                    sunDirWS.z,
                    0f),

                AtmoSunColor = new Vector4(
                    sunColor.r,
                    sunColor.g,
                    sunColor.b,
                    0f),

                AtmoGroundAlbedo = new Vector4(
                    s.groundAlbedo.r,
                    s.groundAlbedo.g,
                    s.groundAlbedo.b,
                    0f),
                AtmoPlanetCenterWS = new Vector4(
                    planetCenterWS.x,
                    planetCenterWS.y,
                    planetCenterWS.z,
                    0f),
            };

            atmosphereParamsBuffer.SetData(atmosphereParamsUpload);
        }

        int ComputeHash()
        {
            unchecked
            {
                int h = 17;
                h = h * 31 + s.transmittanceRes.GetHashCode();
                h = h * 31 + s.multiScatteringRes.GetHashCode();
                h = h * 31 + s.skyViewRes.GetHashCode();
                h = h * 31 + s.planetRadius.GetHashCode();
                h = h * 31 + s.atmosphereHeight.GetHashCode();
                h = h * 31 + s.rayleighScaleHeight.GetHashCode();
                h = h * 31 + s.mieScaleHeight.GetHashCode();
                h = h * 31 + s.betaRayleigh.GetHashCode();
                h = h * 31 + s.betaMie.GetHashCode();
                h = h * 31 + s.mieG.GetHashCode();
                h = h * 31 + s.thickness.GetHashCode();
                h = h * 31 + s.groundAlbedo.GetHashCode();
                h = h * 31 + s.groundIntensity.GetHashCode();
                return h;
            }
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            if (s.atmosphereCompute == null) return;

            EnsureAtmosphereConstantBuffer();

            if (transLut == null || transLut.width != s.transmittanceRes.x || transLut.height != s.transmittanceRes.y)
            {
                if (transLut) transLut.Release();
                transLut = Alloc(s.transmittanceRes, "Atmosphere_TransmittanceLUT");
            }
            if (msLut == null || msLut.width != s.multiScatteringRes.x || msLut.height != s.multiScatteringRes.y)
            {
                if (msLut) msLut.Release();
                msLut = Alloc(s.multiScatteringRes, "Atmosphere_MultiScatteringLUT");
            }
            Camera cam = renderingData.cameraData.camera;
            if (cam != null)
            {
                CameraResources resources = GetOrCreateCameraResources(cam);
                EnsureCameraSkyLut(resources);
            }

            kTrans = s.atmosphereCompute.FindKernel("KTransmittance");
            kMS    = s.atmosphereCompute.FindKernel("KMultiScattering");
            kSky   = s.atmosphereCompute.FindKernel("KSkyView");
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (s.atmosphereCompute == null) return;
            if (!ShouldDriveAtmosphereGlobals(ref renderingData)) return;

            var cmd = CommandBufferPool.Get("Atmosphere LUTs");

            Light main = RenderSettings.sun;
            Vector3 sunDirWS = Vector3.up;
            Color sunColor = Color.black;

            if (main != null)
            {
                sunDirWS = -main.transform.forward;
                sunColor = main.color * s.sunIntensity;
            }

            var cam = renderingData.cameraData.camera;
            CameraResources resources = GetOrCreateCameraResources(cam);
            EnsureCameraSkyLut(resources);

            float camAlt = cam.transform.position.y - s.seaLevelWorldY;
            resources.framesSinceSky++;

            float sunDeltaDeg = AngleDeg(resources.lastSunDir, sunDirWS);
            float altDelta = Mathf.Abs(camAlt - resources.lastCamAlt);

            bool skyMissing = (resources.skyLut == null || !resources.skyLut.IsCreated());

            bool updateSky =
                skyMissing ||
                sunDeltaDeg >= s.sunDirUpdateDegrees ||
                altDelta >= s.altitudeUpdateMeters ||
                resources.framesSinceSky >= s.maxFramesBetweenSkyUpdates;

            int h = ComputeHash();
            bool rebuildStatic = (h != lastHash);
            lastHash = h;

            if (rebuildStatic)
                updateSky = true;
            
            Vector3 sunDirWSN = sunDirWS.normalized;
            // Update CPU->GPU cbuffer contents.
            UpdateAtmosphereConstantBuffer(camAlt, sunDirWSN, sunColor);

            // Build tiny local 1D tau array for local geometry directional-light tinting.
            UpdateLocalSunTransmittanceGlobals(
                cmd,
                cam.transform.position,
                camAlt,
                sunDirWSN,
                resources.localSunTauUpload);
            
            // Bind the cbuffer to the compute shader once.
            cmd.SetComputeConstantBufferParam(
                s.atmosphereCompute,
                ID_AtmosphereParamsCB,
                atmosphereParamsBuffer,
                0,
                AtmosphereParamsCBSize);

            // Optional: also bind globally so regular shaders with the same
            // CBUFFER_START(AtmosphereParams) can read the exact same data.
            cmd.SetGlobalConstantBuffer(
                atmosphereParamsBuffer,
                ID_AtmosphereParamsCB,
                0,
                AtmosphereParamsCBSize);

            // Keep these as separate regular params because they are not in AtmosphereParams.
            cmd.SetComputeVectorParam(s.atmosphereCompute, "_TransmittanceRes",
                new Vector4(s.transmittanceRes.x, s.transmittanceRes.y, 0, 0));
            cmd.SetComputeVectorParam(s.atmosphereCompute, "_MultiScatteringRes",
                new Vector4(s.multiScatteringRes.x, s.multiScatteringRes.y, 0, 0));
            cmd.SetComputeVectorParam(s.atmosphereCompute, "_SkyViewRes",
                new Vector4(s.skyViewRes.x, s.skyViewRes.y, 0, 0));

            if (rebuildStatic)
            {
                cmd.SetComputeTextureParam(s.atmosphereCompute, kTrans, ID_TransRW, transLut);
                Dispatch2D(cmd, s.atmosphereCompute, kTrans, s.transmittanceRes);

                cmd.SetComputeTextureParam(s.atmosphereCompute, kMS, ID_Trans, transLut);
                cmd.SetComputeTextureParam(s.atmosphereCompute, kMS, ID_MSRW, msLut);
                Dispatch2D(cmd, s.atmosphereCompute, kMS, s.multiScatteringRes);
            }

            if (updateSky)
            {
                cmd.SetComputeTextureParam(s.atmosphereCompute, kSky, ID_Trans, transLut);
                cmd.SetComputeTextureParam(s.atmosphereCompute, kSky, ID_MS, msLut);
                cmd.SetComputeTextureParam(s.atmosphereCompute, kSky, ID_SkyRW, resources.skyLut);
                Dispatch2D(cmd, s.atmosphereCompute, kSky, s.skyViewRes);

                resources.lastSunDir = sunDirWSN;
                resources.lastCamAlt = camAlt;
                resources.framesSinceSky = 0;
            }

            cmd.SetGlobalTexture(G_SkyView, resources.skyLut);
            cmd.SetGlobalTexture(G_Trans, transLut);
            cmd.SetGlobalTexture(G_MS, msLut);

            // Optional legacy globals for shaders that have not yet moved to AtmosphereParams cbuffer
            // float rp = s.planetRadius;
            // float ra = s.planetRadius + s.atmosphereHeight;
            // cmd.SetGlobalVector(G_SunDir, sunDirWS.normalized);
            // cmd.SetGlobalVector(G_SunColor, (Vector4)sunColor);
            // cmd.SetGlobalFloat(G_Rp, rp);
            // cmd.SetGlobalFloat(G_Ra, ra);
            // cmd.SetGlobalFloat(G_CamAlt, camAlt);

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        static void Dispatch2D(CommandBuffer cmd, ComputeShader cs, int kernel, Vector2Int res)
        {
            cs.GetKernelThreadGroupSizes(kernel, out uint tx, out uint ty, out _);
            int gx = Mathf.CeilToInt(res.x / (float)tx);
            int gy = Mathf.CeilToInt(res.y / (float)ty);
            cmd.DispatchCompute(cs, kernel, gx, gy, 1);
        }

        public void Dispose()
        {
            if (transLut != null)
            {
                transLut.Release();
                transLut = null;
            }

            if (msLut != null)
            {
                msLut.Release();
                msLut = null;
            }

            foreach (CameraResources resources in m_Resources.Values)
                resources.Release();
            m_Resources.Clear();

            if (atmosphereParamsBuffer != null)
            {
                atmosphereParamsBuffer.Release();
                atmosphereParamsBuffer = null;
            }
        }

        public override void OnCameraCleanup(CommandBuffer cmd) { }
    }

    Pass pass;

    public override void Create()
    {
        pass = new Pass(settings);
    }

    protected override void Dispose(bool disposing)
    {
        pass?.Dispose();
        pass = null;
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (settings.atmosphereCompute == null) return;
      //  if (!Pass.ShouldDriveAtmosphereGlobals(ref renderingData)) return;
        renderer.EnqueuePass(pass);
    }
    
}