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

        static void BakeSceneSkyMeanRadiance()
        {
            if (udata == null)
                return;

            if (udata.skyShader == null)
            {
                Debug.LogWarning("Volumetric Baking: Sky mean radiance compute shader is null.");
                return;
            }

            if (udata.skyTexture == null)
            {
                Debug.LogWarning("Volumetric Baking: Sky texture is null.");
                return;
            }

            if (udata.skyColorBuffer == null)
            {
                Debug.LogWarning("Volumetric Baking: Sky result buffer is null.");
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
                return;
            }

            CommandBuffer cmd = CommandBufferPool.Get("Bake Mean Sky Radiance");
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
            CommandBufferPool.Release(cmd);

            Vector4[] result = new Vector4[1];
            udata.skyColorBuffer.GetData(result);

            udata.bakedSkyMeanRadiance = new Color(result[0].x, result[0].y, result[0].z, result[0].w);
            udata.hasBakedSkyMeanRadiance = true;

            Debug.Log($"Volumetric Baking: baked mean sky radiance = {result[0]}");
        }

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
            public int chunkSampleCount;
            public float areaSeed;
            
            public ComputeBuffer skyColorBuffer;
            public Color bakedSkyMeanRadiance = Color.black;
            public bool hasBakedSkyMeanRadiance = false;
            
            public bool finished = false;

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
            EditorApplication.update += BakeEditorUpdate;
        }


        static void BakeEditorUpdate()
        {
            // Don't run while the asset database is importing
            if (EditorApplication.isUpdating)
            {
                return;
            }

            // Only run the update once every 5 editor updates. Otherwise strange native D3D12 driver crashes occur. Not sure why, maybe unity's not freeing up resources when it should?
            udata.currentFrame = (udata.currentFrame + 1) % UpdateLoopData.frameSkip;
            if (udata.currentFrame != 0)
            {
                // Crashing seems to be linked to the scene view not updating. Skipping 5 frames is fine if the view is set to "always refresh",
                // but if that's disabled it still crashes. Manually force it to update.
                if (!Application.isBatchMode && udata.currentFrame == 1)
                    SceneView.RepaintAll();
                return;
            }

            if (udata.finished)
            {
                EditorApplication.update -= BakeEditorUpdate;

                AssignTexturesToVolumes();
                udata.Dispose();
                udata = null;
                BakeCompleted?.Invoke(true);

                return;
            }

            int currentSample = udata.currentChunkIndex * udata.chunkSampleCount;
            bool areaFinishedRendering = currentSample >= udata.totalAreaSamples;
            TimeSpan runningTime = TimeSpan.FromSeconds(EditorApplication.timeSinceStartup - udata.startTime);

            if (!Application.isBatchMode)
            {
                if (EditorUtility.DisplayCancelableProgressBar(
                        $"Baking Volumes ({runningTime.ToString("hh':'mm':'ss")})",
                        $"Volume {udata.currentAreaIndex} / {VolumetricRegisters.volumetricAreas.Count}, Sample {currentSample} / {udata.totalAreaSamples}",
                        (float)(udata.currentAreaIndex) / (float)(VolumetricRegisters.volumetricAreas.Count)))
                {
                    EditorUtility.ClearProgressBar();
                    EditorApplication.update -= BakeEditorUpdate;
                    udata.Dispose();
                    udata = null;
                    EditorUtility.ClearProgressBar();
                    return;
                }
            }

            if (!areaFinishedRendering)
            {
                TraceChunk();
            }
            else // Area has finished rendering, save it to disk
            {
                SaveAreaToDisk();

                udata.currentChunkIndex = 0;
                udata.currentAreaIndex += 1;
                TimeSpan totalTime = TimeSpan.FromSeconds(EditorApplication.timeSinceStartup - udata.startTime);
                Debug.Log(
                    $"[VolBake] All {VolumetricRegisters.volumetricAreas.Count} areas traced " +
                    $"in {totalTime:hh\\:mm\\:ss} — saving assets…"
                );
                // All volumes are finished, set the finished flag and begin importing volumes.
                if (udata.currentAreaIndex == VolumetricRegisters.volumetricAreas.Count)
                {
                    Color bakedSkyColor = Color.black;
                    bool hasSkyColor = false;
                    int envSampleCount = udata.environmentSampleCount;

                    try
                    {
                        BakeSceneSkyMeanRadiance();
                        bakedSkyColor = udata.bakedSkyMeanRadiance;
                        hasSkyColor = udata.hasBakedSkyMeanRadiance;
                    }
                    catch (Exception ex)
                    {
                        Debug.LogException(ex);
                    }

                    EditorUtility.ClearProgressBar();
                    udata.finished = true;
                    udata.DisposeGraphicsResources();

                    AssetDatabase.StopAssetEditing();

                    if (hasSkyColor)
                    {
                        var bakedAsset = SaveOrUpdateBakedVolumetricsDataAsset(bakedSkyColor, envSampleCount);
                        EnsureSceneBindingObject(bakedAsset);
                    }

                    AssetDatabase.SaveAssets();
                    AssetDatabase.Refresh();

                    if (!Application.isBatchMode)
                        VolumetricRegisters.MarkClipmapDirty();
                }
            }
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
            Color colorModulation = light.color.linear;
            if (light.useColorTemperature) colorModulation *= Mathf.CorrelatedColorTemperatureToRGB(light.colorTemperature);
            colorModulation *= light.intensity;
            colorModulation *= light.gameObject.GetComponent<UniversalAdditionalLightData>().volumetricDimmer;
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
        /// Traces a chunk of rays for the current volume, using the global data stored in udata to determine the volume and ray index to start at.
        /// Automatically re-initializes and clears the global rendertarget for the first chunk in a a volume, and increments the global chunk index
        /// when done. Does not increment the area index or reset the chunk index to 0 upon hitting the total ray count for an area, that is handled 
        /// by the editor update loop.
        /// </summary>
        static void TraceChunk()
        {
            int areaIdx = udata.currentAreaIndex;
            int sampleStart = udata.currentChunkIndex * udata.chunkSampleCount;
            //Debug.Log($"Tracing Area {areaIdx} Chunk {udata.currentChunkIndex}  Sample {sampleStart} - {sampleStart + udata.chunkSampleCount} / {udata.chunkSampleCount}");
            int totalSamplesAllAreas = udata.totalAreaSamples * VolumetricRegisters.volumetricAreas.Count;
            float overallPct = totalSamplesAllAreas > 0
                ? 100f * (udata.currentAreaIndex * udata.totalAreaSamples + sampleStart)
                  / (float)totalSamplesAllAreas
                : 0f;
            float areaPct = udata.totalAreaSamples > 0
                ? 100f * sampleStart / (float)udata.totalAreaSamples
                : 0f;

            TimeSpan elapsed2 = TimeSpan.FromSeconds(EditorApplication.timeSinceStartup - udata.startTime);
            Debug.Log(
                $"[VolBake] [{elapsed2:hh\\:mm\\:ss}] " +
                $"Area {udata.currentAreaIndex + 1}/{VolumetricRegisters.volumetricAreas.Count} " +
                $"| Chunk {udata.currentChunkIndex} " +
                $"| Area {areaPct:F0}% ({sampleStart}/{udata.totalAreaSamples} samples) " +
                $"| Overall {overallPct:F1}%"
            );
            Vector3Int resolution = VolumetricRegisters.volumetricAreas[areaIdx].NormalizedTexelDensity;
            int3 threads = int3(resolution.x, resolution.y, resolution.z);
            Vector3 boxSize = VolumetricRegisters.volumetricAreas[areaIdx].BoxScale;
            float maxVoxelSize = max(boxSize.x / (float)resolution.x, math.max(boxSize.y / (float)resolution.y, boxSize.z / (float)resolution.z));

 

            if (udata.currentChunkIndex == 0)
            {
                udata.areaSeed = (UnityEngine.Random.Range(0.0f, 64.0f));
                ReallocateRendertarget(udata.rendertarget, int3(resolution.x, resolution.y, resolution.z));
                Clear3DRendertexture(udata.rendertarget);
            }

            RayTracingShader rtshader = udata.rtShader;
            ComputeShader skyshader = udata.skyShader;

            //for (int rayCount = sampleStart; rayCount < sampleEnd; rayCount += udata.chunkSampleCount)
            {
                
                CommandBuffer cmd = CommandBufferPool.Get();
                //if (loopIdx > 0) cmd.WaitOnAsyncGraphicsFence(fence[loopIdx - 1]);

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
                cmd.SetRayTracingIntParam(rtshader, id_PerDispatchRayCount, udata.chunkSampleCount);
                
                //Cookies
                cmd.SetRayTracingTextureParam(rtshader, id__LightCookies, udata.cookieAtlas);
                cmd.SetRayTracingTextureParam(rtshader, id__PointCookies, udata.pointCookieArray);

                cmd.SetRayTracingVectorParam(rtshader, id_Size, VolumetricRegisters.volumetricAreas[areaIdx].NormalizedScale);
                cmd.SetRayTracingVectorParam(rtshader, id_WPosition, VolumetricRegisters.volumetricAreas[areaIdx].Corner);
                cmd.SetRayTracingFloatParam(rtshader, id__Seed, udata.areaSeed);
                cmd.SetRayTracingFloatParam(rtshader, id_HalfVoxelSize, maxVoxelSize * 0.5f);
               
                cmd.SetRayTracingTextureParam(rtshader, id_g_Output, udata.rendertarget);

                cmd.SetRayTracingIntParam(rtshader, id_startIdx, sampleStart);
                cmd.DispatchRays(rtshader, "MainRayGenShader", (uint)threads.x, (uint)threads.y, (uint)threads.z);
                Graphics.ExecuteCommandBuffer(cmd);

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

        // static void SaveAreaToDisk()
        // {
        //     string path = Path.Combine(CheckDirectoryAndReturnPath(), $"Volumemap-{udata.currentAreaIndex}{Vol3d.fileExtension}");
        //     ComputeBuffer mipChain = Get3DMipsBuffer(udata.rendertarget);
        //
        //     Texture3D ReadBackTex = ReadBufferToTex3D(mipChain, udata.rendertarget.width, udata.rendertarget.height, udata.rendertarget.volumeDepth);
        //     mipChain.Dispose();
        //
        //     Vol3d.WriteTex3DToVol3D(ReadBackTex, path);
        //     UnityEngine.Object.DestroyImmediate(ReadBackTex);
        // }
        static void SaveAreaToDisk()
        {
            string folder = CheckDirectoryAndReturnPath().Replace('\\','/');
            string assetPath = $"{folder}/Volumemap-{udata.currentAreaIndex}{Vol3d.fileExtension}";
            string absPath = Path.GetFullPath(assetPath);

            //Debug.Log($"SaveAreaToDisk: writing {assetPath} (abs {absPath})");
            Debug.Log($"[VolBake] Saving area {udata.currentAreaIndex} → {assetPath}");

            var mipChain = Get3DMipsBuffer(udata.rendertarget);
            var tex = ReadBufferToTex3D(mipChain, udata.rendertarget.width, udata.rendertarget.height, udata.rendertarget.volumeDepth);
            mipChain.Dispose();

            Vol3d.WriteTex3DToVol3D(tex, absPath);
            UnityEngine.Object.DestroyImmediate(tex);

            //Debug.Log($"SaveAreaToDisk: exists={File.Exists(absPath)} bytes={(File.Exists(absPath) ? new FileInfo(absPath).Length : 0)}");
            long bytes = File.Exists(absPath) ? new FileInfo(absPath).Length : 0;
            Debug.Log($"[VolBake] Area {udata.currentAreaIndex} saved — {bytes / 1024} KB at {assetPath}");
        }

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
            CommandBuffer cmd = CommandBufferPool.Get("Mip3DTexToBuffer");
            cmd.Clear();
            ComputeBuffer mips = new ComputeBuffer(bufferCount, 4 * sizeof(ushort), ComputeBufferType.Structured);

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
            CommandBufferPool.Release(cmd);
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

        #endregion // 3D Texture Processing
    }
}