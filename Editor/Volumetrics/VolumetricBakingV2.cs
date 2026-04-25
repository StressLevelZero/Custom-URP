using System;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using Unity.Mathematics;
using Object = UnityEngine.Object;
using System.Runtime.InteropServices;

using UnityEngine.Experimental.GlobalIllumination;
using LightType = UnityEngine.LightType;
using UnityEngine.Rendering.Universal;
using static Unity.Mathematics.math;
using UnityEngine.SceneManagement;

using Unity.Collections;
using System.IO;
using UnityEditor.SceneManagement;

namespace SLZ.SLZEditorTools
{
    public static class VolumetricBakingV2
    {
        public static bool IsBaking => udata != null;
        public static event Action<bool> BakeCompleted;

        [StructLayout(LayoutKind.Sequential)]
        struct PointLightData
        {
            public float3 wPos;
            public float  _pad0;

            public float4 color;

            // x = cookie layer, y = strength
            public float4 cookieData;

            // inverse rotation quaternion (x,y,z,w)
            public float4 cookieRotInv;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct ConeLightData
        {
            public float3 wPos;
            public float  pad0;

            public float4 color;

            public float3 dir;
            public float  pad1;

            public float2 coneParams;   // outerCos, inv(innerCos-outerCos)
            public float2 pad2;

            public float3 cookieRight;
            public float  pad3;

            public float3 cookieUp;
            public float  pad4;

            public float4 cookieData;   // x=layer, y=strength, z/w unused
        }

        [StructLayout(LayoutKind.Sequential)]
        struct DirLightData
        {
            public float3 dir;
            public float  pad0;

            public float4 color;

            public float3 cookiePos;
            public float  pad1;

            public float3 cookieRight;
            public float  pad2;

            public float3 cookieUp;
            public float  pad3;

            public float4 cookieData;   // x=layer, y=strength, z=cookieSize, w unused
        }
        [StructLayout(LayoutKind.Sequential)]
        struct AreaLightData
        {
            public float4x4 areaMatrix;
            public float4x4 areaMatrixInv;
            public float3 wPos;
            public float4 color;
            public float3 size;

            // x = cookie layer (>=0) or -1, y = strength
            public float4 cookieData;
        }

        const string SkyMeanRadianceComputeGUID = "5d4bc7a7deea7e447a9b4bff56d30343";
        static ComputeShader s_skyMeanRadianceCompute;
        static ComputeShader SkyMeanRadianceCompute
        {
            get
            {
                if (s_skyMeanRadianceCompute == null)
                {
                    string path = AssetDatabase.GUIDToAssetPath(SkyMeanRadianceComputeGUID);
                    if (string.IsNullOrEmpty(path))
                    {
                        throw new FileNotFoundException(
                            "Volumetric Baking: Failed to load sky mean radiance compute shader by hard-coded GUID (" +
                            SkyMeanRadianceComputeGUID +
                            "). Either the shader is missing or its meta file got regenerated");
                    }

                    s_skyMeanRadianceCompute = AssetDatabase.LoadAssetAtPath<ComputeShader>(path);
                    if (s_skyMeanRadianceCompute == null)
                    {
                        throw new FileNotFoundException(
                            "Volumetric Baking: Failed to load sky mean radiance compute shader by hard-coded GUID (" +
                            SkyMeanRadianceComputeGUID +
                            "). No compute shader was found at the path of the GUID, check for GUID collisions");
                    }
                }

                return s_skyMeanRadianceCompute;
            }
        }
        
                static int FindSkyMeanRadianceKernel(ComputeShader cs)
        {
                try { return cs.FindKernel("KSkyNoGeo_MeanRadiance"); }
            catch { }

            try { return cs.FindKernel("CSMain"); }
            catch { }

            return -1;
        }

        // Note: the old synchronous BakeSceneSkyMeanRadiance has been replaced by the
        // BeginSkyMeanRadianceAsync → PollPendingSkyReadback pair, which avoids the
        // ComputeBuffer.GetData() stall at the end of the bake.

        static SLZ.BakedVolumetricsData SaveOrUpdateBakedVolumetricsDataAsset(Color skyMeanRadianceLinear, int environmentSampleCount)
        {
            string folder = CheckDirectoryAndReturnPath().Replace('\\', '/');
            string assetPath = $"{folder}/BakedVolumetricsData.asset";

            var asset = AssetDatabase.LoadAssetAtPath<SLZ.BakedVolumetricsData>(assetPath);
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<SLZ.BakedVolumetricsData>();
                AssetDatabase.CreateAsset(asset, assetPath);
            }

            Undo.RecordObject(asset, "Update Baked Volumetrics Data");

            asset.meanSkyRadianceLinear = skyMeanRadianceLinear;
            asset.environmentSampleCount = environmentSampleCount;
            asset.sourceScenePath = SceneManager.GetActiveScene().path;
            asset.lastBakeUtcTicks = DateTime.UtcNow.Ticks;

            EditorUtility.SetDirty(asset);

            Debug.Log($"Volumetric Baking: saved BakedVolumetricsData to {assetPath} with mean sky radiance {skyMeanRadianceLinear}");
            return asset;
        }
        
        static void EnsureSceneBindingObject(BakedVolumetricsData bakedData)
        {
            if (bakedData == null)
            {
                Debug.LogError("Volumetric Baking: Cannot bind null BakedVolumetricsData to scene.");
                return;
            }

            Scene activeScene = SceneManager.GetActiveScene();
            if (!activeScene.IsValid() || !activeScene.isLoaded)
            {
                Debug.LogError("Volumetric Baking: Active scene is not valid/loaded.");
                return;
            }

            SLZ.VolumetricSceneBindings bindings = Object.FindFirstObjectByType<SLZ.VolumetricSceneBindings>();

            if (bindings == null)
            {
                var go = new GameObject("—VolumetricSceneBindings—");
                SceneManager.MoveGameObjectToScene(go, activeScene);

                bindings = go.AddComponent<SLZ.VolumetricSceneBindings>();
                //User should never directly interact with this. It's serialized baked data 
                go.hideFlags = HideFlags.NotEditable | HideFlags.HideInHierarchy; 
                bindings.hideFlags = HideFlags.NotEditable;
                
                go.tag = "Untagged";
                go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                go.transform.localScale = Vector3.one;
                // Optional: hide the component in the inspector, not the GameObject itself.
                // This is much safer than hiding the object in the hierarchy.
                
            }

            Undo.RecordObject(bindings, "Assign Baked Volumetrics Data");
            bindings.EditorSetBakedVolumetricsData(bakedData);
            EditorUtility.SetDirty(bindings);

            if (PrefabUtility.IsPartOfPrefabInstance(bindings))
                PrefabUtility.RecordPrefabInstancePropertyModifications(bindings);

            EditorSceneManager.MarkSceneDirty(bindings.gameObject.scene);
        }
        // Adaptive chunk sizing: governs how many samples we dispatch per editor tick.
        // Target a ~120ms chunk so TDR has plenty of headroom on the worst case voxel.
        // These bound the *per-tick* sample count; the overall sample budget per area
        // is still driven by totalAreaSamples.
        const double kTargetChunkMs = 120.0;
        const int kMinChunkSamples = 1;
        const int kMaxChunkSamples = 1024;

        // Phases for the bake state machine. Separating tracing from async-readback
        // phases lets us avoid synchronous GetData() stalls on the critical path.
        enum BakePhase
        {
            Tracing,       // Dispatching ray chunks into the current area's rendertarget.
            SavingArea,    // Area finished tracing; waiting on GPU readback to write to disk.
            BakingSky,     // Dispatched sky mean-radiance kernel; waiting on readback.
            Finalizing     // All GPU work done; doing asset/scene bookkeeping.
        }

        class UpdateLoopData : IDisposable
        {
            public RayTracingShader rtShader;
            public ComputeShader skyShader;
            public RayTracingAccelerationStructure rtStructure;
            public int pointLightCount, coneLightCount, dirLightCount, areaLightCount;
            public ComputeBuffer pointBuffer, coneBuffer, dirBuffer, areaBuffer;

            public bool skyTexIsGenerated = false;
            public Texture skyTexture;
            public RenderTexture rendertarget;

            public Texture2DArray cookieAtlas;
            public bool cookieAtlasIsGenerated = false;
            public Dictionary<Texture, int> cookieToLayer;
            
            public CubemapArray pointCookieArray;
            public bool pointCookieArrayIsGenerated = false;
            public Dictionary<Texture, int> pointCookieToLayer;
            
            public double startTime;
            public const int frameSkip = 5; // Only run update once every 5 frames. Large scenes will crash otherwise, not sure why. Must be at least 5!
            public int currentFrame = 0;

            public int totalSamples;
            public int totalAreaSamples;
            public int environmentSampleCount;
            public int areaLightSampleCount;
            public int currentAreaIndex = 0;
            public int currentChunkIndex = 0;
            // Running count of samples actually dispatched in the current area. Used as the
            // authoritative progress counter instead of (currentChunkIndex * chunkSampleCount),
            // because chunkSampleCount is a moving target once adaptive sizing kicks in.
            public int currentAreaSamplesTraced = 0;
            public int chunkSampleCount;
            public float areaSeed;

            // Bake state machine / async save plumbing.
            public BakePhase phase = BakePhase.Tracing;
            public bool assetEditingOpen = false;
            // Sticky flag: once AsyncGPUReadback fails once in a session we stop trying it
            // and use the synchronous path for the remainder of the bake.
            public bool asyncReadbackDisabled = false;
            public ComputeBuffer pendingMipBuffer;
            public AsyncGPUReadbackRequest pendingAreaReadback;
            public bool pendingAreaReadbackValid = false;
            public string pendingSaveAbsPath;
            public int pendingSaveAreaIndex;
            public AsyncGPUReadbackRequest pendingSkyReadback;
            public bool pendingSkyReadbackValid = false;

            public ComputeBuffer skyColorBuffer;
            public Color bakedSkyMeanRadiance = Color.black;
            public bool hasBakedSkyMeanRadiance = false;

            private bool disposed = false;

            public void Dispose()
            {
                if (cookieAtlasIsGenerated && cookieAtlas)
                    CoreUtils.Destroy(cookieAtlas);
                cookieAtlas = null;
                cookieToLayer = null;
                
                if (pointCookieArrayIsGenerated && pointCookieArray)
                    CoreUtils.Destroy(pointCookieArray);

                pointCookieArray = null;
                pointCookieToLayer = null;
                
                Dispose(true);
                GC.SuppressFinalize(this);
            }
            private void Dispose(bool disposing)
            {
                if (!disposed)
                {
                    if (disposing)
                    {
                        rtShader = null;
                        rtStructure?.Release();
                        pointBuffer?.Release();
                        coneBuffer?.Release();
                        dirBuffer?.Release();
                        areaBuffer?.Release();
                        rtStructure = null;
                        pointBuffer = null;
                        coneBuffer = null;
                        dirBuffer = null;
                        areaBuffer = null;
                        skyColorBuffer?.Release();
                        skyColorBuffer = null;
                        pendingMipBuffer?.Release();
                        pendingMipBuffer = null;
                    }

                    // Textures are funky and won't be destroyed by the finalizer, treat them like unmanaged memory

                    if (skyTexIsGenerated && skyTexture)
                    {
                        CoreUtils.Destroy(skyTexture);
                    }
                    skyTexture = null;

                    if (rendertarget)
                    {
                        CoreUtils.Destroy(rendertarget);
                        rendertarget = null;
                    }

                    disposed = true;
                }
            }

            public void DisposeGraphicsResources()
            {
                rtShader = null;
                rtStructure?.Release();
                pointBuffer?.Release();
                coneBuffer?.Release();
                dirBuffer?.Release();
                areaBuffer?.Release();
                rtStructure = null;
                pointBuffer = null;
                coneBuffer = null;
                dirBuffer = null;
                areaBuffer = null;
                skyColorBuffer?.Release();
                skyColorBuffer = null;
                pendingMipBuffer?.Release();
                pendingMipBuffer = null;

                if (skyTexIsGenerated && skyTexture)
                {
                    CoreUtils.Destroy(skyTexture);
                }
                skyTexture = null;

                if (rendertarget)
                {
                    CoreUtils.Destroy(rendertarget);
                    rendertarget = null;
                }
                if (cookieAtlasIsGenerated && cookieAtlas)
                    CoreUtils.Destroy(cookieAtlas);
                cookieAtlas = null;
                cookieToLayer = null;
                
                if (pointCookieArrayIsGenerated && pointCookieArray)
                    CoreUtils.Destroy(pointCookieArray);

                pointCookieArray = null;
                pointCookieToLayer = null;
            }
        }

        static UpdateLoopData udata;

        #region Shaders

        const string DXRVolumeBakerGUID = "c7c13d3174f650f4fa0202ce218970eb";
        static RayTracingShader s_DXRVolumeBaker;
        static RayTracingShader DXRVolumeBaker
        {
            get
            {
                if (s_DXRVolumeBaker == null)
                {
                    string dxrVolumeBakerPath = AssetDatabase.GUIDToAssetPath(DXRVolumeBakerGUID);
                    if (string.IsNullOrEmpty(dxrVolumeBakerPath))
                    {
                        throw new FileNotFoundException("Volumetric Baking: Failed to load DXR-VolumeBaker raytracing shader by hard-coded GUID (" + DXRVolumeBakerGUID + "). Either the shader is missing or its meta file got regenerated");
                    }
                    s_DXRVolumeBaker = AssetDatabase.LoadAssetAtPath<RayTracingShader>(dxrVolumeBakerPath);
                    if (s_DXRVolumeBaker == null)
                    {
                        throw new FileNotFoundException("Volumetric Baking: Failed to load DXR-VolumeBaker raytracing shader by hard-coded GUID (" + DXRVolumeBakerGUID + "). No shader was found at the path of the GUID, check for GUID collisions");
                    }
                }
                return s_DXRVolumeBaker;
            }
        }

        const string Mip3DTextureGUID = "a7b6f45f3454c3345a78c989cedd7229";
        static ComputeShader s_mip3DCompute;
        static ComputeShader Mip3DCompute
        {
            get
            {
                if (s_mip3DCompute == null)
                {
                    string mip3dComputePath = AssetDatabase.GUIDToAssetPath(Mip3DTextureGUID);
                    if (string.IsNullOrEmpty(mip3dComputePath))
                    {
                        throw new FileNotFoundException("Volumetric Baking: Failed to load Mip3DTexture compute shader by hard-coded GUID (" + Mip3DTextureGUID + "). Either the shader is missing or its meta file got regenerated");
                    }
                    s_mip3DCompute = AssetDatabase.LoadAssetAtPath<ComputeShader>(mip3dComputePath);
                    if (s_mip3DCompute == null)
                    {
                        throw new FileNotFoundException("Volumetric Baking: Failed to load Mip3DTexture compute shader by hard-coded GUID (" + Mip3DTextureGUID + "). No compute shader was found at the path of the GUID, check for GUID collisions");
                    }
                }
                return s_mip3DCompute;
            }
        }

        const string clear3DTextureGUID = "e78dcf06706ad9840b7c3b0ec9f489b7";
        static ComputeShader s_clear3DTex;
        static ComputeShader Clear3DTexShader
        {
            get
            {
                if (s_clear3DTex == null)
                {
                    string clear3DTexturePath = AssetDatabase.GUIDToAssetPath(clear3DTextureGUID);
                    if (string.IsNullOrEmpty(clear3DTexturePath))
                    {
                        throw new FileNotFoundException("Volumetric Baking: Failed to load Clear3DTexture compute shader by hard-coded GUID (" + clear3DTextureGUID + "). Either the shader is missing or its meta file got regenerated");
                    }
                    s_clear3DTex = AssetDatabase.LoadAssetAtPath<ComputeShader>(clear3DTexturePath);
                    if (s_clear3DTex == null)
                    {
                        throw new FileNotFoundException("Volumetric Baking: Failed to load Clear3DTexture compute shader by hard-coded GUID (" + clear3DTextureGUID + "). No compute shader was found at the path of the GUID, check for GUID collisions");
                    }
                }
                return s_clear3DTex;
            }
        }
        #endregion

        public static void BakeDXR(int chunkSampleCount, int environmentSampleCount, int areaLightSampleCount, bool skyboxContributes, Cubemap customSkyTexture)
        {
            int numVolumetricAreas = VolumetricRegisters.volumetricAreas.Count;
            if (numVolumetricAreas == 0)
            {
                Debug.LogWarning("Volumetric Baking: No volumetric areas in loaded scenes");
                return;
            }

            if (udata != null)
            {
                Debug.LogError("Volumetric Baking: global data not cleared. Either a bake is currently in progress or baking was terminated abnormally");
                return;
            }

            udata = new UpdateLoopData();
            
            udata.skyShader = SkyMeanRadianceCompute;
            udata.skyColorBuffer = new ComputeBuffer(1, sizeof(float) * 4, ComputeBufferType.Structured);

            RayTracingShader rtshader = DXRVolumeBaker;
            rtshader.SetShaderPass("BakedRaytrace");

            udata.rtStructure = BuildRTAccelerationStruct();
            rtshader.SetAccelerationStructure("g_SceneAccelStruct", udata.rtStructure);

            List<Light> pointLights, coneLights, dirLights, areaLights;

            GatherBakedLights(
                  out pointLights,
                  out coneLights,
                  out dirLights,
                  out areaLights
                );

            var cookiePack = BuildCookieAtlas(coneLights, dirLights, areaLights, 256);
            udata.cookieAtlas = cookiePack.atlas;
            udata.cookieToLayer = cookiePack.cookieToLayer;
            udata.cookieAtlasIsGenerated = true;            udata.cookieAtlasIsGenerated = true;
            
            var pointCookiePack = BuildPointCookieArray(pointLights);
            udata.pointCookieArray = pointCookiePack.array;
            udata.pointCookieToLayer = pointCookiePack.map;
            udata.pointCookieArrayIsGenerated = true;
            
            udata.rtShader = rtshader;
            udata.pointLightCount = pointLights.Count;
            udata.coneLightCount = coneLights.Count;
            udata.dirLightCount = dirLights.Count;
            udata.areaLightCount = areaLights.Count;
            udata.environmentSampleCount = environmentSampleCount;
            udata.areaLightSampleCount = areaLightSampleCount;
            udata.totalAreaSamples = udata.pointLightCount + udata.coneLightCount + udata.dirLightCount + udata.environmentSampleCount + (udata.areaLightSampleCount * udata.areaLightCount);
            udata.totalSamples = udata.totalAreaSamples * VolumetricRegisters.volumetricAreas.Count;
            udata.chunkSampleCount = chunkSampleCount;
            udata.startTime = EditorApplication.timeSinceStartup;
            //Debug.Log($"Skybox {skyboxContributes}, {pointLights.Count} Point Lights, {coneLights.Count} Cone Lights, {dirLights.Count} Dir Lights, {areaLights.Count} area lights.");

            //Set up buffers with data stride. Keeping a min count of 1 to keep buffer valid. Get's skipped in shader.
            ComputeBuffer pointBuffer = new ComputeBuffer(Mathf.Max(pointLights.Count, 1), Marshal.SizeOf<PointLightData>());
            ComputeBuffer coneBuffer = new ComputeBuffer(Mathf.Max(coneLights.Count, 1), Marshal.SizeOf<ConeLightData>());
            ComputeBuffer dirBuffer = new ComputeBuffer(Mathf.Max(dirLights.Count, 1), Marshal.SizeOf<DirLightData>());
            ComputeBuffer areaBuffer = new ComputeBuffer(Mathf.Max(areaLights.Count, 1), Marshal.SizeOf<AreaLightData>());

            udata.pointBuffer = pointBuffer;
            udata.coneBuffer = coneBuffer;
            udata.dirBuffer = dirBuffer;
            udata.areaBuffer = areaBuffer;

            PopulateLightBuffers(pointBuffer, pointLights, coneBuffer, coneLights, dirBuffer, dirLights, areaBuffer, areaLights, udata.cookieToLayer, udata.pointCookieToLayer);
            Texture skyTex = GetEnvironmentCubemap(skyboxContributes, customSkyTexture);
            udata.skyTexture = skyTex;
            udata.skyTexIsGenerated = customSkyTexture == null && skyboxContributes && skyTex!=null;

            Vector3Int Texels = VolumetricRegisters.volumetricAreas[0].NormalizedTexelDensity;
            RenderTextureDescriptor rtDesc = new RenderTextureDescriptor();
            rtDesc.enableRandomWrite = true;
            rtDesc.dimension = TextureDimension.Tex3D;
            rtDesc.width = Texels.x;
            rtDesc.height = Texels.y;
            rtDesc.volumeDepth = Texels.z;
            rtDesc.graphicsFormat = GraphicsFormat.R16G16B16A16_SFloat;
            rtDesc.msaaSamples = 1;

            //Target buffer
            RenderTexture rendertarget = new RenderTexture(rtDesc);
            rendertarget.Create();

            udata.rendertarget = rendertarget;

            AssetDatabase.StartAssetEditing();
            udata.assetEditingOpen = true;
            udata.phase = BakePhase.Tracing;
            EditorApplication.update += BakeEditorUpdate;
        }

        /// <summary>
        /// Centralized teardown. Every exit from the bake loop (success, cancel,
        /// exception, abort) must go through this method so that
        /// <see cref="AssetDatabase.StopAssetEditing"/> is guaranteed to be called
        /// and all graphics resources are released. Previously the cancel path
        /// leaked both of those.
        /// </summary>
        static void EndBake(bool success, string message = null)
        {
            try
            {
                EditorApplication.update -= BakeEditorUpdate;
                EditorUtility.ClearProgressBar();

                if (udata != null && udata.assetEditingOpen)
                {
                    try { AssetDatabase.StopAssetEditing(); }
                    catch (Exception ex) { Debug.LogException(ex); }
                    udata.assetEditingOpen = false;
                }

                if (!string.IsNullOrEmpty(message))
                {
                    if (success) Debug.Log("[VolBake] " + message);
                    else Debug.LogError("[VolBake] " + message);
                }
            }
            finally
            {
                udata?.Dispose();
                udata = null;
                BakeCompleted?.Invoke(success);
            }
        }

        static void BakeEditorUpdate()
        {
            if (udata == null)
            {
                EditorApplication.update -= BakeEditorUpdate;
                return;
            }

            // Don't run while the asset database is importing
            if (EditorApplication.isUpdating)
            {
                return;
            }

            // Only run the update once every 5 editor updates. Otherwise strange native D3D12 driver crashes occur. Not sure why, maybe unity's not freeing up resources when it should?
            // Adaptive chunk sizing handles *how much* we dispatch; this frameSkip handles *how often* the editor
            // gets to breathe between dispatches. They solve different problems, so we keep both.
            udata.currentFrame = (udata.currentFrame + 1) % UpdateLoopData.frameSkip;
            if (udata.currentFrame != 0)
            {
                // Crashing seems to be linked to the scene view not updating. Skipping 5 frames is fine if the view is set to "always refresh",
                // but if that's disabled it still crashes. Manually force it to update.
                if (!Application.isBatchMode && udata.currentFrame == 1)
                    SceneView.RepaintAll();
                return;
            }

            // Progress + cancel. Cancel routes through EndBake so StopAssetEditing() always fires.
            if (!Application.isBatchMode && udata.phase != BakePhase.Finalizing)
            {
                int currentSampleForUI = udata.currentChunkIndex * udata.chunkSampleCount;
                TimeSpan runningTime = TimeSpan.FromSeconds(EditorApplication.timeSinceStartup - udata.startTime);
                if (EditorUtility.DisplayCancelableProgressBar(
                        $"Baking Volumes ({runningTime:hh\\:mm\\:ss})",
                        $"Volume {udata.currentAreaIndex} / {VolumetricRegisters.volumetricAreas.Count}, Sample {currentSampleForUI} / {udata.totalAreaSamples} [{udata.phase}]",
                        (float)udata.currentAreaIndex / (float)VolumetricRegisters.volumetricAreas.Count))
                {
                    EndBake(false, "Bake cancelled by user.");
                    return;
                }
            }

            try
            {
                switch (udata.phase)
                {
                    case BakePhase.Tracing:
                        TickTracing();
                        break;
                    case BakePhase.SavingArea:
                        PollPendingAreaSave();
                        break;
                    case BakePhase.BakingSky:
                        PollPendingSkyReadback();
                        break;
                    case BakePhase.Finalizing:
                        TickFinalizing();
                        break;
                }
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                EndBake(false, "Bake aborted due to exception: " + ex.Message);
            }
        }

        /// <summary>
        /// Tracing tick: either dispatch another ray chunk into the current area,
        /// or if the area is full, hand off to the async save path.
        /// </summary>
        static void TickTracing()
        {
            // Use the running counter, NOT (currentChunkIndex * chunkSampleCount) —
            // chunkSampleCount changes under adaptive sizing, so reconstructing progress
            // from it is incorrect and causes over-tracing (samples double-count in
            // the additive raygen accumulator).
            int currentSample = udata.currentAreaSamplesTraced;
            bool areaFinishedRendering = currentSample >= udata.totalAreaSamples;

            if (!areaFinishedRendering)
            {
                // Clamp the final chunk so we don't over-trace past totalAreaSamples.
                int raysThisChunk = Mathf.Min(udata.chunkSampleCount, udata.totalAreaSamples - currentSample);
                if (raysThisChunk <= 0)
                {
                    // Defensive: shouldn't happen, but don't dispatch a zero-ray chunk.
                    BeginSaveAreaAsync();
                    return;
                }

                double t0 = EditorApplication.timeSinceStartup;
                TraceChunk(currentSample, raysThisChunk);
                double chunkMs = (EditorApplication.timeSinceStartup - t0) * 1000.0;

                udata.currentAreaSamplesTraced += raysThisChunk;

                // Adaptive resize only kicks in when timing is clearly off target.
                // Bounded on both sides so the bake can't spiral into 1-sample chunks or 16k-sample monsters.
                if (chunkMs > kTargetChunkMs * 1.5)
                    udata.chunkSampleCount = Mathf.Max(kMinChunkSamples, udata.chunkSampleCount / 2);
                else if (chunkMs < kTargetChunkMs * 0.5)
                    udata.chunkSampleCount = Mathf.Min(kMaxChunkSamples, udata.chunkSampleCount * 2);
            }
            else
            {
                BeginSaveAreaAsync();
            }
        }

        /// <summary>
        /// Kick off GPU readback for the just-finished area. Falls back to the
        /// old synchronous path if async readback isn't supported on the platform.
        /// </summary>
        static void BeginSaveAreaAsync()
        {
            string folder = CheckDirectoryAndReturnPath().Replace('\\', '/');
            string assetPath = $"{folder}/Volumemap-{udata.currentAreaIndex}{Vol3d.fileExtension}";
            udata.pendingSaveAbsPath = Path.GetFullPath(assetPath);
            udata.pendingSaveAreaIndex = udata.currentAreaIndex;
            udata.pendingMipBuffer = Get3DMipsBuffer(udata.rendertarget);

            Debug.Log($"[VolBake] Area {udata.currentAreaIndex} trace complete, beginning readback → {assetPath}");

            if (SystemInfo.supportsAsyncGPUReadback && !udata.asyncReadbackDisabled)
            {
                udata.pendingAreaReadback = AsyncGPUReadback.Request(udata.pendingMipBuffer);
                udata.pendingAreaReadbackValid = true;
                udata.phase = BakePhase.SavingArea;
            }
            else
            {
                // Synchronous fallback: platforms without async readback, or after a previous async failure.
                FinishAreaSaveSync();
            }
        }

        /// <summary>
        /// Poll the pending area readback. When done, write the mipchain to disk,
        /// advance to the next area (or finalization), and return to Tracing.
        /// On async readback failure, falls back to the synchronous GetData path
        /// on the still-valid pendingMipBuffer rather than aborting the bake —
        /// AsyncGPUReadback on structured compute buffers has occasional quirks
        /// on some D3D12 driver/Unity combinations.
        /// </summary>
        static void PollPendingAreaSave()
        {
            if (!udata.pendingAreaReadbackValid)
            {
                // Shouldn't happen, but don't spin forever.
                udata.phase = BakePhase.Tracing;
                return;
            }

            if (!udata.pendingAreaReadback.done)
                return;

            Texture3D tex = null;

            if (udata.pendingAreaReadback.hasError)
            {
                // Fall back to synchronous readback on the same buffer. The mip compute
                // already ran, so the data should be present — AsyncGPUReadback just
                // refused to hand it back for some reason Unity doesn't report.
                Debug.LogWarning(
                    $"[VolBake] AsyncGPUReadback reported hasError for area {udata.pendingSaveAreaIndex} " +
                    $"(buffer stride={udata.pendingMipBuffer?.stride}, count={udata.pendingMipBuffer?.count}). " +
                    $"Falling back to synchronous ComputeBuffer.GetData and disabling async readback for this bake."
                );
                udata.asyncReadbackDisabled = true;

                try
                {
                    tex = ReadBufferToTex3D(
                        udata.pendingMipBuffer,
                        udata.rendertarget.width,
                        udata.rendertarget.height,
                        udata.rendertarget.volumeDepth);
                }
                catch (Exception ex)
                {
                    Debug.LogException(ex);
                    udata.pendingAreaReadbackValid = false;
                    udata.pendingMipBuffer?.Release();
                    udata.pendingMipBuffer = null;
                    EndBake(false, $"Both async and sync readback failed for area {udata.pendingSaveAreaIndex}. See above exception.");
                    return;
                }
            }
            else
            {
                var data = udata.pendingAreaReadback.GetData<ushort>();
                tex = ReadRawMipDataToTex3D(
                    data,
                    udata.rendertarget.width,
                    udata.rendertarget.height,
                    udata.rendertarget.volumeDepth);
            }

            Vol3d.WriteTex3DToVol3D(tex, udata.pendingSaveAbsPath);
            UnityEngine.Object.DestroyImmediate(tex);

            long bytes = File.Exists(udata.pendingSaveAbsPath) ? new FileInfo(udata.pendingSaveAbsPath).Length : 0;
            Debug.Log($"[VolBake] Area {udata.pendingSaveAreaIndex} saved — {bytes / 1024} KB at {udata.pendingSaveAbsPath}");

            udata.pendingMipBuffer?.Release();
            udata.pendingMipBuffer = null;
            udata.pendingAreaReadbackValid = false;

            udata.currentChunkIndex = 0;
            udata.currentAreaSamplesTraced = 0;
            udata.currentAreaIndex += 1;

            if (udata.currentAreaIndex >= VolumetricRegisters.volumetricAreas.Count)
            {
                TimeSpan totalTime = TimeSpan.FromSeconds(EditorApplication.timeSinceStartup - udata.startTime);
                Debug.Log(
                    $"[VolBake] All {VolumetricRegisters.volumetricAreas.Count} areas traced " +
                    $"in {totalTime:hh\\:mm\\:ss} — baking sky mean radiance…"
                );
                BeginSkyMeanRadianceAsync();
            }
            else
            {
                udata.phase = BakePhase.Tracing;
            }
        }

        /// <summary>
        /// Synchronous fallback used when AsyncGPUReadback is unavailable.
        /// Preserves the old end-of-area behavior.
        /// </summary>
        static void FinishAreaSaveSync()
        {
            try
            {
                var tex = ReadBufferToTex3D(
                    udata.pendingMipBuffer,
                    udata.rendertarget.width,
                    udata.rendertarget.height,
                    udata.rendertarget.volumeDepth);
                Vol3d.WriteTex3DToVol3D(tex, udata.pendingSaveAbsPath);
                UnityEngine.Object.DestroyImmediate(tex);

                long bytes = File.Exists(udata.pendingSaveAbsPath) ? new FileInfo(udata.pendingSaveAbsPath).Length : 0;
                Debug.Log($"[VolBake] Area {udata.pendingSaveAreaIndex} saved (sync) — {bytes / 1024} KB");
            }
            finally
            {
                udata.pendingMipBuffer?.Release();
                udata.pendingMipBuffer = null;
            }

            udata.currentChunkIndex = 0;
            udata.currentAreaSamplesTraced = 0;
            udata.currentAreaIndex += 1;

            if (udata.currentAreaIndex >= VolumetricRegisters.volumetricAreas.Count)
            {
                BeginSkyMeanRadianceAsync();
            }
            else
            {
                udata.phase = BakePhase.Tracing;
            }
        }

        /// <summary>
        /// Kick off the sky mean-radiance compute and its readback. Previously this
        /// was a synchronous GetData() call on the critical path.
        /// </summary>
        static void BeginSkyMeanRadianceAsync()
        {
            udata.phase = BakePhase.BakingSky;

            if (udata.skyShader == null || udata.skyTexture == null || udata.skyColorBuffer == null)
            {
                Debug.LogWarning("Volumetric Baking: Sky inputs missing; skipping sky mean radiance bake.");
                udata.phase = BakePhase.Finalizing;
                return;
            }

            int kernel;
            try
            {
                kernel = SLZ.SkyMeanRadianceUtility.FindKernel(udata.skyShader);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                udata.phase = BakePhase.Finalizing;
                return;
            }

            CommandBuffer cmd = CommandBufferPool.Get("Bake Mean Sky Radiance");
            try
            {
                cmd.Clear();
                SLZ.SkyMeanRadianceUtility.Dispatch(
                    cmd,
                    udata.skyShader,
                    kernel,
                    udata.skyTexture,
                    udata.skyColorBuffer,
                    udata.environmentSampleCount,
                    0x51A7C3Du,
                    0.0f,
                    0);
                Graphics.ExecuteCommandBuffer(cmd);
            }
            finally
            {
                CommandBufferPool.Release(cmd);
            }

            if (SystemInfo.supportsAsyncGPUReadback && !udata.asyncReadbackDisabled)
            {
                udata.pendingSkyReadback = AsyncGPUReadback.Request(udata.skyColorBuffer);
                udata.pendingSkyReadbackValid = true;
            }
            else
            {
                // Sync fallback
                Vector4[] result = new Vector4[1];
                udata.skyColorBuffer.GetData(result);
                udata.bakedSkyMeanRadiance = new Color(result[0].x, result[0].y, result[0].z, result[0].w);
                udata.hasBakedSkyMeanRadiance = true;
                Debug.Log($"Volumetric Baking: baked mean sky radiance (sync) = {result[0]}");
                udata.phase = BakePhase.Finalizing;
            }
        }

        static void PollPendingSkyReadback()
        {
            if (!udata.pendingSkyReadbackValid)
            {
                udata.phase = BakePhase.Finalizing;
                return;
            }

            if (!udata.pendingSkyReadback.done)
                return;

            if (udata.pendingSkyReadback.hasError)
            {
                // Same quirk as area save: fall back to sync GetData on the same buffer.
                Debug.LogWarning("[VolBake] Sky mean radiance async readback reported hasError; falling back to synchronous GetData.");
                udata.asyncReadbackDisabled = true;
                udata.pendingSkyReadbackValid = false;

                try
                {
                    Vector4[] result = new Vector4[1];
                    udata.skyColorBuffer.GetData(result);
                    udata.bakedSkyMeanRadiance = new Color(result[0].x, result[0].y, result[0].z, result[0].w);
                    udata.hasBakedSkyMeanRadiance = true;
                    Debug.Log($"Volumetric Baking: baked mean sky radiance (sync fallback) = {result[0]}");
                }
                catch (Exception ex)
                {
                    Debug.LogException(ex);
                    Debug.LogError("Volumetric Baking: sky mean radiance sync fallback also failed; continuing finalization with black sky.");
                }

                udata.phase = BakePhase.Finalizing;
                return;
            }

            var data = udata.pendingSkyReadback.GetData<float>();
            if (data.Length >= 4)
            {
                udata.bakedSkyMeanRadiance = new Color(data[0], data[1], data[2], data[3]);
                udata.hasBakedSkyMeanRadiance = true;
                Debug.Log($"Volumetric Baking: baked mean sky radiance = ({data[0]}, {data[1]}, {data[2]}, {data[3]})");
            }
            else
            {
                Debug.LogWarning("Volumetric Baking: sky readback returned unexpected data size.");
            }

            udata.pendingSkyReadbackValid = false;
            udata.phase = BakePhase.Finalizing;
        }

        /// <summary>
        /// All GPU work is done. Release graphics resources, close asset-editing,
        /// write the BakedVolumetricsData asset, assign per-area Texture3Ds, and
        /// fire the BakeCompleted event.
        /// </summary>
        static void TickFinalizing()
        {
            Color bakedSkyColor = udata.bakedSkyMeanRadiance;
            bool hasSkyColor = udata.hasBakedSkyMeanRadiance;
            int envSampleCount = udata.environmentSampleCount;

            udata.DisposeGraphicsResources();

            // Close asset editing before we start touching the asset database for writes.
            if (udata.assetEditingOpen)
            {
                try { AssetDatabase.StopAssetEditing(); }
                catch (Exception ex) { Debug.LogException(ex); }
                udata.assetEditingOpen = false;
            }

            if (hasSkyColor)
            {
                var bakedAsset = SaveOrUpdateBakedVolumetricsDataAsset(bakedSkyColor, envSampleCount);
                EnsureSceneBindingObject(bakedAsset);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            AssignTexturesToVolumes();

            if (!Application.isBatchMode)
                VolumetricRegisters.MarkClipmapDirty();

            TimeSpan totalTime = TimeSpan.FromSeconds(EditorApplication.timeSinceStartup - udata.startTime);
            EndBake(true, $"Bake complete in {totalTime:hh\\:mm\\:ss}.");
        }

        static RayTracingAccelerationStructure BuildRTAccelerationStruct()
        {
            RayTracingAccelerationStructure accelerationStructure = new RayTracingAccelerationStructure();
            List<Renderer> renderers = GatherStaticRenderers();
            RayTracingSubMeshFlags[] smflags = new RayTracingSubMeshFlags[64];
            for (int smIdx = 0; smIdx < 64; smIdx++)
            {
                smflags[smIdx] = RayTracingSubMeshFlags.Enabled;
            }

            for (int rIdx = 0; rIdx < renderers.Count; rIdx++)
            {
                Material[] mats = renderers[rIdx].sharedMaterials;
                int smCount = mats.Length;
                bool hasShownWarning = false;
                for (int smIdx = 0; smIdx < smCount; smIdx++)
                {
                    if (mats[smIdx] == null)
                    {
                        if (!hasShownWarning)
                        {
                            Debug.LogWarning($"Volumetric Baking: Renderer with unpopulated material slots, fix this! : {AnimationUtility.CalculateTransformPath(renderers[rIdx].transform, null)}");
                            hasShownWarning = true;
                        }
                        smflags[smIdx] = RayTracingSubMeshFlags.Disabled;
                        continue;
                    }
                    if (mats[smIdx].renderQueue < 2450)
                    {
                        smflags[smIdx] = RayTracingSubMeshFlags.Enabled;
                    }
                    else
                    {
                        smflags[smIdx] = RayTracingSubMeshFlags.Disabled;
                    }
                }
                accelerationStructure.AddInstance(renderers[rIdx], smflags);
            }
            accelerationStructure.Build();
            return accelerationStructure;
        }

        static List<Renderer> GatherStaticRenderers()
        {
            Renderer[] allRenderers = Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            List<Renderer> staticRenderers = new List<Renderer>(allRenderers.Length);
            for (int rIdx = 0; rIdx < allRenderers.Length; rIdx++)
            {
                if (GameObjectUtility.AreStaticEditorFlagsSet(allRenderers[rIdx].gameObject, StaticEditorFlags.ContributeGI) &&
                        allRenderers[rIdx].shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.Off)
                {
                    staticRenderers.Add(allRenderers[rIdx]);
                }
            }
            staticRenderers.Capacity = staticRenderers.Count;
            return staticRenderers;
        }

        static void GatherBakedLights(
            out List<Light> PointLights,
            out List<Light> ConeLights,
            out List<Light> DirectionalLights,
            out List<Light> AreaLights
            )
        {
            Light[] lights = Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude, FindObjectsSortMode.None); //TODO: Make it smarter to find only baked lights affecting zone.
                                                                                                                     //List<Light> filteredLights = new List<Light>();
            PointLights = new List<Light>();
            ConeLights = new List<Light>();
            DirectionalLights = new List<Light>();
            AreaLights = new List<Light>();

            for (int lIdx = 0; lIdx < lights.Length; lIdx++)
            {
                Light light = lights[lIdx];
                if (light.isActiveAndEnabled && (light.lightmapBakeType == LightmapBakeType.Baked || light.lightmapBakeType == LightmapBakeType.Mixed))
                {
                    switch (light.type)
                    {
                        case LightType.Point:
                            PointLights.Add(light);
                            break;

                        case LightType.Spot:
                            ConeLights.Add(light);
                            break;

                        case LightType.Directional:
                            DirectionalLights.Add(light);
                            break;
#if UNITY_6000_0_OR_NEWER
                        case LightType.Rectangle:
#else
                        case LightType.Area:
#endif
                            AreaLights.Add(light);
                            break;

                        case LightType.Disc:
                            AreaLights.Add(light); //Stacking area and disc
                            break;

                        default:
                            break;
                    }
                }
            }

            return;
        }


        static float4 ResolveLightColor(Light light)
        {
            var ald = light.gameObject.GetComponent<UniversalAdditionalLightData>();
            Color colorModulation = light.color.linear;
            if (light.useColorTemperature) colorModulation *= Mathf.CorrelatedColorTemperatureToRGB(light.colorTemperature);
            colorModulation *= light.intensity;
            if (ald.advancedOptions) colorModulation *= ald.volumetricDimmer;
            return float4(colorModulation.r, colorModulation.g, colorModulation.b, colorModulation.a);
        }
        
        static CubemapArray CreateWhitePointCookieArray()
{
    var arr = new CubemapArray(1, 1, TextureFormat.RGBA32, false);
    arr.wrapMode = TextureWrapMode.Clamp;
    arr.filterMode = FilterMode.Bilinear;

    var cols = new Color[] { Color.white };
    for (int face = 0; face < 6; face++)
        arr.SetPixels(cols, (CubemapFace)face, 0);

    arr.Apply(false, true);
    return arr;
}

static (CubemapArray array, Dictionary<Texture, int> map) BuildPointCookieArray(List<Light> pointLights)
{
    var map = new Dictionary<Texture, int>();

    if (!SystemInfo.supportsCubemapArrayTextures)
        return (CreateWhitePointCookieArray(), map); // safe fallback :contentReference[oaicite:3]{index=3}

    // Collect unique cubemap cookies
    Cubemap first = null;
    foreach (var l in pointLights)
    {
        if (l.cookie == null) continue;

        var cm = l.cookie as Cubemap;
        if (cm == null)
        {
            Debug.LogWarning($"Point light cookie is not a Cubemap and will be ignored: {l.name}");
            continue;
        }

        if (first == null) first = cm;

        if (!map.ContainsKey(cm))
            map.Add(cm, map.Count);
    }

    if (first == null || map.Count == 0)
        return (CreateWhitePointCookieArray(), map);

    int size = first.width;
    var fmt = first.format;

    var arr = new CubemapArray(size, map.Count, fmt, false);
    arr.wrapMode = TextureWrapMode.Clamp;
    arr.filterMode = FilterMode.Bilinear;

    foreach (var kvp in map)
    {
        var cm = (Cubemap)kvp.Key;
        int slice = kvp.Value;

        if (cm.width != size || cm.height != size || cm.format != fmt)
        {
            Debug.LogWarning(
                $"Skipping point cookie '{cm.name}' (size/format mismatch). " +
                $"Expected {size} {fmt}, got {cm.width} {cm.format}."
            );
            continue;
        }

        // For cubemap arrays, Unity uses a single "element" index for subresources.
        // In practice, that element is addressed as slice*6 + face (6 faces per cube).
        for (int face = 0; face < 6; face++)
        {
            int dstElement = slice * 6 + face;
            Graphics.CopyTexture(cm, face, 0, arr, dstElement, 0);
        }
    }

    arr.Apply(false, true);
    return (arr, map);
}
        
        static int GetCookieLayer(Texture cookie, Dictionary<Texture, int> map)
        {
            if (cookie == null || map == null) return -1;
            return map.TryGetValue(cookie, out int layer) ? layer : -1;
        }

        static Texture2DArray CreateWhiteCookieAtlas()
        {
            var arr = new Texture2DArray(1, 1, 1, TextureFormat.RGBA32, false, true);
            arr.wrapMode = TextureWrapMode.Clamp;
            arr.filterMode = FilterMode.Bilinear;
            arr.SetPixels32(new[] { new Color32(255, 255, 255, 255) }, 0);
            arr.Apply(false, true);
            return arr;
        }

        static (Texture2DArray atlas, Dictionary<Texture, int> cookieToLayer) BuildCookieAtlas(
            List<Light> coneLights,
            List<Light> dirLights,
            List<Light> areaLights,
            int atlasSize = 256)
        {
            var cookieToLayer = new Dictionary<Texture, int>();

            void Collect(Light l)
            {
                var t = l.cookie;
                if (t != null && !cookieToLayer.ContainsKey(t))
                    cookieToLayer.Add(t, cookieToLayer.Count);
            }

            foreach (var l in coneLights) Collect(l);
            foreach (var l in dirLights) Collect(l);
            foreach (var l in areaLights) Collect(l);

            if (cookieToLayer.Count == 0)
                return (CreateWhiteCookieAtlas(), cookieToLayer);

            var atlas = new Texture2DArray(atlasSize, atlasSize, cookieToLayer.Count, TextureFormat.RGBA32, false, true);
            atlas.wrapMode = TextureWrapMode.Clamp;
            atlas.filterMode = FilterMode.Bilinear;

            foreach (var kvp in cookieToLayer)
            {
                Texture src = kvp.Key;
                int layer = kvp.Value;

                // Blit -> ReadPixels -> Copy into array layer (works for most cookie texture types)
                var rt = RenderTexture.GetTemporary(atlasSize, atlasSize, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                Graphics.Blit(src, rt);

                var prev = RenderTexture.active;
                RenderTexture.active = rt;

                var tmp = new Texture2D(atlasSize, atlasSize, TextureFormat.RGBA32, false, true);
                tmp.ReadPixels(new Rect(0, 0, atlasSize, atlasSize), 0, 0);
                tmp.Apply(false, true);

                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);

                Graphics.CopyTexture(tmp, 0, 0, atlas, layer, 0);
                Object.DestroyImmediate(tmp);
            }

            atlas.Apply(false, true);
            return (atlas, cookieToLayer);
        }
        static void PopulateLightBuffers(
            ComputeBuffer pointBuffer, List<Light> pointLights,
            ComputeBuffer coneBuffer, List<Light> coneLights,
            ComputeBuffer dirBuffer, List<Light> dirLights,
            ComputeBuffer areaBuffer, List<Light> areaLights,
            Dictionary<Texture, int> cookieToLayer, Dictionary<Texture, int> pointCookieToLayer)
        {
            PointLightData[] pointDatas = new PointLightData[pointLights.Count];
            ConeLightData[] coneDatas = new ConeLightData[coneLights.Count];
            DirLightData[] dirDatas = new DirLightData[dirLights.Count];
            AreaLightData[] areaDatas = new AreaLightData[areaLights.Count];

            for (int pIdx = 0; pIdx < pointLights.Count; pIdx++)
            {
                var l = pointLights[pIdx];

                pointDatas[pIdx].wPos = l.transform.position;
                pointDatas[pIdx]._pad0 = 0;
                pointDatas[pIdx].color = ResolveLightColor(l);

                int layer = -1;
                float strength = 0f;

                if (l.cookie != null && pointCookieToLayer != null && pointCookieToLayer.TryGetValue(l.cookie, out int foundLayer))
                {
                    layer = foundLayer;
                    strength = 1f; // if later we add a “cookie intensity” knob, pipe it here
                }

                pointDatas[pIdx].cookieData = new float4(layer, strength, 0, 0);

                Quaternion inv = Quaternion.Inverse(l.transform.rotation);
                pointDatas[pIdx].cookieRotInv = new float4(inv.x, inv.y, inv.z, inv.w);
            }

            for (int cIdx = 0; cIdx < coneLights.Count; cIdx++)
            {
                var l = coneLights[cIdx];
                coneDatas[cIdx].wPos = l.transform.position;
                coneDatas[cIdx].color = ResolveLightColor(l);
                coneDatas[cIdx].dir = l.transform.forward;

                float flPhiDot = saturate(cos(l.spotAngle * 0.5f * Mathf.Deg2Rad));
                float flThetaDot = saturate(cos(l.innerSpotAngle * 0.5f * Mathf.Deg2Rad));
                coneDatas[cIdx].coneParams = float2(flPhiDot, 1.0f / Mathf.Max(0.01f, flThetaDot - flPhiDot));

                coneDatas[cIdx].cookieRight = l.transform.right;
                coneDatas[cIdx].cookieUp = l.transform.up;

                int layer = GetCookieLayer(l.cookie, cookieToLayer);
                coneDatas[cIdx].cookieData = float4(layer, 1.0f, 0.0f, 0.0f);
            }

            for (int dIdx = 0; dIdx < dirLights.Count; dIdx++)
            {
                var l = dirLights[dIdx];
                dirDatas[dIdx].dir = l.transform.forward;
                dirDatas[dIdx].color = ResolveLightColor(l);

                dirDatas[dIdx].cookiePos = l.transform.position;
                dirDatas[dIdx].cookieRight = l.transform.right;
                dirDatas[dIdx].cookieUp = l.transform.up;

                int layer = GetCookieLayer(l.cookie, cookieToLayer);
                float cookieSize = Mathf.Max(1e-4f, l.cookieSize);
                dirDatas[dIdx].cookieData = float4(layer, 1.0f, cookieSize, 0.0f);
            }

            for (int aIdx = 0; aIdx < areaLights.Count; aIdx++)
            {
                areaDatas[aIdx].wPos = areaLights[aIdx].transform.position;
                areaDatas[aIdx].areaMatrix = Matrix4x4.TRS(areaLights[aIdx].transform.position, areaLights[aIdx].transform.rotation, Vector3.one);
                areaDatas[aIdx].areaMatrixInv = math.inverse(areaDatas[aIdx].areaMatrix);
                areaDatas[aIdx].color = ResolveLightColor(areaLights[aIdx]);
                areaDatas[aIdx].size = float3(areaLights[aIdx].areaSize.x, areaLights[aIdx].areaSize.y, areaLights[aIdx].type == LightType.Disc ? 1 : 0); //Packing for area or disc logic
                int layer = -1;
                if (areaLights[aIdx].cookie != null && cookieToLayer != null &&
                    cookieToLayer.TryGetValue(areaLights[aIdx].cookie, out int foundLayer))
                {
                    layer = foundLayer;
                }

                areaDatas[aIdx].cookieData = new float4(layer, 1.0f, 0, 0);
            }

            pointBuffer.SetData(pointDatas);
            coneBuffer.SetData(coneDatas);
            dirBuffer.SetData(dirDatas);
            areaBuffer.SetData(areaDatas);
        }

        static Texture GetEnvironmentCubemap(bool useSkybox, Cubemap customTexture)
        {

            //Black Background
            if (!useSkybox)
            {
                return AssetDatabase.LoadAssetAtPath<Cubemap>(AssetDatabase.GUIDToAssetPath("56b7b692370e11940b6f0323bf47a14a"));
            }

            //Generate Skybox
            if (customTexture == null)
            {

                RenderTexture cubetex = new RenderTexture(256, 256, 1, GraphicsFormat.R16G16B16A16_SFloat);
                cubetex.enableRandomWrite = true;
                cubetex.dimension = UnityEngine.Rendering.TextureDimension.Cube;
                cubetex.Create();

                Camera renderCam = new GameObject().AddComponent<Camera>();
                float oldFogDensity = UnityEngine.RenderSettings.fogDensity;
                bool oldFogEnabled = UnityEngine.RenderSettings.fog;
                UnityEngine.RenderSettings.fog = false;
                UnityEngine.RenderSettings.fogDensity = 0 ;
                renderCam.gameObject.AddComponent<SkipVolumetricsTag>();
                renderCam.cullingMask = 0;
                renderCam.backgroundColor = Color.black;
                renderCam.clearFlags = CameraClearFlags.Skybox;
                renderCam.gameObject.hideFlags = HideFlags.DontSave;
                renderCam.RenderToCubemap(cubetex);
                CoreUtils.Destroy(renderCam.gameObject);
                UnityEngine.RenderSettings.fogDensity = oldFogDensity;
                UnityEngine.RenderSettings.fog = oldFogEnabled;
                return cubetex;
            }
            
            //Use Provided skybox
            return customTexture;
        }






        static int id_startIdx = Shader.PropertyToID("StartRayIdx");
        static int id_PointLightCount = Shader.PropertyToID("PointLightCount");
        static int id_PLD = Shader.PropertyToID("PLD");
        static int id_ConeLightCount = Shader.PropertyToID("ConeLightCount");
        static int id_CLD = Shader.PropertyToID("CLD");
        static int id_DirLightCount = Shader.PropertyToID("DirLightCount");
        static int id_DLD = Shader.PropertyToID("DLD");
        static int id_AreaLightCount = Shader.PropertyToID("AreaLightCount");
        static int id_AreaLightSamples = Shader.PropertyToID("AreaLightSamples");
        static int id_ALD = Shader.PropertyToID("ALD");
        static int id__SkyTexture = Shader.PropertyToID("_SkyTexture");
        static int id_EnvLightSamples = Shader.PropertyToID("EnvLightSamples");
        static int id_PerDispatchRayCount = Shader.PropertyToID("PerDispatchRayCount");
        static int id_Size = Shader.PropertyToID("Size");
        static int id_WPosition = Shader.PropertyToID("WPosition");
        static int id__Seed = Shader.PropertyToID("_Seed");
        static int id_HalfVoxelSize = Shader.PropertyToID("HalfVoxelSize");
        static int id_g_Output = Shader.PropertyToID("g_Output");
        static int id__LightCookies = Shader.PropertyToID("_LightCookies");
        static int id__PointCookies = Shader.PropertyToID("_PointCookies");
        static int id__OutColor   = Shader.PropertyToID("_OutColor");
        static int id__GlobalSeed = Shader.PropertyToID("_GlobalSeed");
        static int id__MipLevel   = Shader.PropertyToID("_MipLevel");
        
        /// <summary>
        /// Traces a chunk of rays for the current volume. The caller is responsible for
        /// tracking <paramref name="sampleStart"/> (running sample count within the area)
        /// and <paramml name="raysThisChunk"/> (the number of samples to dispatch on this
        /// tick). Automatically clears the global rendertarget on the first chunk of an
        /// area. Does not advance the area index; that is handled by the poll/save path.
        /// </summary>
        /// <param name="sampleStart">
        /// Running count of samples already dispatched for the current area. Becomes
        /// <c>StartRayIdx</c> in the raygen shader.
        /// </param>
        /// <param name="raysThisChunk">
        /// Number of sample slots to dispatch on this tick. Passed explicitly (rather than
        /// using udata.chunkSampleCount) so the last chunk of an area can be clamped to
        /// avoid over-tracing.
        /// </param>
        static void TraceChunk(int sampleStart, int raysThisChunk)
        {
            int areaIdx = udata.currentAreaIndex;
            int totalSamplesAllAreas = udata.totalAreaSamples * VolumetricRegisters.volumetricAreas.Count;
            float overallPct = totalSamplesAllAreas > 0
                ? 100f * (udata.currentAreaIndex * udata.totalAreaSamples + sampleStart)
                  / (float)totalSamplesAllAreas
                : 0f;
            float areaPct = udata.totalAreaSamples > 0
                ? 100f * sampleStart / (float)udata.totalAreaSamples
                : 0f;

            var currentArea = VolumetricRegisters.volumetricAreas[areaIdx];

            TimeSpan elapsed2 = TimeSpan.FromSeconds(EditorApplication.timeSinceStartup - udata.startTime);
            if (udata.currentChunkIndex == 0)
            {
                Debug.Log($"[VolBake] Baking area {udata.currentAreaIndex + 1} [{currentArea.name}] Resolution [ {currentArea.NormalizedTexelDensity} ] in scene [{currentArea.gameObject.scene.name}]");
            }
            Debug.Log(
                $"[VolBake] [{elapsed2:hh\\:mm\\:ss}] " +
                $"Area {udata.currentAreaIndex + 1}/{VolumetricRegisters.volumetricAreas.Count} " +
                $"| Chunk {udata.currentChunkIndex} (rays={raysThisChunk}, chunkSize={udata.chunkSampleCount}) " +
                $"| Area {areaPct:F0}% ({sampleStart}/{udata.totalAreaSamples} samples) " +
                $"| Overall {overallPct:F1}%"
            );
            Vector3Int resolution = currentArea.NormalizedTexelDensity;
            int3 threads = int3(resolution.x, resolution.y, resolution.z);
            Vector3 boxSize = currentArea.BoxScale;
            float maxVoxelSize = max(boxSize.x / (float)resolution.x, math.max(boxSize.y / (float)resolution.y, boxSize.z / (float)resolution.z));

 

            if (udata.currentChunkIndex == 0)
            {
                udata.areaSeed = (UnityEngine.Random.Range(0.0f, 64.0f));
                ReallocateRendertarget(udata.rendertarget, int3(resolution.x, resolution.y, resolution.z));
                Clear3DRendertexture(udata.rendertarget);
            }

            RayTracingShader rtshader = udata.rtShader;

            // try/finally guarantees the pooled command buffer is released even if dispatch throws.
            CommandBuffer cmd = CommandBufferPool.Get("VolBake TraceChunk");
            try
            {
                cmd.Clear();

                cmd.SetRayTracingIntParam(rtshader, id_PointLightCount, udata.pointLightCount);
                cmd.SetRayTracingBufferParam(rtshader, id_PLD, udata.pointBuffer);

                //Cone
                cmd.SetRayTracingIntParam(rtshader, id_ConeLightCount, udata.coneLightCount);
                cmd.SetRayTracingBufferParam(rtshader, id_CLD, udata.coneBuffer);

                //Directional
                cmd.SetRayTracingIntParam(rtshader, id_DirLightCount, udata.dirLightCount);
                cmd.SetRayTracingBufferParam(rtshader, id_DLD, udata.dirBuffer);

                //Area
                cmd.SetRayTracingIntParam(rtshader, id_AreaLightCount, udata.areaLightCount);
                cmd.SetRayTracingIntParam(rtshader, id_AreaLightSamples, udata.areaLightSampleCount);
                cmd.SetRayTracingBufferParam(rtshader, id_ALD, udata.areaBuffer);

                //Env
                cmd.SetRayTracingTextureParam(rtshader, id__SkyTexture, udata.skyTexture);
                cmd.SetRayTracingIntParam(rtshader, id_EnvLightSamples, udata.environmentSampleCount);
                // NOTE: raysThisChunk, not chunkSampleCount. Last chunk of an area may be partial.
                cmd.SetRayTracingIntParam(rtshader, id_PerDispatchRayCount, raysThisChunk);

                //Cookies
                cmd.SetRayTracingTextureParam(rtshader, id__LightCookies, udata.cookieAtlas);
                cmd.SetRayTracingTextureParam(rtshader, id__PointCookies, udata.pointCookieArray);

                cmd.SetRayTracingVectorParam(rtshader, id_Size, currentArea.NormalizedScale);
                cmd.SetRayTracingVectorParam(rtshader, id_WPosition, currentArea.Corner);
                cmd.SetRayTracingFloatParam(rtshader, id__Seed, udata.areaSeed);
                cmd.SetRayTracingFloatParam(rtshader, id_HalfVoxelSize, maxVoxelSize * 0.5f);

                cmd.SetRayTracingTextureParam(rtshader, id_g_Output, udata.rendertarget);

                cmd.SetRayTracingIntParam(rtshader, id_startIdx, sampleStart);
                cmd.DispatchRays(rtshader, "MainRayGenShader", (uint)threads.x, (uint)threads.y, (uint)threads.z);
                Graphics.ExecuteCommandBuffer(cmd);
            }
            finally
            {
                CommandBufferPool.Release(cmd);
            }

            udata.currentChunkIndex += 1;
        }


        static string CheckDirectoryAndReturnPath()
        {
            //Define path 
            string path = SceneManager.GetActiveScene().path;
            path = path.Replace(".unity", "");
            if (!Directory.Exists(path)) //Check if path exists
            {
                Directory.CreateDirectory(path); //if it doesn't, create it
                Debug.Log("Made Directory " + path);
                AssetDatabase.Refresh();
            }
            return path;
        }

        // Note: the old synchronous SaveAreaToDisk has been replaced by the
        // BeginSaveAreaAsync → PollPendingAreaSave pair (with FinishAreaSaveSync
        // as a fallback when AsyncGPUReadback is unavailable).

        // static void AssignTexturesToVolumes()
        // {
        //     List<BakedVolumetricArea> volumes = VolumetricRegisters.volumetricAreas;
        //     string basePath = Path.Combine(CheckDirectoryAndReturnPath(), $"Volumemap-");
        //     int numVolumes = volumes.Count;
        //     for (int vIdx = 0; vIdx < numVolumes; vIdx++)
        //     {
        //         string path = basePath + vIdx.ToString() + Vol3d.fileExtension;
        //         Texture3D volumeTex = (Texture3D)AssetDatabase.LoadAssetAtPath(path, typeof(Texture3D));
        //         if (volumeTex == null)
        //         {
        //             Debug.LogError($"Volumetric Baking: 3D texture for volume {vIdx} not found at {path}");
        //         }
        //         volumes[vIdx].bakedTexture = volumeTex;
        //         EditorSceneManager.MarkSceneDirty(volumes[vIdx].gameObject.scene);
        //     }
        // }
        public static void AssignTexturesToVolumes()
        {
            var volumes = VolumetricRegisters.volumetricAreas;
            string folder = CheckDirectoryAndReturnPath().Replace('\\','/'); // Assets/.. path
            int numVolumes = volumes.Count;

            for (int vIdx = 0; vIdx < numVolumes; vIdx++)
            {
                string assetPath = $"{folder}/Volumemap-{vIdx}{Vol3d.fileExtension}";

                // Ensure importer has produced the Texture3D before we load
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);

                // First try main asset
                var tex = AssetDatabase.LoadAssetAtPath<Texture3D>(assetPath);

                // If your importer sets Texture3D as a sub-asset, grab it this way
                if (tex == null)
                {
                    foreach (var a in AssetDatabase.LoadAllAssetsAtPath(assetPath))
                    {
                        if (a is Texture3D t) { tex = t; break; }
                    }
                }

                if (tex == null)
                {
                    var main = AssetDatabase.LoadMainAssetAtPath(assetPath);
                    Debug.LogError($"Volumetric Baking: No Texture3D found at {assetPath}. Main={main?.GetType().Name ?? "null"}");
                    continue;
                }

                var area = volumes[vIdx];

                // Record + dirty so it serializes (esp prefab instances)
                Undo.RecordObject(area, "Assign Volumetric Bake");
                area.bakedTexture = tex;
                EditorUtility.SetDirty(area);

                if (PrefabUtility.IsPartOfPrefabInstance(area))
                    PrefabUtility.RecordPrefabInstancePropertyModifications(area);

                EditorSceneManager.MarkSceneDirty(area.gameObject.scene);
            }

            SaveAllDirtyScenes();
            AssetDatabase.SaveAssets();
        }

        static void SaveAllDirtyScenes()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var s = SceneManager.GetSceneAt(i);
                if (!s.isLoaded) continue;

                if (s.isDirty)
                {
                    bool ok = EditorSceneManager.SaveScene(s);
                    Debug.Log($"Saved scene '{s.path}' ok={ok}");
                }
            }
        }

        #region 3D Texture Processing

        static void ReallocateRendertarget(RenderTexture rendertarget, int3 resolution)
        {
            if (all(int3(rendertarget.width, rendertarget.height, rendertarget.volumeDepth) == resolution))
            {
                return;
            }
            rendertarget.Release();
            rendertarget.width = resolution.x;
            rendertarget.height = resolution.y;
            rendertarget.volumeDepth = resolution.z;
            rendertarget.Create();
        }

        static int ID_Source = Shader.PropertyToID("_Source");
        static int ID_SourceDim = Shader.PropertyToID("_SourceDim");
        static int ID_ClearColor = Shader.PropertyToID("_ClearColor");

        /// <summary>
        /// Clears a 3D rendertexture to a specified color. Necessary as the normal methods for clearing only do so to a single slice.
        /// </summary>
        /// <param name="RT3d">3D Rendertexture to clear, must be read-write enabled</param>
        /// <param name="color">color to clear to, defaults to (0,0,0,0)</param>
        static void Clear3DRendertexture(RenderTexture RT3d, float4 color = default)
        {
            ComputeShader clearShader = Clear3DTexShader;
            int[] dim = new int[3] { RT3d.width, RT3d.height, RT3d.volumeDepth };
            clearShader.SetTexture(0, ID_Source, RT3d);
            clearShader.SetInts(ID_SourceDim, dim);
            clearShader.SetVector(ID_ClearColor, color);
            clearShader.Dispatch(0, (dim[0] + 3) / 4, (dim[1] + 3) / 4, (dim[2] + 3) / 4);
        }

        static int id_PrevMipDimOffset = Shader.PropertyToID("_PrevMipDimOffset");
        static int id_MipDimOffset = Shader.PropertyToID("_MipDimOffset");
        static int id_Buffer = Shader.PropertyToID("_Buffer");
        static int id_Input = Shader.PropertyToID("_Input");

        /// <summary>
        /// Creates mips of a 3D rendertexture, and stores all mips sequentially into a compute buffer for easy synchronous readback.
        /// </summary>
        /// <param name="mip0">3D Rendertexture to calculate mips for, must be RGBAHalf and RW enabled </param>
        /// <returns>ComputeBuffer containing the rendertexture's mips</returns>
        static ComputeBuffer Get3DMipsBuffer(RenderTexture mip0)
        {
            int numMips = (int)math.floor(math.log2(math.max(mip0.width, math.max(mip0.height, mip0.volumeDepth)))) + 1;
            RenderTextureDescriptor rtDesc = mip0.descriptor;
            int3 textureDim = new int3(rtDesc.width, rtDesc.height, rtDesc.volumeDepth);
            int bufferCount = textureDim.x * textureDim.y * textureDim.z;
            for (int i = 0; i < numMips; i++)
            {
                textureDim = math.max(textureDim / 2, 1);
                bufferCount += textureDim.x * textureDim.y * textureDim.z;
            }
            ComputeBuffer mips = new ComputeBuffer(bufferCount, 4 * sizeof(ushort), ComputeBufferType.Structured);

            CommandBuffer cmd = CommandBufferPool.Get("Mip3DTexToBuffer");
            try
            {
                cmd.Clear();

                ComputeShader mip3DCompute = Mip3DCompute;

                int initKernel = mip3DCompute.FindKernel("CopyTexToBuffer");
                int mipKernel = mip3DCompute.FindKernel("CalculateMipBuffer");
                int3 mipDim = new int3(rtDesc.width, rtDesc.height, rtDesc.volumeDepth);

                cmd.SetComputeIntParams(mip3DCompute, id_MipDimOffset, new int[] { mipDim.x, mipDim.y, mipDim.z, 0 });
                cmd.SetComputeBufferParam(mip3DCompute, initKernel, id_Buffer, mips);
                cmd.SetComputeTextureParam(mip3DCompute, initKernel, id_Input, mip0);
                cmd.DispatchCompute(mip3DCompute, initKernel, (mipDim.x + 3) / 4, (mipDim.y + 3) / 4, (mipDim.z + 3) / 4);

                int3 prevMipDim = mipDim;
                int mipPtr = 0;
                int prevMipPtr = 0;
                cmd.SetComputeBufferParam(mip3DCompute, mipKernel, id_Buffer, mips);
                for (int level = 1; level < numMips; level++)
                {
                    prevMipDim = mipDim;
                    prevMipPtr = mipPtr;
                    mipDim = math.max(mipDim / 2, new int3(1, 1, 1));
                    mipPtr += prevMipDim.x * prevMipDim.y * prevMipDim.z;
                    cmd.SetComputeIntParams(mip3DCompute, id_PrevMipDimOffset, new int[] { prevMipDim.x, prevMipDim.y, prevMipDim.z, prevMipPtr });
                    cmd.SetComputeIntParams(mip3DCompute, id_MipDimOffset, new int[] { mipDim.x, mipDim.y, mipDim.z, mipPtr });
                    cmd.DispatchCompute(mip3DCompute, mipKernel, (mipDim.x + 3) / 4, (mipDim.y + 3) / 4, (mipDim.z + 3) / 4);
                    GraphicsFence fence = cmd.CreateGraphicsFence(GraphicsFenceType.AsyncQueueSynchronisation, SynchronisationStageFlags.AllGPUOperations);
                    cmd.WaitOnAsyncGraphicsFence(fence);
                }
                Graphics.ExecuteCommandBuffer(cmd);
            }
            finally
            {
                CommandBufferPool.Release(cmd);
            }
            return mips;
        }

        static Texture3D ReadBufferToTex3D(ComputeBuffer rtAndMips, int width, int height, int depth)
        {
            GraphicsFormat gfmt = GraphicsFormat.R16G16B16A16_SFloat;
            Texture3D output = new Texture3D(width, height, depth, gfmt, TextureCreationFlags.MipChain);
            int stride = rtAndMips.stride / sizeof(ushort);
            ushort[] bufferReadBack = new ushort[rtAndMips.count * stride];
            rtAndMips.GetData(bufferReadBack);
            //Debug.Log($"Pixel 0 is ({f16tof32(bufferReadBack[0])}, {math.f16tof32(bufferReadBack[1])}, {math.f16tof32(bufferReadBack[2])}, {math.f16tof32(bufferReadBack[3])})");
            int ptr = 0;
            for (int mip = 0; mip < output.mipmapCount; mip++)
            {
                NativeArray<ushort> outputRaw = output.GetPixelData<ushort>(mip);
                int copyCount = width * height * depth * stride;
                NativeArray<ushort>.Copy(bufferReadBack, ptr, outputRaw, 0, width * height * depth * stride);
                ptr += copyCount;
                width = math.max(1, width / 2);
                height = math.max(1, height / 2);
                depth = math.max(1, depth / 2);
            }
            return output;
        }

        /// <summary>
        /// NativeArray-based twin of <see cref="ReadBufferToTex3D"/> for the async
        /// readback path. Takes the raw ushort NativeArray from an
        /// <see cref="AsyncGPUReadbackRequest.GetData{T}"/> call and copies the
        /// mip chain into a freshly created <see cref="Texture3D"/>. Avoids the
        /// managed-array round-trip the sync path uses.
        /// </summary>
        static Texture3D ReadRawMipDataToTex3D(NativeArray<ushort> rawMipData, int width, int height, int depth)
        {
            const int channelsPerPixel = 4; // R16G16B16A16

            GraphicsFormat gfmt = GraphicsFormat.R16G16B16A16_SFloat;
            Texture3D output = new Texture3D(width, height, depth, gfmt, TextureCreationFlags.MipChain);

            int ptr = 0;
            for (int mip = 0; mip < output.mipmapCount; mip++)
            {
                NativeArray<ushort> outputRaw = output.GetPixelData<ushort>(mip);
                int copyCount = width * height * depth * channelsPerPixel;

                // Bounds guard against an undersized readback (shouldn't happen, but fail loudly if it does).
                if (ptr + copyCount > rawMipData.Length)
                {
                    Debug.LogError(
                        $"Volumetric Baking: async readback data ({rawMipData.Length}) smaller than expected mip chain (needed {ptr + copyCount}). " +
                        $"Stopping at mip {mip}.");
                    break;
                }

                NativeArray<ushort>.Copy(rawMipData, ptr, outputRaw, 0, copyCount);
                ptr += copyCount;
                width = math.max(1, width / 2);
                height = math.max(1, height / 2);
                depth = math.max(1, depth / 2);
            }

            return output;
        }

        #endregion // 3D Texture Processing
    }
}