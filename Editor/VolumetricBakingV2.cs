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
    public class VolumetricBakingV2
    {
        [StructLayout(LayoutKind.Sequential)]
        struct PointLightData
        {
            public float3 wPos;
            public float4 color;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct ConeLightData
        {
            public float3 wPos;
            public float4 color;
            public float3 dir;
            public float2 coneParams;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct DirLightData
        {
            public float3 dir;
            public float4 color;
        }
        [StructLayout(LayoutKind.Sequential)]
        struct AreaLightData
        {
            public float4x4 areaMatrix;
            public float4x4 areaMatrixInv;
            public float3 wPos;
            public float4 color;
            public float3 size;
        }

        class UpdateLoopData : IDisposable
        {
            public RayTracingShader rtShader;
            public RayTracingAccelerationStructure rtStructure;
            public int pointLightCount, coneLightCount, dirLightCount, areaLightCount;
            public ComputeBuffer pointBuffer, coneBuffer, dirBuffer, areaBuffer;

            public bool skyTexIsGenerated = false;
            public Texture skyTexture;
            public RenderTexture rendertarget;


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

            public bool finished = false;

            private bool disposed = false;

            public void Dispose()
            {
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

            PopulateLightBuffers(pointBuffer, pointLights, coneBuffer, coneLights, dirBuffer, dirLights, areaBuffer, areaLights);

            Texture skyTex = GetEnvironmentCubemap(skyboxContributes, customSkyTexture);
            udata.skyTexture = skyTex;
            udata.skyTexIsGenerated = customSkyTexture != null && skyboxContributes;

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
                if (udata.currentFrame == 1) SceneView.RepaintAll();

                return;
            }

            if (udata.finished)
            {
                EditorApplication.update -= BakeEditorUpdate;

                AssignTexturesToVolumes();
                udata.Dispose();
                udata = null;
                return;
            }

            int currentSample = udata.currentChunkIndex * udata.chunkSampleCount;
            bool areaFinishedRendering = currentSample >= udata.totalAreaSamples;
            TimeSpan runningTime = TimeSpan.FromSeconds(EditorApplication.timeSinceStartup - udata.startTime);
            if (EditorUtility.DisplayCancelableProgressBar($"Baking Volumes ({runningTime.ToString("hh':'mm':'ss")})",
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

            if (!areaFinishedRendering)
            {
                TraceChunk();
            }
            else // Area has finished rendering, save it to disk
            {
                SaveAreaToDisk();

                udata.currentChunkIndex = 0;
                udata.currentAreaIndex += 1;

                // All volumes are finished, set the finished flag and begin importing volumes.
                if (udata.currentAreaIndex == VolumetricRegisters.volumetricAreas.Count)
                {
                    EditorUtility.ClearProgressBar();
                    udata.finished = true;
                    udata.DisposeGraphicsResources();
                    AssetDatabase.StopAssetEditing();
                    AssetDatabase.Refresh();
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


        static void PopulateLightBuffers(
            ComputeBuffer pointBuffer, List<Light> pointLights,
            ComputeBuffer coneBuffer, List<Light> coneLights,
            ComputeBuffer dirBuffer, List<Light> dirLights,
            ComputeBuffer areaBuffer, List<Light> areaLights)
        {
            PointLightData[] pointDatas = new PointLightData[pointLights.Count];
            ConeLightData[] coneDatas = new ConeLightData[coneLights.Count];
            DirLightData[] dirDatas = new DirLightData[dirLights.Count];
            AreaLightData[] areaDatas = new AreaLightData[areaLights.Count];

            for (int pIdx = 0; pIdx < pointLights.Count; pIdx++)
            {
                pointDatas[pIdx].wPos = pointLights[pIdx].transform.position;
                pointDatas[pIdx].color = ResolveLightColor(pointLights[pIdx]);
            }

            for (int cIdx = 0; cIdx < coneLights.Count; cIdx++)
            {
                coneDatas[cIdx].wPos = coneLights[cIdx].transform.position;
                coneDatas[cIdx].color = ResolveLightColor(coneLights[cIdx]);
                coneDatas[cIdx].dir = coneLights[cIdx].transform.forward;

                float flPhiDot = saturate(cos(coneLights[cIdx].spotAngle * 0.5f * Mathf.Deg2Rad)); // outer cone
                float flThetaDot = saturate(cos(coneLights[cIdx].innerSpotAngle * 0.5f * Mathf.Deg2Rad)); // inner cone

                coneDatas[cIdx].coneParams = float2(flPhiDot, 1.0f / Mathf.Max(0.01f, flThetaDot - flPhiDot));
            }

            for (int dIdx = 0; dIdx < dirLights.Count; dIdx++)
            {
                dirDatas[dIdx].dir = dirLights[dIdx].transform.forward;
                dirDatas[dIdx].color = ResolveLightColor(dirLights[dIdx]);
            }

            for (int aIdx = 0; aIdx < areaLights.Count; aIdx++)
            {
                areaDatas[aIdx].wPos = areaLights[aIdx].transform.position;
                areaDatas[aIdx].areaMatrix = Matrix4x4.TRS(areaLights[aIdx].transform.position, areaLights[aIdx].transform.rotation, Vector3.one);
                areaDatas[aIdx].areaMatrixInv = math.inverse(areaDatas[aIdx].areaMatrix);
                areaDatas[aIdx].color = ResolveLightColor(areaLights[aIdx]);
                areaDatas[aIdx].size = float3(areaLights[aIdx].areaSize.x, areaLights[aIdx].areaSize.y, areaLights[aIdx].type == LightType.Disc ? 1 : 0); //Packing for area or disc logic
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

                RenderTexture cubetex = new RenderTexture(256, 256, 1, RenderTextureFormat.DefaultHDR);
                cubetex.enableRandomWrite = true;
                cubetex.dimension = UnityEngine.Rendering.TextureDimension.Cube;
                cubetex.Create();

                Camera renderCam = new GameObject().AddComponent<Camera>();
                renderCam.cullingMask = 0;
                renderCam.backgroundColor = Color.black;
                renderCam.clearFlags = CameraClearFlags.Skybox;
                renderCam.RenderToCubemap(cubetex);
                CoreUtils.Destroy(renderCam.gameObject);
                return cubetex;
            }
            //Provided skybox
            else
            {
                return customTexture;
            }
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
            Debug.Log($"Tracing Area {areaIdx} Chunk {udata.currentChunkIndex}  Sample {sampleStart} - {sampleStart + udata.chunkSampleCount} / {udata.chunkSampleCount}");
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

        static void SaveAreaToDisk()
        {
            string path = Path.Combine(CheckDirectoryAndReturnPath(), $"Volumemap-{udata.currentAreaIndex}{Vol3d.fileExtension}");
            ComputeBuffer mipChain = Get3DMipsBuffer(udata.rendertarget);

            Texture3D ReadBackTex = ReadBufferToTex3D(mipChain, udata.rendertarget.width, udata.rendertarget.height, udata.rendertarget.volumeDepth);
            mipChain.Dispose();

            Vol3d.WriteTex3DToVol3D(ReadBackTex, path);
            UnityEngine.Object.DestroyImmediate(ReadBackTex);
        }

        static void AssignTexturesToVolumes()
        {
            List<BakedVolumetricArea> volumes = VolumetricRegisters.volumetricAreas;
            string basePath = Path.Combine(CheckDirectoryAndReturnPath(), $"Volumemap-");
            int numVolumes = volumes.Count;
            for (int vIdx = 0; vIdx < numVolumes; vIdx++)
            {
                string path = basePath + vIdx.ToString() + Vol3d.fileExtension;
                Texture3D volumeTex = (Texture3D)AssetDatabase.LoadAssetAtPath(path, typeof(Texture3D));
                if (volumeTex == null)
                {
                    Debug.LogError($"Volumetric Baking: 3D texture for volume {vIdx} not found at {path}");
                }
                volumes[vIdx].bakedTexture = volumeTex;
                EditorSceneManager.MarkSceneDirty(volumes[vIdx].gameObject.scene);
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