using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR;
using Color = UnityEngine.Color;

public sealed class VolumetricRenderingFeature_2022 : ScriptableRendererFeature
{
    [Serializable]
    public sealed class Settings
    {
        [Header("Assets")]
        public VolumetricData volumetricData;
        public ComputeShader froxelFogCompute;
        public ComputeShader froxelIntegrationCompute;
        public ComputeShader clipmapCompute;

        [Header("Execution")]
        public RenderPassEvent passEvent = RenderPassEvent.AfterRenderingShadows;
        [Range(1, 64)] public int maxClipmapBuildStepsPerFrame = 8;
        public bool runInSceneView = false;

        [Header("Per-frame Params")]
        [Range(0, 1)] public float sliceDistributionUniformity = 0.5f;

        [Header("Foveation (Scatter)")]
        public bool foveationEnabled = true;
        [Range(0, 1)] public float foveationInnerRadius = 0.3f;
        [Range(0, 1)] public float foveationOuterRadius = 0.4f;

        [Header("Noise / Debug (Scatter)")]
        public float bakedTurbulence = 5f;
        public bool panic = false;

        [Header("Globals (optional)")]
        public bool enableVolumetricsKeyword = true; // _VOLUMETRICS_ENABLED
        public bool enableHiQSamplingKeyword = true; // _HiQSampling_ENABLED
    }

    public Settings settings = new Settings();

    public static ComputeShader s_froxelFogCompute          {get; internal set;}
    public static ComputeShader s_froxelIntegrationCompute  {get; internal set;}
    public static ComputeShader s_clipmapCompute            {get; internal set;}

    VolumetricPass m_Pass;
    ClearVolumetricGlobalsPass m_ClearPass;
    
    readonly Dictionary<Camera, CameraResources> m_Resources = new();

    public override void Create()
    {
        m_Pass = new VolumetricPass(this)
        {
            renderPassEvent = settings.passEvent
        };
        
        m_ClearPass = new ClearVolumetricGlobalsPass()
        {
            renderPassEvent = settings.passEvent
        };
        s_froxelFogCompute          = settings.froxelFogCompute;
        s_froxelIntegrationCompute  = settings.froxelIntegrationCompute;
        s_clipmapCompute            = settings.clipmapCompute;
        RenderPipelineManager.beginContextRendering += GarbageCollectPeriodic;
    }
    
    static readonly Unity.Profiling.ProfilerMarker s_AddRenderPassesMarker = new Unity.Profiling.ProfilerMarker("VolumetricsRenderFeature.AddRenderPasses");
    static readonly Unity.Profiling.ProfilerMarker s_ARPNullCheckMarker = new Unity.Profiling.ProfilerMarker("VolumetricsRenderFeature.AddRenderPasses.InitNullChecks");
    static readonly Unity.Profiling.ProfilerMarker s_ARPShouldRunForCamera = new Unity.Profiling.ProfilerMarker("VolumetricsRenderFeature.AddRenderPasses.ShouldRunForCamera");
    static readonly Unity.Profiling.ProfilerMarker s_ARPNotRun = new Unity.Profiling.ProfilerMarker("VolumetricsRenderFeature.AddRenderPasses.NotShouldRun");
    static readonly Unity.Profiling.ProfilerMarker s_ARPEnsureAllocated = new Unity.Profiling.ProfilerMarker("VolumetricsRenderFeature.AddRenderPasses.EnsureAllocated");
    static readonly Unity.Profiling.ProfilerMarker s_ARPSetup = new Unity.Profiling.ProfilerMarker("VolumetricsRenderFeature.AddRenderPasses.Setup");
    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        using (s_AddRenderPassesMarker.Auto())
        {
            Camera cam;
            s_ARPNullCheckMarker.Begin();
        if (!SystemInfo.supportsComputeShaders) return;

        var s = settings;
        if (s.volumetricData == null) return;
        if (s.froxelFogCompute == null || s.froxelIntegrationCompute == null || s.clipmapCompute == null) return;

        cam = renderingData.cameraData.camera;
        if (cam == null) return;
            s_ARPNullCheckMarker.End();

s_ARPShouldRunForCamera.Begin();
        bool shouldRun = ShouldRunForCamera(cam, ref renderingData, s);
s_ARPShouldRunForCamera.End();

        if (!shouldRun)
        {
            s_ARPNotRun.Begin();
            ReleaseForCamera(cam);
            renderer.EnqueuePass(m_ClearPass);
            s_ARPNotRun.End();
            return;
        }

        s_ARPEnsureAllocated.Begin();
        var res = GetOrCreate(cam);
        res.EnsureAllocated(s, cam, renderingData.cameraData.xrRendering);
        s_ARPEnsureAllocated.End();

        s_ARPSetup.Begin();
        m_Pass.Setup(cam, res);
        s_ARPSetup.End();
        renderer.EnqueuePass(m_Pass);
        }
    }
    
    static bool ShouldRunForCamera(Camera cam, ref RenderingData renderingData, Settings s)
    {
        if (cam == null)
            return false;

        ref var cd = ref renderingData.cameraData;

        if (cd.renderType == CameraRenderType.Overlay)
            return false;

        if (cam.GetComponent<SkipVolumetricsTag>() != null)
            return false;

        // Preview cameras, inspector previews, etc.
        if (cd.isPreviewCamera)
            return false;

        // Reflection probe / reflection cameras
        if (cd.cameraType == CameraType.Reflection || cam.cameraType == CameraType.Reflection)
            return false;

        // Scene view only if explicitly enabled
        if (cd.isSceneViewCamera || cam.cameraType == CameraType.SceneView)
            return s.runInSceneView;

        // Only real game cameras otherwise
        return cd.cameraType == CameraType.Game || cam.cameraType == CameraType.Game;
    }

    public void ReleaseForCamera(Camera cam)
    {
        //int id = cam.GetInstanceID();
        if (m_Resources.TryGetValue(cam, out var r))
        {
            r.Dispose();
            m_Resources.Remove(cam);
        }
    }
    protected override void Dispose(bool disposing)
    {
        if (!disposing) return;
        foreach (var kv in m_Resources) kv.Value.Dispose();
        m_Resources.Clear();
        RenderPipelineManager.beginContextRendering -= GarbageCollectPeriodic;
    }

    Camera[] garbageCollectArray = new Camera[16];
    static readonly Unity.Profiling.ProfilerMarker s_GCMarker = new Unity.Profiling.ProfilerMarker("VolumetricsRenderFeature.GarbageCollectResources");
    public void GarbageCollectResources()
    {
        using (s_GCMarker.Auto())
        {
            garbageCollectArray ??= new Camera[16];
            int gcIdx = 0;
            int gcArrayLength = garbageCollectArray.Length;

            // Only allocate a list if we have more than 16 cameras to remove, otherwise just use the fixed length array
            bool needsListAlloc = false;
            List<Camera> garbageCollectList = null;

            // We can't remove items
            var enumerator = m_Resources.GetEnumerator();
            while (enumerator.MoveNext()) // foreach creates garbage, use enumerator directly
            {
                KeyValuePair<Camera, CameraResources> kvp = enumerator.Current;
#if UNITY_EDITOR
                if (kvp.Key == null)
#else   
                if (kvp.Key == null || kvp.Key.isActiveAndEnabled == false)
#endif  
                {
                    kvp.Value.Dispose();
                    if (gcIdx < gcArrayLength)
                    {
                        garbageCollectArray[gcIdx] = kvp.Key;
                        gcIdx++;
                    }
                    else if (needsListAlloc)
                    {
                        garbageCollectList.Add(kvp.Key);
                    }
                    else
                    {
                        needsListAlloc = true;
                        garbageCollectList = new List<Camera>(gcArrayLength);
                        garbageCollectList.Add(kvp.Key);
                    }
                }
            }
            for (int i = 0; i < gcIdx; i++)
            {
                m_Resources.Remove(garbageCollectArray[i]);
            }
            if (garbageCollectList != null)
            {
                int numList = garbageCollectList.Count;
                for (int i = 0; i < numList; i++)
                {
                     m_Resources.Remove(garbageCollectList[i]);
                }
            }
            //Debug.Log($"Removed {gcIdx + (garbageCollectList == null ? 0 : garbageCollectList.Count)} cameras");
        }
    }

    int frameCount = 0;
    const int gcTimerCount = 900;
    void GarbageCollectPeriodic(ScriptableRenderContext ctx, List<Camera> cameras)
    {
        frameCount++;
        if (frameCount > gcTimerCount)
        {
            //Debug.Log("Garbage Collecting volumes...");
            frameCount = 0;
            GarbageCollectResources();
        }
    }

    CameraResources GetOrCreate(Camera cam)
    {
        //int id = cam.GetInstanceID();
        if (!m_Resources.TryGetValue(cam, out var r))
        {
            r = new CameraResources();
            m_Resources.Add(cam, r);
        }
        return r;
    }

    // ============================================================
    // Per-camera persistent state
    // ============================================================
    sealed class CameraResources : IDisposable
    {
        public RenderTexture[] froxel = new RenderTexture[2];
        public RenderTexture integrate;
        public int ping = 0;

        public ResolvedVolumetricConfig rVC;
        public struct ResolvedVolumetricConfig
        {
            public int froxelWidth;
            public int froxelHeight;
            public int froxelDepth;
            //public int eyeCount;
            //public int integrateWidth;
            public float near;
            public float far;
            //public float sliceDistributionUniformity;
           // public bool foveationEnabled;
        }
        
        public VolumetricClipmapManager clipmaps;
        public VolumeManager VolMana;
        // kernels
        public int kScatter = -1;
        public int kIntegrate = -1;

        // dispatch sizes
        public int scatterGX, scatterGY, scatterGZ;
        public int integrateGX, integrateGY;

        // history
        public Matrix4x4 previousFrameMatrix = Matrix4x4.identity;
        public Matrix4x4 prevViewProjMatrix = Matrix4x4.identity;
        public Vector3 previousCameraPos = Vector3.zero;

        // constant buffers
        public ComputeBuffer shaderGlobalsCB;
        public ComputeBuffer scatterCB;
        public ComputeBuffer stepAddCB;

        public ShaderConstants[] shaderGlobalsArr = new ShaderConstants[1];
        public ScatteringPerFrameConstants[] scatterArr = new ScatteringPerFrameConstants[1];
        public StepAddPerFrameConstants[] stepAddArr = new StepAddPerFrameConstants[1];

        // jitter pattern cache
        public bool xyInit = false;
        public Vector2[] xySeq = new Vector2[7];
        
        public bool   panicPrevValid;
        public float  panicPrevExtinction;
        public float  panicPrevStaticLightMul;

    static readonly Unity.Profiling.ProfilerMarker s_EA1 = new Unity.Profiling.ProfilerMarker("VolumetricsRenderFeature.CameraResources.EnsureAllocated.1");
    static readonly Unity.Profiling.ProfilerMarker s_EAFindKernels = new Unity.Profiling.ProfilerMarker("VolumetricsRenderFeature.CameraResources.EnsureAllocated.FindKernel");
    static readonly Unity.Profiling.ProfilerMarker s_EA2 = new Unity.Profiling.ProfilerMarker("VolumetricsRenderFeature.CameraResources.EnsureAllocated.2");

       public void EnsureAllocated(Settings s, Camera cam, bool stereo)
        {
            s_EA1.Begin();
            var vd = s.volumetricData;

            rVC.froxelWidth  = vd.FroxelWidthResolution;
            rVC.froxelHeight = vd.FroxelHeightResolution;
            rVC.froxelDepth  = vd.FroxelDepthResolution;
            
            //Forcing higher resolution in the SceneView because it's not accumulating correctly leading to flickery jittery slices. We should fix that.
            // Quickly patching for now so artists don't work with poor looking previews and nerf their own work thinking it looks worse than it is   
        #if UNITY_EDITOR
            if (cam.cameraType == CameraType.SceneView)
            {
                rVC.froxelWidth  = 256;
                rVC.froxelHeight = 256;
                rVC.froxelDepth  = 256;
            }
        #endif
            s_EAFindKernels.Begin();
            if (kScatter < 0) kScatter = s.froxelFogCompute.FindKernel("Scatter");
            if (kIntegrate < 0) kIntegrate = s.froxelIntegrationCompute.FindKernel("StepAdd");
            s_EAFindKernels.End();

            int eyeCount = stereo ? 2 : 1;
            //rVC.eyeCount = eyeCount;
            int integrateWidth = rVC.froxelWidth * eyeCount;

            Ensure3DRT(ref froxel[0], rVC.froxelWidth, rVC.froxelHeight, rVC.froxelDepth,
                GraphicsFormat.R16G16B16A16_SFloat, false, $"{cam.name}_FroxelA", FilterMode.Point);
            Ensure3DRT(ref froxel[1], rVC.froxelWidth, rVC.froxelHeight, rVC.froxelDepth,
                GraphicsFormat.R16G16B16A16_SFloat, false, $"{cam.name}_FroxelB", FilterMode.Point);

            FilterMode integrationFilter = eyeCount == 2 ? FilterMode.Trilinear : FilterMode.Point;
            Ensure3DRT(ref integrate, integrateWidth, rVC.froxelHeight, rVC.froxelDepth,
                GraphicsFormat.R16G16B16A16_SFloat, false, $"{cam.name}_Integrate", integrationFilter);
s_EA1.End();
s_EA2.Begin();

            s.froxelFogCompute.GetKernelThreadGroupSizes(kScatter, out uint sx, out uint sy, out uint sz);
            scatterGX = Mathf.CeilToInt(rVC.froxelWidth  / (float)sx);
            scatterGY = Mathf.CeilToInt(rVC.froxelHeight / (float)sy);
            scatterGZ = Mathf.CeilToInt(rVC.froxelDepth  / (float)sz);

            s.froxelIntegrationCompute.GetKernelThreadGroupSizes(kIntegrate, out uint ix, out uint iy, out _);
            integrateGX = Mathf.CeilToInt(integrateWidth / (float)ix);
            integrateGY = Mathf.CeilToInt(rVC.froxelHeight / (float)iy);

            EnsureConstantBuffer(ref shaderGlobalsCB, MarshalSizeAligned<ShaderConstants>());
            EnsureConstantBuffer(ref scatterCB,       MarshalSizeAligned<ScatteringPerFrameConstants>());
            EnsureConstantBuffer(ref stepAddCB,       MarshalSizeAligned<StepAddPerFrameConstants>());

            clipmaps ??= new VolumetricClipmapManager();
            clipmaps.EnsureInitialized(vd, s.clipmapCompute, cam.name);

            VolMana ??= VolumeManager.instance;

            if (!xyInit)
            {
                GetHexagonalClosePackedSpheres7(xySeq);
                xyInit = true;
            }

            rVC.near = vd.near;
            rVC.far = vd.far;
            
            //
            // public float near;
            // public float far;
            // public float sliceDistributionUniformity;
            //
            // public bool foveationEnabled;
s_EA2.End();
        }

        public void Dispose()
        {
            Debug.Log("Volumetrics: Disposing of per-camera resources");

            ReleaseRT(ref froxel[0]);
            ReleaseRT(ref froxel[1]);
            ReleaseRT(ref integrate);

            
            ReleaseCB(ref shaderGlobalsCB);
            ReleaseCB(ref scatterCB);
            ReleaseCB(ref stepAddCB);

            clipmaps?.Dispose();
            clipmaps = null;

            Shader.SetGlobalConstantBuffer(VolumetricPass.ID_VolumetricsCB, (ComputeBuffer)null, 0, 0);
            VolumetricRenderingFeature_2022.s_froxelFogCompute.SetConstantBuffer(VolumetricPass.ID_PerFrameCB, (ComputeBuffer)null, 0, 0);
            VolumetricRenderingFeature_2022.s_froxelIntegrationCompute.SetConstantBuffer(VolumetricPass.ID_PerFrameCB, (ComputeBuffer)null, 0, 0);
        }

        static void Ensure3DRT(ref RenderTexture rt, int w, int h, int d, GraphicsFormat fmt, bool useMips, string name, FilterMode filter)
        {
            bool need =
                rt == null ||
                !rt.IsCreated() ||
                rt.width != w || rt.height != h || rt.volumeDepth != d ||
                rt.graphicsFormat != fmt ||
                rt.useMipMap != useMips;

            if (!need) return;
            
            if (rt != null && rt.IsCreated())
            {
                
                //CoreUtils.Destroy(rt);
            }

            var desc = new RenderTextureDescriptor(w, h)
            {
                dimension = TextureDimension.Tex3D,
                volumeDepth = d,
                msaaSamples = 1,
                enableRandomWrite = true,
                graphicsFormat = fmt,
                useMipMap = useMips,
                autoGenerateMips = false
            };

            if (rt == null)
            {
                rt = new RenderTexture(desc)
                {
                    name = name,
                    filterMode = filter,
                    wrapMode = TextureWrapMode.Clamp
                };
                rt.Create();
            }
            else
            {
                if (rt.IsCreated()) rt.Release();
                rt.width             = desc.width;
                rt.height            = desc.height;
                rt.dimension         = desc.dimension;
                rt.volumeDepth       = desc.volumeDepth;
                rt.antiAliasing      = desc.msaaSamples;
                rt.enableRandomWrite = desc.enableRandomWrite;
                rt.graphicsFormat    = desc.graphicsFormat;
                rt.useMipMap         = desc.useMipMap;
                rt.autoGenerateMips  = desc.autoGenerateMips;
                rt.name = name;
                rt.filterMode = filter;
                rt.wrapMode = TextureWrapMode.Clamp;
                rt.Create();
            }
        }

        static void ReleaseRT(ref RenderTexture rt)
        {
            if (rt == null) return;
            rt.Release();
            CoreUtils.Destroy(rt);
            rt = null;
        }

        static void ReleaseCB(ref ComputeBuffer cb)
        {
            if (cb == null) return;
            cb.Release();
            cb = null;
        }

        static void EnsureConstantBuffer(ref ComputeBuffer cb, int strideBytesAligned)
        {
            if (cb != null && cb.count == 1 && cb.stride == strideBytesAligned) return;

            if (cb != null) 
            {
                Debug.Log("EnsureConstantBuffer releasing existing constant buffer.");
                cb.Release();
            }
            cb = new ComputeBuffer(1, strideBytesAligned, ComputeBufferType.Constant);
        }

        static int MarshalSizeAligned<T>() where T : struct
        {
            int sz = Marshal.SizeOf(typeof(T));
            return (sz + 15) & ~15; // 16-byte align
        }

        // Your sampling pattern generator (ported)
        static void GetHexagonalClosePackedSpheres7(Vector2[] coords)
        {
            float r = 0.17054068870105443882f;
            float d = 2 * r;
            float s = r * Mathf.Sqrt(3);

            coords[0] = new Vector2(0, 0);
            coords[1] = new Vector2(-d, 0);
            coords[2] = new Vector2(d, 0);
            coords[3] = new Vector2(-r, -s);
            coords[4] = new Vector2(r, s);
            coords[5] = new Vector2(r, -s);
            coords[6] = new Vector2(-r, s);

            const float cos15 = 0.96592582628906828675f;
            const float sin15 = 0.25881904510252076235f;

            for (int i = 0; i < 7; i++)
            {
                Vector2 c = coords[i];
                coords[i].x = c.x * cos15 - c.y * sin15;
                coords[i].y = c.x * sin15 + c.y * cos15;
            }
        }
    }

    sealed class ClearVolumetricGlobalsPass : ScriptableRenderPass
    {
        static readonly ProfilingSampler s_Profile = new ProfilingSampler("Clear Volumetrics");

        static readonly int ID_VolumetricResult = Shader.PropertyToID("_VolumetricResult");
        static readonly int ID_InLightingTexture = Shader.PropertyToID("InLightingTexture");
        static readonly int ID_PanicRefresh = Shader.PropertyToID("_PanicRefresh");

        const string KW_VOLUMETRICS_ENABLED = "_VOLUMETRICS_ENABLED";
        const string KW_VOLUMETRICS_HQ_ENABLED = "_VOLUMETRICS_ENABLED_HQ";

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            var cmd = CommandBufferPool.Get("Clear Volumetrics");

            using (new ProfilingScope(cmd, s_Profile))
            {
                cmd.DisableShaderKeyword(KW_VOLUMETRICS_ENABLED);
                cmd.DisableShaderKeyword(KW_VOLUMETRICS_HQ_ENABLED);

                cmd.SetGlobalTexture(ID_VolumetricResult, CoreUtils.blackVolumeTexture);
                cmd.SetGlobalTexture(ID_InLightingTexture, CoreUtils.blackVolumeTexture);

                cmd.SetGlobalFloat(ID_PanicRefresh, 0f);
            }

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }
    }

    // ============================================================
    // Main SRP pass
    // ============================================================
    sealed class VolumetricPass : ScriptableRenderPass
    {
        readonly VolumetricRenderingFeature_2022 m_Feature;

        Camera m_Cam;
        CameraResources m_Res;

        static readonly ProfilingSampler s_Profile = new ProfilingSampler("Volumetrics (2022)");
        static readonly ProfilingSampler s_Volumeping = new ProfilingSampler("Volumes ping");

        // ---- IDs / names used by your compute shaders ----
        static readonly int ID_Result                = Shader.PropertyToID("Result");
        static readonly int ID_PreviousFrameLighting = Shader.PropertyToID("PreviousFrameLighting");
        static readonly int ID_InLightingTexture     = Shader.PropertyToID("InLightingTexture");

        static readonly int ID_VBufferUnitDepthTexelSpacing = Shader.PropertyToID("_VBufferUnitDepthTexelSpacing");
        static readonly int ID_invDimensions                = Shader.PropertyToID("_invDimensions");
        static readonly int ID_LeftEyeMatrix                = Shader.PropertyToID("LeftEyeMatrix");
        static readonly int ID_RightEyeMatrix               = Shader.PropertyToID("RightEyeMatrix");
        static readonly int ID_VolZBufferParams             = Shader.PropertyToID("_VolZBufferParams");
        static readonly int ID_FroxelDepthCount             = Shader.PropertyToID("_FroxelDepthCount");

        static readonly int ID_StereoBaseline               = Shader.PropertyToID("_StereoBaseline");
        static readonly int ID_StereoFocalLen               = Shader.PropertyToID("_StereoFocalLen");
        
        static readonly int ID_EyeCount                = Shader.PropertyToID("_EyeCount");
        static readonly int ID_StereoEnabled           = Shader.PropertyToID("_StereoEnabled");
        static readonly int ID_StereoDisparityOverride = Shader.PropertyToID("_StereoDisparityOverride");

        internal static readonly int ID_PerFrameCB          = Shader.PropertyToID("PerFrameCB");
        internal static readonly int ID_VolumetricsCB       = Shader.PropertyToID("VolumetricsCB");

        static readonly int ID_PreviousFrameMatrix          = Shader.PropertyToID("PreviousFrameMatrix");

        static readonly int ID_FrameIndex                   = Shader.PropertyToID("_FrameIndex");

        // Scatter extras (names match your existing compute usage)
        static readonly int ID_Noise3d                      = Shader.PropertyToID("Noise3d");
        static readonly int ID_FoveationEnabled             = Shader.PropertyToID("_FoveationEnabled");
        static readonly int ID_FoveationInnerRadius         = Shader.PropertyToID("_FoveationInnerRadius");
        static readonly int ID_FoveationOuterRadius         = Shader.PropertyToID("_FoveationOuterRadius");
        static readonly int ID_BakedTurbulence              = Shader.PropertyToID("_bakedTurbulence");
        static readonly int ID_Panic                        = Shader.PropertyToID("_Panic");

        static readonly int ID_VolumetricResult             = Shader.PropertyToID("_VolumetricResult");
        static readonly string KW_VOLUMETRICS_ENABLED       = "_VOLUMETRICS_ENABLED";
        static readonly string KW_VOLUMETRICS_HQ_ENABLED       = "_VOLUMETRICS_ENABLED_HQ";

        // z sequence (your original)
        static readonly float[] s_zSeq =
        {
            7.0f / 14.0f, 3.0f / 14.0f, 11.0f / 14.0f,
            5.0f / 14.0f, 9.0f / 14.0f, 1.0f / 14.0f, 13.0f / 14.0f
        };

        public VolumetricPass(VolumetricRenderingFeature_2022 feature) => m_Feature = feature;

        public void Setup(Camera cam, CameraResources res)
        {
            m_Cam = cam;
            m_Res = res;
        }
        // Property IDs (cache as static readonly on the pass class)
        static readonly int ID_GlobalExtinction      = Shader.PropertyToID("_GlobalExtinction");
        static readonly int ID_StaticLightMultiplier = Shader.PropertyToID("_StaticLightMultiplier");
        static readonly int ID_PanicRefresh          = Shader.PropertyToID("_PanicRefresh");

        void ApplyVolumeGlobals(CommandBuffer cmd, Volumetrics vol)
        {
            if (vol == null || !vol.active)
            {
                // Defaults when no volume is present
                cmd.SetGlobalFloat(ID_GlobalExtinction, VolumeRenderingUtils.ExtinctionFromMeanFreePath(50f));
                cmd.SetGlobalFloat(ID_StaticLightMultiplier, 1f);
                cmd.SetGlobalFloat(ID_PanicRefresh, 0f);
                m_Res.panicPrevValid = false;  // reset history when volume disappears
                return;
            }

            float extinction = VolumeRenderingUtils.ExtinctionFromMeanFreePath(vol.FogViewDistance.value);
            float staticMul  = vol.GlobalStaticLightMultiplier.value;

            // Panic detection — strictly per-camera, no Time.frameCount guard needed.
            float panic = 0f;
            if (vol.PanicRefreshEnabled.value && m_Res.panicPrevValid)
            {
                float dExt = RelDiff(extinction, m_Res.panicPrevExtinction);
                float dMul = RelDiff(staticMul,  m_Res.panicPrevStaticLightMul);
                float delta = Mathf.Max(dExt, dMul);
                if (delta >= vol.PanicRefreshThreshold.value)
                    panic = 1f;
            }

            m_Res.panicPrevExtinction     = extinction;
            m_Res.panicPrevStaticLightMul = staticMul;
            m_Res.panicPrevValid          = true;

            cmd.SetGlobalFloat(ID_GlobalExtinction,      extinction);
            cmd.SetGlobalFloat(ID_StaticLightMultiplier, staticMul);
            cmd.SetGlobalFloat(ID_PanicRefresh,          panic);
        }

        static float RelDiff(float a, float b)
        {
            float denom = Mathf.Max(Mathf.Max(Mathf.Abs(a), Mathf.Abs(b)), 1e-4f);
            return Mathf.Abs(a - b) / denom;
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (m_Cam == null || m_Res == null) return;

            var s  = m_Feature.settings;
           // var vd = s.volumetricData;
            var vd = m_Res.rVC;
            var cmd = CommandBufferPool.Get("Volumetrics");
            
            bool wantVol = ShouldRunForCamera(m_Cam, ref renderingData, m_Feature.settings);
            if (!wantVol)
            {
                // HARD RESET for this camera so it can't inherit from previous cameras
                cmd.DisableShaderKeyword(KW_VOLUMETRICS_ENABLED);
                cmd.DisableShaderKeyword(KW_VOLUMETRICS_HQ_ENABLED);
                cmd.SetGlobalTexture(ID_VolumetricResult, CoreUtils.blackVolumeTexture); // safe fallback

                // Optional: also clear panic/global params so debugging is sane
                cmd.SetGlobalFloat(Shader.PropertyToID("_PanicRefresh"), 0f);

                context.ExecuteCommandBuffer(cmd);
                CommandBufferPool.Release(cmd);
                return;
            }

            // Volumetrics enabled for this camera
            // cmd.EnableShaderKeyword(KW_VOLUMETRICS_ENABLED);
            
            using (new ProfilingScope(cmd, s_Profile))
            {

                if (s.enableVolumetricsKeyword && !s.enableHiQSamplingKeyword)
                { 
                    cmd.EnableShaderKeyword(KW_VOLUMETRICS_ENABLED);
                    cmd.DisableShaderKeyword(KW_VOLUMETRICS_HQ_ENABLED);
                }
                else if(s.enableVolumetricsKeyword && s.enableHiQSamplingKeyword)
                {
                    cmd.EnableShaderKeyword(KW_VOLUMETRICS_ENABLED); //Enabling both to handle legacy shaders
                    cmd.EnableShaderKeyword(KW_VOLUMETRICS_HQ_ENABLED);   
                }
                else
                {
                    cmd.DisableShaderKeyword(KW_VOLUMETRICS_ENABLED);
                    cmd.DisableShaderKeyword(KW_VOLUMETRICS_HQ_ENABLED);
                }
                
                
                    
                SkyManager.CheckSkyNull();

                // Ping-pong selection
                int prev = m_Res.ping;
                int cur  = prev ^ 1;
                m_Res.ping = cur;

                // ------------------------------------------------------------
                // Per-frame camera math (ported from UpdateFunc)
                // ------------------------------------------------------------
                float camAspect = GetAspectRatio(renderingData, m_Cam);

                // matrix scale-bias 
                Matrix4x4 matScaleBias = Matrix4x4.identity;
                matScaleBias.m00 = -0.5f;
                matScaleBias.m11 = -0.5f;
                matScaleBias.m22 =  0.5f;
                matScaleBias.m03 =  0.5f;
                matScaleBias.m13 =  0.5f;
                matScaleBias.m23 =  0.5f;

                //  projection matrix construction
                Matrix4x4 projectionMatrix =
                    Matrix4x4.Perspective(m_Cam.fieldOfView, camAspect, m_Cam.nearClipPlane, vd.far) *
                    Matrix4x4.Rotate(m_Cam.transform.rotation).inverse;

                projectionMatrix = matScaleBias * projectionMatrix;

                // VBuffer params 
                var vbuff = new VBufferParameters(
                    new Vector3Int(vd.froxelWidth, vd.froxelHeight, vd.froxelDepth),
                    vd.far,
                    m_Cam.nearClipPlane,
                    vd.far,
                    m_Cam.fieldOfView,
                    s.sliceDistributionUniformity);

                Vector4 vres = new Vector4(
                    vd.froxelWidth,
                    vd.froxelHeight,
                    1.0f / vd.froxelWidth,
                    1.0f / vd.froxelHeight);

                Matrix4x4 pixelCoordToViewDirWS = ComputePixelCoordToWorldSpaceViewDirectionMatrix(m_Cam, camAspect, vres);

                // jitter sequence
                int sampleIndex = Time.renderedFrameCount % 7;
                int slowCounter = (Time.renderedFrameCount % 70) / 10; // int div 
                Vector4 seqOffset = new Vector4(
                    m_Res.xySeq[sampleIndex].x,
                    m_Res.xySeq[sampleIndex].y,
                    s_zSeq[sampleIndex],
                    -slowCounter);

                // dimensions
                Vector3 invDim = new Vector3(
                    1.0f / vd.froxelWidth,
                    1.0f / vd.froxelHeight,
                    1.0f / vd.froxelDepth);

                Vector3 invDimStereo = new Vector3(invDim.x * 0.5f, invDim.y, invDim.z);

                // Z-plane texel spacing (same helper)
                float zPlaneTexelSpacing = ComputZPlaneTexelSpacing(1.0f, m_Cam.fieldOfView, vd.froxelHeight);

                // Prev frame matrix for shader
                cmd.SetComputeMatrixParam(s.froxelFogCompute, ID_PreviousFrameMatrix, m_Res.previousFrameMatrix);


                // ------------------------------------------------------------
                // Volume stack controller: 
                // ------------------------------------------------------------
                using (new ProfilingScope(cmd, s_Volumeping))
                {
                    //m_Cam.UpdateVolumeStack();
                    //var stack = m_Res.VolMana.stack;
                    // var vol = stack.GetComponent<UnityEngine.Rendering.Universal.Volumetrics>();
                    var vol = VolumeManager.instance.stack.GetComponent<UnityEngine.Rendering.Universal.Volumetrics>();

                    if (vol != null && vol.active) // active = component enabled + any overrides
                    {
                        // Apply globals for this camera for this pass
                        // vol.SetGlobalsOnCmdBuffer(cmd); //TODO: Refactor and move the logic off the volume system. Causing garbage
                        ApplyVolumeGlobals(cmd, vol);
                    }
                    else
                    {
                        // Defaults if no volume is present 
                        cmd.SetGlobalFloat(ID_GlobalExtinction, VolumeRenderingUtils.ExtinctionFromMeanFreePath(50f));
                        cmd.SetGlobalFloat(ID_StaticLightMultiplier, 1f);
                        cmd.SetGlobalFloat(ID_PanicRefresh, 0f);
                    }
                    
                }

                // ------------------------------------------------------------
                // Clipmaps: update + bind into Scatter
                // ------------------------------------------------------------
                m_Res.clipmaps.RecordTick(cmd, m_Cam.transform.position, s.volumetricData, s.maxClipmapBuildStepsPerFrame); //hard ref for now, //TODO: Add scaler for clipmap res

                // ------------------------------------------------------------
                // Scatter: bind params + dispatch
                // ------------------------------------------------------------
                var fogCS = s.froxelFogCompute;
                int kScatter = m_Res.kScatter;

                // bind clipmaps (scales, centers, textures)
                m_Res.clipmaps.RecordBindToScatter(cmd, fogCS, kScatter, s.volumetricData); //hard ref for now, //TODO: Add scaler for clipmap res

                // history/output
                cmd.SetComputeTextureParam(fogCS, kScatter, ID_PreviousFrameLighting, m_Res.froxel[prev]);
                cmd.SetComputeTextureParam(fogCS, kScatter, ID_Result,               m_Res.froxel[cur]);

                // required scatter uniforms
                cmd.SetComputeFloatParam(fogCS, ID_VBufferUnitDepthTexelSpacing, zPlaneTexelSpacing);
                cmd.SetComputeVectorParam(fogCS, ID_invDimensions, ToV4(invDim));

                // frame index
                cmd.SetGlobalInt(ID_FrameIndex, Time.renderedFrameCount);
                cmd.SetComputeIntParam(fogCS, ID_FrameIndex, Time.renderedFrameCount % 4);

                // noise + debug/foveation
                // if (vd.DefaultTurbulentNoise != null)
                //     cmd.SetComputeTextureParam(fogCS, kScatter, ID_Noise3d, vd.DefaultTurbulentNoise);
                
                // Main Light Check
                
                int main = renderingData.lightData.mainLightIndex;

                bool hasMain =
                    main >= 0 &&
                    main < renderingData.lightData.visibleLights.Length &&
                    renderingData.lightData.visibleLights[main].lightType == UnityEngine.LightType.Directional;

                Vector3 sunDirToSunWS = Vector3.up;
                Color   sunRadiance   = Color.black;

                if (hasMain)
                {
                    var vl = renderingData.lightData.visibleLights[main];

                    // Direction for directional light: to-sun direction = -forward
                    sunDirToSunWS = -(Vector3)vl.localToWorldMatrix.GetColumn(2);
                    sunRadiance   = vl.finalColor; // color * intensity
                }

                //Disabling from the preview window, too distracting  //TODO: Add per camera setting for situations like spectator cam
                #if !UNITY_EDITOR
                bool foveationEnabled = s.foveationEnabled;
                #else
                bool foveationEnabled =  s.foveationEnabled && renderingData.cameraData.cameraType == CameraType.Game;
                #endif
                
            // Uniforms for compute
                cmd.SetComputeIntParam(fogCS, Shader.PropertyToID("_HasMainLight"), hasMain ? 1 : 0);
                // cmd.SetComputeVectorParam(fogCS, Shader.PropertyToID("_SunDirToSunWS"),
                //     new Vector4(sunDirToSunWS.x, sunDirToSunWS.y, sunDirToSunWS.z, 0));
                // cmd.SetComputeVectorParam(fogCS, Shader.PropertyToID("_SunRadiance"),
                //     new Vector4(sunRadiance.r, sunRadiance.g, sunRadiance.b, 1));

                cmd.SetComputeIntParam(fogCS, ID_FoveationEnabled, foveationEnabled  ? 1 : 0);
                cmd.SetComputeFloatParam(fogCS, ID_FoveationInnerRadius, s.foveationInnerRadius);
                cmd.SetComputeFloatParam(fogCS, ID_FoveationOuterRadius, s.foveationOuterRadius);
                cmd.SetComputeFloatParam(fogCS, ID_BakedTurbulence, s.bakedTurbulence);
                cmd.SetComputeIntParam(fogCS, ID_Panic, s.panic ? 1 : 0);

                // scatter constant buffer (PerFrameCB)
                m_Res.scatterArr[0] = new ScatteringPerFrameConstants
                {
                    _VBufferCoordToViewDirWS          = pixelCoordToViewDirWS,
                    _PrevViewProjMatrix_L             = m_Res.prevViewProjMatrix,
                    _ViewMatrix_L                     = m_Cam.worldToCameraMatrix,
                    TransposedCameraProjectionMatrix_L= projectionMatrix.transpose,
                    CameraProjectionMatrix_L          = projectionMatrix,
                    _VBufferDistanceEncodingParams_L  = vbuff.depthEncodingParams,
                    _VBufferDistanceDecodingParams    = vbuff.depthDecodingParams,
                    SeqOffset                         = seqOffset,
                    CameraPosition                     = (Vector4)m_Cam.transform.position,
                    CameraMotionVector                 = (Vector4)(m_Cam.transform.position - m_Res.previousCameraPos)
                };
                m_Res.scatterCB.SetData(m_Res.scatterArr);
//cmd.SetBufferData( m_Res.scatterArr, m_Res.scatterCB );
                // NOTE: we set constant buffer binding directly on the ComputeShader before executing this cmd.
                // In this pass we dispatch once, so this is safe even if the binding isn't recorded per-command.
                fogCS.SetConstantBuffer(ID_PerFrameCB, m_Res.scatterCB, 0, m_Res.scatterCB.stride);

                cmd.DispatchCompute(fogCS, kScatter, m_Res.scatterGX, m_Res.scatterGY, m_Res.scatterGZ);

                // ------------------------------------------------------------
                // Integrate: uniforms + dispatch
                // ------------------------------------------------------------
                var intCS = s.froxelIntegrationCompute;
                int kIntegrate = m_Res.kIntegrate;
                
                bool stereo = renderingData.cameraData.xrRendering;
                int eyeCount = stereo ? 2 : 1;

                // invDimensions for the integrate buffer (NOT always stereo)
                Vector3 invDimIntegrate = new Vector3(
                    1.0f / (vd.froxelWidth * eyeCount),
                    1.0f / vd.froxelHeight,
                    1.0f / vd.froxelDepth);

                cmd.SetComputeVectorParam(intCS, ID_invDimensions, ToV4(invDimIntegrate));
                cmd.SetComputeIntParam(intCS, ID_EyeCount, eyeCount);
                cmd.SetComputeIntParam(intCS, ID_StereoEnabled, stereo ? 1 : 0);

                // Disable the old forced disparity by default.
                // If you still want it as a debug knob, expose it in Settings and set it here when stereo.
                cmd.SetComputeFloatParam(intCS, ID_StereoDisparityOverride, -1.0f);

                // history/output
                cmd.SetComputeTextureParam(intCS, kIntegrate, ID_InLightingTexture, m_Res.froxel[cur]);
                cmd.SetComputeTextureParam(intCS, kIntegrate, ID_Result,            m_Res.integrate);
                
                

                // eye matrices (same derivation as your Initialize)
                Matrix4x4 centerProj = matScaleBias * Matrix4x4.Perspective(m_Cam.fieldOfView, camAspect, vd.near, vd.far);
                Matrix4x4 centerInv  = centerProj.inverse;

                float halfIPD = m_Cam.stereoSeparation * 0.5f;
                

                
                Matrix4x4 leftProj  = matScaleBias * Matrix4x4.Perspective(m_Cam.fieldOfView, camAspect, vd.near, vd.far) *
                                      Matrix4x4.Translate(new Vector3( halfIPD, 0, 0));
                Matrix4x4 rightProj = matScaleBias * Matrix4x4.Perspective(m_Cam.fieldOfView, camAspect, vd.near, vd.far) *
                                      Matrix4x4.Translate(new Vector3(-halfIPD, 0, 0));

                Matrix4x4 leftEyeMatrix  = leftProj  * centerInv;
                Matrix4x4 rightEyeMatrix = rightProj * centerInv;

                // VolZBufferParams (same as your code)
                Vector4 volZ;
                volZ.x = 1.0f - vd.far / vd.near;
                volZ.y = vd.far / vd.near;
                volZ.z = volZ.x / vd.far;
                volZ.w = volZ.y / vd.far;

                cmd.SetComputeMatrixParam(intCS, ID_LeftEyeMatrix,  leftEyeMatrix);
                cmd.SetComputeMatrixParam(intCS, ID_RightEyeMatrix, rightEyeMatrix);
                cmd.SetComputeVectorParam(intCS, ID_VolZBufferParams, volZ);
                cmd.SetComputeVectorParam(intCS, ID_invDimensions, ToV4(invDimStereo));
                cmd.SetComputeIntParam(intCS, ID_FroxelDepthCount, vd.froxelDepth);

                // stereo baseline + focal length (same math you used)
                float fovY = m_Cam.fieldOfView * Mathf.Deg2Rad;
                float fovX = 2.0f * Mathf.Atan(Mathf.Tan(fovY * 0.5f) * camAspect);
                float focalLen = 1.0f / Mathf.Tan(fovX * 0.5f);
                cmd.SetComputeFloatParam(intCS, ID_StereoBaseline, stereo ? halfIPD : 0.0f); // stereo baseline should be 0 in mono
                cmd.SetComputeFloatParam(intCS, ID_StereoFocalLen, focalLen);

                // integrate per-frame CB (decoding params + seq offset)
                m_Res.stepAddArr[0] = new StepAddPerFrameConstants
                {
                    _VBufferDistanceDecodingParams = vbuff.depthDecodingParams,
                    SeqOffset = seqOffset // implicit v4->v3
                };
                m_Res.stepAddCB.SetData(m_Res.stepAddArr);

                intCS.SetConstantBuffer(ID_PerFrameCB, m_Res.stepAddCB, 0, m_Res.stepAddCB.stride);

                cmd.DispatchCompute(intCS, kIntegrate, m_Res.integrateGX, m_Res.integrateGY, 1);

                // publish results
                cmd.SetGlobalTexture(ID_VolumetricResult, m_Res.integrate); //Main integrated result 
                cmd.SetGlobalTexture(ID_InLightingTexture, m_Res.froxel[cur]);   //Preintegration

               
                // update history for next frame
                m_Res.previousFrameMatrix = projectionMatrix;
                m_Res.previousCameraPos   = m_Cam.transform.position;

                var gpuProj = GL.GetGPUProjectionMatrix(Matrix4x4.Perspective(m_Cam.fieldOfView, camAspect, m_Cam.nearClipPlane, 100000f), true);
                m_Res.prevViewProjMatrix = gpuProj * m_Cam.worldToCameraMatrix;

                // update VolumetricsCB contents (optional, but keeps parity with your ShaderConstants)
                m_Res.shaderGlobalsArr[0] = new ShaderConstants
                {
                    TransposedCameraProjectionMatrix = projectionMatrix.transpose,
                    CameraProjectionMatrix = projectionMatrix,
                    _VBufferDistanceEncodingParams = vbuff.depthEncodingParams,
                    _VolumetricResultDim = new Vector4(
                        vd.froxelWidth * eyeCount,
                        vd.froxelHeight,
                        vd.froxelDepth,
                        0f),

                    _VolCameraPos = new Vector4(
                        m_Cam.transform.position.x,
                        m_Cam.transform.position.y,
                        m_Cam.transform.position.z,
                        0f)
                };
                m_Res.shaderGlobalsCB.SetData(m_Res.shaderGlobalsArr);

                // publish global constant buffer (optional, mirrors your old "VolumetricsCB" usage)
                cmd.SetGlobalConstantBuffer(m_Res.shaderGlobalsCB, ID_VolumetricsCB, 0, m_Res.shaderGlobalsCB.stride);
            }

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        static Vector4 ToV4(Vector3 v) => new Vector4(v.x, v.y, v.z, 0f);

        static float GetAspectRatio(in RenderingData renderingData, Camera cam)
        {
            // Preserve your original "XR aspect is H/W" behavior.
            if (!renderingData.cameraData.xrRendering) return cam.aspect;

            var desc = renderingData.cameraData.cameraTargetDescriptor;
            if (desc.width > 0 && desc.height > 0)
                return (float)desc.height / (float)desc.width;

            return XRSettings.eyeTextureHeight == 0
                ? cam.aspect
                : (float)XRSettings.eyeTextureHeight / (float)XRSettings.eyeTextureWidth;
        }

        // ===== Ported helpers from your script =====

        internal static float ComputZPlaneTexelSpacing(float planeDepth, float verticalFoV, float resolutionY)
        {
            float tanHalfVertFoV = Mathf.Tan(0.5f * verticalFoV);
            return tanHalfVertFoV * (2.0f / resolutionY) * planeDepth;
        }

        static Matrix4x4 ComputePixelCoordToWorldSpaceViewDirectionMatrix(Camera cam, float camAspect, Vector4 resolution)
        {
            var proj = Matrix4x4.Perspective(
                cam.fieldOfView,
                camAspect,
                cam.nearClipPlane,
                100000f);

            // pixel -> clip/NDC
            var pixelToClip =
                Matrix4x4.Translate(new Vector3(-1.0f, -1.0f, 0.0f)) *
                Matrix4x4.Scale(new Vector3(2.0f * resolution.z, 2.0f * resolution.w, 1.0f));

            // clip -> view
            var invProj = proj.inverse;

            // rotation only, no translation
            var camToWorld = cam.cameraToWorldMatrix;
            camToWorld.m03 = 0.0f;
            camToWorld.m13 = 0.0f;
            camToWorld.m23 = 0.0f;
            camToWorld.m33 = 1.0f;

            // keep your current sign convention for now
            var worldDirMatrix =
                camToWorld *
                Matrix4x4.Scale(new Vector3(-1.0f, -1.0f, -1.0f)) *
                invProj *
                pixelToClip;

            return worldDirMatrix.transpose;
        }
    }

    // ============================================================
    // Constant buffer structs (match your original layout)
    // ============================================================
    [StructLayout(LayoutKind.Sequential)]
    struct ShaderConstants
    {
        public Matrix4x4 TransposedCameraProjectionMatrix;
        public Matrix4x4 CameraProjectionMatrix;
        public Vector4   _VBufferDistanceEncodingParams;

        // was Vector3 -> make it Vector4 to match 16-byte packing
        public Vector4   _VolumetricResultDim; // xyz used, w padding
        public Vector4   _VolCameraPos;        // xyz used, w padding
    }

    [StructLayout(LayoutKind.Sequential)]
    struct ScatteringPerFrameConstants
    {
        public Matrix4x4 _VBufferCoordToViewDirWS;
        public Matrix4x4 _PrevViewProjMatrix_L;
        public Matrix4x4 _ViewMatrix_L;
        public Matrix4x4 TransposedCameraProjectionMatrix_L;
        public Matrix4x4 CameraProjectionMatrix_L;
        public Vector4   _VBufferDistanceEncodingParams_L;
        public Vector4   _VBufferDistanceDecodingParams;
        public Vector4   SeqOffset;
        public Vector4   CameraPosition;
        public Vector4   CameraMotionVector;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct StepAddPerFrameConstants
    {
        public Vector4 _VBufferDistanceDecodingParams;
        public Vector4 SeqOffset;
    }

    // ============================================================
    // VBufferParameters (ported)
    // ============================================================
    struct VBufferParameters
    {
        public Vector3Int viewportSize;
        public Vector4 depthEncodingParams;
        public Vector4 depthDecodingParams;

        public VBufferParameters(Vector3Int viewportResolution, float depthExtent, float camNear, float camFar, float camVFoV, float sliceDistributionUniformity)
        {
            viewportSize = viewportResolution;

            float aspectRatio = viewportResolution.x / (float)viewportResolution.y;
            float farPlaneHeight = 2.0f * Mathf.Tan(0.5f * camVFoV) * camFar;
            float farPlaneWidth = farPlaneHeight * aspectRatio;
            float farPlaneMaxDim = Mathf.Max(farPlaneWidth, farPlaneHeight);
            float farPlaneDist = Mathf.Sqrt(camFar * camFar + 0.25f * farPlaneMaxDim * farPlaneMaxDim);

            float nearDist = camNear;
            float farDist = Mathf.Min(nearDist + depthExtent, farPlaneDist);

            float c = 2 - 2 * sliceDistributionUniformity;
            c = Mathf.Max(c, 0.001f);

            depthEncodingParams = ComputeLogarithmicDepthEncodingParams(nearDist, farDist, c);
            depthDecodingParams = ComputeLogarithmicDepthDecodingParams(nearDist, farDist, c);
        }

        static Vector4 ComputeLogarithmicDepthEncodingParams(float nearPlane, float farPlane, float c)
        {
            float n = nearPlane;
            float f = farPlane;

            Vector4 depthParams;
            depthParams.y = 1.0f / Mathf.Log(c * (f - n) + 1, 2);
            depthParams.x = Mathf.Log(c, 2) * depthParams.y;
            depthParams.z = n - 1.0f / c;
            depthParams.w = 0.0f;

            return depthParams;
        }

        static Vector4 ComputeLogarithmicDepthDecodingParams(float nearPlane, float farPlane, float c)
        {
            float n = nearPlane;
            float f = farPlane;

            Vector4 depthParams;
            depthParams.x = 1.0f / c;
            depthParams.y = Mathf.Log(c * (f - n) + 1, 2);
            depthParams.z = n - 1.0f / c;
            depthParams.w = 0.0f;

            return depthParams;
        }
    }
    
    ///////////////////// 
    ///  Latch onto the fog toggle in scene view
    /////////////////////
#if UNITY_EDITOR
    void OnEnable()
    {
        _lastFogState = UnityEditor.SceneView.lastActiveSceneView?.sceneViewState.fogEnabled ?? false;
        UnityEditor.SceneView.duringSceneGui += OnSceneGUI;
    }

    void OnDisable()
    {
        UnityEditor.SceneView.duringSceneGui -= OnSceneGUI;
    }

    bool _lastFogState;

    void OnSceneGUI(UnityEditor.SceneView sv)
    {
        // Guard against stale delegate firing after this object has been destroyed
        // (e.g. domain reload). Unity's == null catches destroyed native objects
        // even when the C# reference is still live.
        if (this == null)
        {
            UnityEditor.SceneView.duringSceneGui -= OnSceneGUI;
            return;
        }
        
        bool fog = sv.sceneViewState.fogEnabled;
        if (fog == _lastFogState) return;
        _lastFogState = fog;

        settings.runInSceneView = fog;
        UnityEditor.EditorUtility.SetDirty(this);
    }
#endif
}