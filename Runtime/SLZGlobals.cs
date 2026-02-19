using System.Collections;
using System.Collections.Generic;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Linq;
using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering.RenderGraphModule;
using Unity.Mathematics;
using UnityEngine.Experimental.Rendering;
using static Unity.Burst.Intrinsics.X86.Avx;


#if UNITY_EDITOR
using UnityEditor;
//using static UnityEditor.ShaderData;
#endif

namespace UnityEngine.Rendering.Universal
{
    public class SLZGlobals
    {
        static SLZGlobals s_Instance;
        // Blue Noise
        private ComputeBuffer BlueNoiseCB;
        private ComputeBuffer HiZDimBuffer;
        private float[] BlueNoiseDim = new float[8]; // width, height, depth, current slice index 
        private bool hasSetBNTextures;
#if UNITY_EDITOR
        private static long framecount = 0;
        private static int unityFrameCount = 0;
        private static double timeSinceStartup = 0.0;
#endif
        //private int HiZDimBufferID = Shader.PropertyToID("HiZDimBuffer");
        public static readonly int HiZMipNumID = Shader.PropertyToID("_HiZHighestMip");
        public static readonly int HiZDimID = Shader.PropertyToID("_HiZDim");
        public static readonly int SSRConstantsID = Shader.PropertyToID("SSRConstants");
        public static readonly int CameraOpaqueTextureID = Shader.PropertyToID("_CameraOpaqueTexture");
        public static readonly int PrevHiZ0TextureID = Shader.PropertyToID("_PrevHiZ0Texture");
        // float4 containing the camera opaque texture's 
        public static readonly int OpaqueTextureDimID = Shader.PropertyToID("_CameraOpaqueTexture_Dim");
        public static readonly int VrOccMeshDistanceID = Shader.PropertyToID("_VrOccMeshDistance");

        // Mips of the camera opaque texture only go down to 8x8 (2^3) so truncate the number of mips by this amount
        public const int opaqueMipTruncation = 3;
        public int opaqueTexID { get { return CameraOpaqueTextureID; } }
        public int prevHiZTexID { get { return PrevHiZ0TextureID; } }

        public GlobalKeyword HiZEnabledKW { get; private set; }
        public GlobalKeyword HiZMinMaxKW { get; private set; }

        public RenderTexture VrOccDistanceTex;
        public bool hasGeneratedVrOcDistTex = false;
        public Material VrOccDistanceMat;

        // Previously was SSREnabledKW, had to be inverted to support disabling SSR as a material property without
        // adding an additional keyword.
        //
        // Unity will not allow an enabled global keyword to be disabled by the material's local keyword state.
        // This meant that in order to disable SSR on a specific material another local keyword was neccessary,
        // potentially doubling the shader size with an unnecessary duplicates of the programs for the global SSR
        // off state.
        //
        // However, unity will override the local keyword state with the global state if the global is enabled but
        // the local is not Thus, if we have SSR enabled be the default state and disable it with a global keyword,
        // then we can also disable it per-material as well.
        public GlobalKeyword SSRDisabledKW { get; private set; }

        //public SLZPerCameraRTStorage PerCameraOpaque;
        //public SLZPerCameraRTStorage PerCameraPrevHiZ;

        //private uint PerCameraPrevHiZIter = 0;
        //private uint PerCameraOpaqueIter = 0;

        private ComputeBuffer SSRGlobalCB;

        private double extraSmoothedDT = 0.01111;

        [StructLayout(LayoutKind.Sequential, Pack = 4, Size = 32)]
        struct SSRBufferData
        {
            public float _SSRHitRadius;
            public float _SSRTemporalWeight;
            public float _SSRSteps;
            public int _SSRMinMip;
            
            public float _SSRDistScale;
            public float empty1;
            public float empty2;
            public float empty3;
        }
        private SLZGlobals()
        {
            BlueNoiseCB = new ComputeBuffer(8, sizeof(float), ComputeBufferType.Constant);
            BlueNoiseDim = new float[8];
            hasSetBNTextures = false;
            SSRGlobalCB = new ComputeBuffer(8, sizeof(float), ComputeBufferType.Constant);
            HiZDimBuffer = new ComputeBuffer(15, Marshal.SizeOf<Vector4>());

            //SSREnabledKW = GlobalKeyword.Create("_SLZ_SSR_ENABLED");
            SSRDisabledKW = GlobalKeyword.Create("_SLZ_SSR_DISABLED");
            HiZEnabledKW = GlobalKeyword.Create("_HIZ_ENABLED");
            HiZMinMaxKW = GlobalKeyword.Create("_HIZ_MIN_MAX_ENABLED");
            VrOccDistanceTex = new RenderTexture(VrOccMaskDescriptor(4,4));
            VrOccDistanceTex.hideFlags = HideFlags.DontSaveInBuild | HideFlags.DontSaveInEditor;
            //PerCameraOpaque = new SLZPerCameraRTStorage();
            //PerCameraPrevHiZ = new SLZPerCameraRTStorage();
            //PerCameraSSRGlobals = new SLZPerCameraBufferStorage(8, sizeof(float), ComputeBufferMode.Dynamic, ComputeBufferType.Constant);
        }
        public static SLZGlobals instance
        {
            get
            {
                if (s_Instance == null)
                {
                    s_Instance = new SLZGlobals();
                }
                return s_Instance;
            }

        }
        /*
        private static void IncrementFrameCounter(ScriptableRenderContext ctx, List<Camera> cam)
        {
            //if (unityFrameCount == Time.frameCount)
            //{
            //    Debug.LogError($"IncrementFrameCounter called multiple times in one frame! {unityFrameCount}, {framecount}");
            //}
            //unityFrameCount = Time.frameCount;
            framecount = Time.frameCount;
        }
        */

        public void SetHiZSSRKeyWords(bool enableSSR, bool requireHiZ, bool requireMinMax)
        {
            Shader.SetKeyword(SSRDisabledKW, !enableSSR);
            Shader.SetKeyword(HiZEnabledKW, requireHiZ);
            Shader.SetKeyword(HiZMinMaxKW, requireMinMax);
        }

        public void SetHiZGlobal(int numMips, Vector4 dim)
        {
            //HiZDimBuffer.SetData(data);
            //Shader.SetGlobalBuffer(HiZDimBufferID, HiZDimBuffer);
            Shader.SetGlobalInt(HiZMipNumID, numMips);
            Shader.SetGlobalVector(HiZDimID, dim);
            //Shader.SetKeyword(HiZMinMaxKW, minmax);
        }

        private SSRBufferData SetSSRGlobalsBase(int maxSteps, int minMip, float hitRadius, float temporalWeight, float fov, int screenHeight)
        {
            /*
             * 0 float _SSRHitRadius;
             * 1 float _SSREdgeFade;
             * 2 int _SSRSteps;
             * 3 none
             */
            SSRBufferData SSRGlobalArray = new SSRBufferData();
            //SSRGlobalArray[0] = 1.0f / (1.0f + hitRadius);//hitRadius;
            //SSRGlobalArray[1] = -cameraNear / (cameraFar - cameraNear) * (hitRadius * SSRGlobalArray[0]);
            SSRGlobalArray._SSRHitRadius = hitRadius;
            extraSmoothedDT = 0.95 * extraSmoothedDT + 0.05 * Time.smoothDeltaTime;
            float framerateConst = 1.0f / (float)extraSmoothedDT * (1.0f / 90.0f);
            float expConst = math.exp(-framerateConst);
            float FRTemporal = (math.exp(-framerateConst * temporalWeight) - expConst) * (1.0f / (1.0f - expConst));
            //Debug.Log(FRTemporal);
            SSRGlobalArray._SSRTemporalWeight = math.clamp(1.0f - temporalWeight, 0.0078f, 1.0f); //Mathf.Clamp(FRTemporal, 0.0078f, 1.0f); 
            SSRGlobalArray._SSRSteps = maxSteps;
            float SSRRes = 2048;
            int dynamicMinMip = (int)math.round(math.log2(((float)screenHeight) / SSRRes));
            SSRGlobalArray._SSRMinMip = math.max(dynamicMinMip + minMip, 0);
            float halfTan = math.tan(Mathf.Deg2Rad * (fov * 0.5f));
            SSRGlobalArray._SSRDistScale = halfTan / (0.5f * (float)screenHeight); // rcp(0.5*_ScaledScreenParams.y * UNITY_MATRIX_P._m11)
            //Debug.Log("SSR scale: " + SSRGlobalArray[4]);
            return SSRGlobalArray;
           
        }
        public void SetSSRGlobals_(int maxSteps, int minMip, float hitRadius, float temporalWeight, float fov, int screenHeight)
        {
            Span<SSRBufferData> buffer = stackalloc SSRBufferData[1] { SetSSRGlobalsBase(maxSteps, minMip, hitRadius, temporalWeight, fov, screenHeight) };
            
            SSRGlobalCB.SetData<SSRBufferData>(buffer);
            Shader.SetGlobalConstantBuffer(SSRConstantsID, SSRGlobalCB, 0, SSRGlobalCB.count * SSRGlobalCB.stride);
        }

        public void SetSSRGlobalsCmd(ref CommandBuffer cmd, int maxSteps, int minMip, float hitRadius, float temporalWeight, float fov, int screenHeight)
        {
            Span<SSRBufferData> buffer = stackalloc SSRBufferData[1] { SetSSRGlobalsBase(maxSteps, minMip, hitRadius, temporalWeight, fov, screenHeight) };
            cmd.SetBufferData<SSRBufferData>(SSRGlobalCB, buffer );
            cmd.SetGlobalConstantBuffer(SSRGlobalCB, SSRConstantsID, 0, SSRGlobalCB.count * SSRGlobalCB.stride);
        }


        public void SetBlueNoiseGlobals(Texture2DArray BlueNoiseRGBA, Texture2DArray BlueNoiseR)
        {
    
            if (BlueNoiseRGBA != null)
            {
                BlueNoiseDim[0] = BlueNoiseRGBA.width;
                BlueNoiseDim[1] = BlueNoiseRGBA.height;
                BlueNoiseDim[2] = BlueNoiseRGBA.depth;
                BlueNoiseDim[3] = (float) Random.Range(0, BlueNoiseRGBA.width);
                BlueNoiseDim[4] = (float) Random.Range(0, BlueNoiseRGBA.height);
#if UNITY_EDITOR
                if (!EditorApplication.isPlaying)
                {
                    //if (timeSinceStartup != EditorApplication.timeSinceStartup)
                    //{
                    //    timeSinceStartup = EditorApplication.timeSinceStartup;
                    //    framecount++;
                    //}
                    BlueNoiseDim[3] = (int)(Math.Abs(Time.frameCount) % BlueNoiseRGBA.depth);
                    //Debug.Log(BlueNoiseDim[3]);
                }
                else
#endif
                {
                    BlueNoiseDim[3] = math.abs((int)(Time.renderedFrameCount % BlueNoiseRGBA.depth));
                }
                if (BlueNoiseCB != null)
                {
                    BlueNoiseCB.SetData(BlueNoiseDim);
                    Shader.SetGlobalConstantBuffer("BlueNoiseDim", BlueNoiseCB, 0, BlueNoiseCB.count * BlueNoiseCB.stride);
                }
                if (!hasSetBNTextures)
                {
                    Shader.SetGlobalTexture("_BlueNoiseRGBA", BlueNoiseRGBA);
                    Shader.SetGlobalTexture("_BlueNoiseR", BlueNoiseR);
                    hasSetBNTextures = true;
                }
            }
        }



        public void UpdateBlueNoiseFrame()
        {
            if (BlueNoiseCB != null)
            {
#if UNITY_EDITOR
                if (!EditorApplication.isPlaying)
                {
                    long depth = (long)BlueNoiseDim[2];
                    BlueNoiseDim[3] = (Math.Abs(Time.frameCount) % depth + depth) % depth;//(int)((Screen.currentResolution.refreshRateRatio.value * EditorApplication.timeSinceStartup) % depth);
                }
                else
#endif
                {
                    int depth = (int)math.round(BlueNoiseDim[2]);
                    BlueNoiseDim[3] = (Math.Abs(Time.frameCount) % depth + depth) % depth;
                    //BlueNoiseDim[3] = (int)((Time.timeSinceLevelLoadAsDouble * Screen.currentResolution.refreshRate) % depth);
                }
                
                BlueNoiseCB.SetData(BlueNoiseDim);
                Shader.SetGlobalConstantBuffer("BlueNoiseDim", BlueNoiseCB, 0, BlueNoiseCB.count * BlueNoiseCB.stride);
            }
        }




        public static void Dispose()
        {
            if (s_Instance != null)
            {
                if (s_Instance.SSRGlobalCB != null)
                {
                    s_Instance.SSRGlobalCB.Dispose();
                    s_Instance.SSRGlobalCB = null;
                }
                if (s_Instance.BlueNoiseCB != null)
                {
                    s_Instance.BlueNoiseCB.Dispose();
                    s_Instance.BlueNoiseCB = null;
                }
                if (s_Instance.HiZDimBuffer != null)
                {
                    s_Instance.HiZDimBuffer.Dispose();
                    s_Instance.HiZDimBuffer = null;
                }
                if (s_Instance.VrOccDistanceTex != null)
                {
                    s_Instance.VrOccDistanceTex.Release();
                    CoreUtils.Destroy(s_Instance.VrOccDistanceTex);
                }
                if (s_Instance.VrOccDistanceMat != null)
                {
                    CoreUtils.Destroy(s_Instance.VrOccDistanceMat);
                }
            }
            s_Instance = null;
        }


        public static int CalculateOpaqueTexMipLevels(int width, int height)
        {
            return (int)math.floor(math.max(math.log2(width), math.log2(height))) + 1 - opaqueMipTruncation;
        }

        public static RenderTextureDescriptor VrOccMaskDescriptor(int width, int height)
        {
            RenderTextureDescriptor output = new RenderTextureDescriptor();
            {
                output.width = width;
                output.height = height;
                output.volumeDepth = 2;
                output.depthBufferBits = 0;
                output.graphicsFormat = Experimental.Rendering.GraphicsFormat.R8G8_UNorm;
                output.dimension = TextureDimension.Tex2DArray;
                output.enableRandomWrite = false;
                output.useMipMap = false;
                output.autoGenerateMips = false;
                output.msaaSamples = 1;
            }
            return output;
        }
    }


    public class SLZGlobalsSetPass : ScriptableRenderPass
    {

        //public bool setColorTarget = false;
        //public bool setDepthTarget = false;
        //public RTHandle colorTarget;
        //public RTHandle depthTarget;
        //static RTHandle[] target = new RTHandle[0];
        private bool enableSSR;
        private bool requireHiZ;
        private bool requireMinMax;

        private float ssrHitRadius;
        private int ssrMaxSteps;
        private int ssrMinMip;
        private int opaqueMipLevels;
        private float cameraNear;
        private float cameraFar;
        int opaqueTexSizeFrac = 1;
        private Camera camera;
        private PrevOpaqueRT prevOpaque;
        private PrevHiZRT prevHiZ;

        private Material vrOccDistMat;


        SLZGlobalsData passData;
        public SLZGlobalsSetPass(RenderPassEvent evt, bool skipSetup, Material vrOccDistMat)
        {
            renderPassEvent = evt;
            passData = new SLZGlobalsData();
			skipRenderPassAttachmentSetup = skipSetup;
            useNativeRenderPass = false;
            this.vrOccDistMat = vrOccDistMat;

            //ConfigureTarget(target);
        }
        public void Setup(CameraData camData, CameraDataExtSet camDataExtSet)
        {

            // Hack to tell unity to store previous frame object to world matrices...
            // Not used by SRP to enable motion vectors or depth but somehow still necessary :(
            if (camData.enableSSR)
            {
                prevOpaque = camDataExtSet.GetOrCreateExtension<PrevOpaqueRT>();
                //prevHiZ = PersistentRT.TryGet(camDataExtSet, (int)CamDataExtType.HI_Z); // EXPERIMENT: use quad averaging instead of temporal averaging and avoid extra RT + blit + jank previous frame SSR estimation
                camData.camera.depthTextureMode |= DepthTextureMode.MotionVectors | DepthTextureMode.Depth;
            }
            Downsampling downsamplingMethod = UniversalRenderPipeline.asset.opaqueDownsampling;
            if (downsamplingMethod == Downsampling._2xBilinear)
            {
                opaqueTexSizeFrac = 2;
            }
            else if (downsamplingMethod == Downsampling._4xBox || downsamplingMethod == Downsampling._4xBilinear)
            {
                opaqueTexSizeFrac = 4;
            }
            else
            {
                opaqueTexSizeFrac = 1;
            }

            //ConfigureTarget(new RenderTargetIdentifier(BuiltinRenderTextureType.None), new RenderTargetIdentifier(BuiltinRenderTextureType.None));
            //Debug.Log("Setup for " + camData.camera.name);
        }
        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            CameraData camData = renderingData.cameraData;
            ref RenderTextureDescriptor targetDesc = ref camData.cameraTargetDescriptor;
            passData.cmd = cmd;
            passData.cam = camData.camera;
            passData.enableSSR = camData.enableSSR;
            passData.requireHiZ = camData.requiresDepthPyramid;
            passData.requireMinMax = camData.requiresMinMaxDepthPyr;
            passData.opaqueTex = camData.enableSSR ? prevOpaque.handle : null;
            //passData.hiZTex = camData.enableSSR ? prevHiZ.handle : null; // EXPERIMENT: use quad averaging instead of temporal averaging and avoid extra RT + blit + jank previous frame SSR estimation
            passData.opaqueID = SLZGlobals.CameraOpaqueTextureID;
            passData.hiZID = SLZGlobals.PrevHiZ0TextureID;
            passData.ssrDisabledKW = SLZGlobals.instance.SSRDisabledKW;
            passData.hiZEnabledKW = SLZGlobals.instance.HiZEnabledKW;
            passData.hiZMinMaxKW = SLZGlobals.instance.HiZMinMaxKW;
            passData.ssrMinMip = camData.SSRMinMip;
            passData.ssrMaxSteps = camData.maxSSRSteps;
            passData.ssrHitRadius = camData.SSRHitRadius;
            passData.temporalWeight = camData.SSRTemporalWeight;
            passData.fov = camData.camera.fieldOfView;
            passData.screenWidth = targetDesc.width;
            passData.screenHeight = targetDesc.height;
            passData.opaqueTexSizeFrac = opaqueTexSizeFrac;


            if (camData.xrRendering && camData.xrUniversal != null && camData.xrUniversal.hasValidOcclusionMesh && camData.requiresOpaqueTexture)   
                //if (true)
            {
                bool xrOccMeshIsValid = (SLZGlobals.instance.VrOccDistanceTex.width == (camData.cameraTargetDescriptor.width / 4)) &&
                                        (SLZGlobals.instance.VrOccDistanceTex.height == (camData.cameraTargetDescriptor.height / 4));
                if (!xrOccMeshIsValid)
                {
                    passData.generateXrOcclusionMeshDistance = true;

                    RenderTextureDescriptor maskDesc = SLZGlobals.VrOccMaskDescriptor(camData.cameraTargetDescriptor.width, camData.cameraTargetDescriptor.height);
                    SLZGlobals.instance.VrOccDistanceTex.Release();
                    SLZGlobals.instance.VrOccDistanceTex.width = maskDesc.width / 4;
                    SLZGlobals.instance.VrOccDistanceTex.height = maskDesc.height / 4;
                    SLZGlobals.instance.VrOccDistanceTex.Create();



                    passData.xrPass = camData.xrUniversal;

                    passData.xrOcclusionMeshTexID = new RenderTargetIdentifier(SLZGlobals.VrOccMeshDistanceID);
                    cmd.GetTemporaryRT(SLZGlobals.VrOccMeshDistanceID, SLZGlobals.VrOccMaskDescriptor(camData.cameraTargetDescriptor.width, camData.cameraTargetDescriptor.height));
                    passData.xrOcclusionMeshTex = RTHandles.Alloc(passData.xrOcclusionMeshTexID);
                    passData.xrOccDistanceMat = vrOccDistMat;
                }
                else
                {
                    passData.generateXrOcclusionMeshDistance = false;
                }
            }
            else
            {
                passData.generateXrOcclusionMeshDistance = false;
            }

            passData.xrOccDistanceTex = RTHandles.Alloc(SLZGlobals.instance.VrOccDistanceTex);

            if (camData.requiresColorPyramid)
                passData.opaqueMipLevels = SLZGlobals.CalculateOpaqueTexMipLevels(targetDesc.width / opaqueTexSizeFrac, targetDesc.height / opaqueTexSizeFrac);
            else
                passData.opaqueMipLevels = 1;
            
        }
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            ExecutePass(passData, ref renderingData.commandBuffer);
        }

        internal static void ExecutePass(SLZGlobalsData data, ref CommandBuffer cmd)
        {
            
            Camera cam = data.cam;
            bool enableSSR = data.enableSSR;
            bool requireHiZ = data.requireHiZ;
            bool requireMinMax = data.requireMinMax;
            RTHandle opaqueTex = data.opaqueTex;
            RTHandle hiZTex = data.hiZTex;
            int opaqueID = data.opaqueID;
            int hiZID = data.hiZID;
            GlobalKeyword ssrDisabledKW = data.ssrDisabledKW;
            GlobalKeyword hiZEnabledKW = data.hiZEnabledKW;
            GlobalKeyword hiZMinMaxKW = data.hiZMinMaxKW;
           
            using (new ProfilingScope(cmd, ProfilingSampler.Get(URPProfileId.SetSLZGlobals)))
            {

                if (enableSSR && opaqueTex != null)
                {
                    cmd.SetGlobalTexture(opaqueID, opaqueTex);
                    //cmd.SetGlobalTexture(hiZID, hiZTex); // EXPERIMENT: use quad averaging instead of temporal averaging and avoid extra RT + blit + jank previous frame SSR estimation
                }

                if (data.generateXrOcclusionMeshDistance)
                {
                    cmd.SetRenderTarget(data.xrOcclusionMeshTexID, 0, CubemapFace.Unknown, -1);
                    cmd.ClearRenderTarget(false, true, Color.red);
                    data.xrPass.RenderOcclusionMesh(cmd, true);
                    cmd.SetGlobalTexture("_MaskTex", data.xrOcclusionMeshTexID);

                    cmd.SetRenderTarget(data.xrOccDistanceTex, 0, CubemapFace.Unknown, -1);

                    Blitter.BlitCameraTexture(cmd, data.xrOccDistanceTex, data.xrOccDistanceTex, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store, data.xrOccDistanceMat, 0);
                    //data.xrPass.RenderOcclusionMesh(cmd, true, data.xrOccDistanceMat);
                }
                cmd.SetGlobalTexture(SLZGlobals.VrOccMeshDistanceID, data.xrOccDistanceTex);
                SLZGlobals.instance.SetSSRGlobalsCmd(ref cmd, data.ssrMaxSteps, data.ssrMinMip, data.ssrHitRadius, data.temporalWeight, data.fov, data.screenHeight);
                cmd.SetKeyword(ssrDisabledKW, !enableSSR);
                cmd.SetKeyword(hiZEnabledKW, requireHiZ);
                cmd.SetKeyword(hiZMinMaxKW, requireMinMax);
                cmd.SetGlobalVector(SLZGlobals.OpaqueTextureDimID, 
                    new Vector4(data.screenWidth / data.opaqueTexSizeFrac, data.screenHeight / data.opaqueTexSizeFrac, data.opaqueMipLevels - 1, data.opaqueMipLevels + SLZGlobals.opaqueMipTruncation));
            }
        }

        

        TextureDesc tempOcclusionMask(RenderTextureDescriptor main)
        {
            TextureDesc output = new TextureDesc(main.width, main.height, false, true);
            {
                output.sizeMode = TextureSizeMode.Explicit;
                output.width = main.width;
                output.height = main.height;
                output.slices = 2;
                output.scale = Vector2.one;
                output.depthBufferBits = DepthBits.None;
                output.colorFormat = Experimental.Rendering.GraphicsFormat.R8_UNorm;
                output.filterMode = FilterMode.Point;
                output.wrapMode = TextureWrapMode.Clamp;
                output.dimension = TextureDimension.Tex2DArray;
                output.enableRandomWrite = false;
                output.useMipMap = false;
                output.autoGenerateMips = false;
                output.isShadowMap = false;
                output.anisoLevel = 0;
                output.mipMapBias = 0;
                output.msaaSamples = MSAASamples.None;
                output.bindTextureMS = false;
                output.clearBuffer = true;
                output.clearColor = Color.white;
            }
            return output;
        }



        /// <summary>
        /// Rendergraph stuff
        /// </summary>

        internal class SLZGlobalsData
        {
            public CommandBuffer cmd;
            public Camera cam;
            public bool enableSSR;
            public bool requireHiZ;
            public bool requireMinMax;
            public RTHandle opaqueTex;
            public RTHandle hiZTex;
            public int opaqueID;
            public int hiZID;
            public GlobalKeyword ssrDisabledKW;
            public GlobalKeyword hiZEnabledKW;
            public GlobalKeyword hiZMinMaxKW;
            public int ssrMinMip;
            public int ssrMaxSteps;
            public float ssrHitRadius;
            public float temporalWeight;
            public float fov;
            public int screenWidth;
            public int screenHeight;
            public int opaqueMipLevels;
            public int opaqueTexSizeFrac;
            public bool generateXrOcclusionMeshDistance;

            public XRPassUniversal xrPass;
            public Material xrOccDistanceMat;
            public RTHandle xrOccDistanceTex;
            public RTHandle xrOcclusionMeshTex;
            public RenderTargetIdentifier xrOcclusionMeshTexID;
        }

        internal void Render(RenderGraph renderGraph, ref RenderingData renderingData)
        {
            CameraData camData = renderingData.cameraData;
            CameraDataExtSet camDataExtSet = CameraExtDataPool.Instance.GetCameraDataSet(camera);
            prevOpaque = camDataExtSet.GetOrCreateExtension<PrevOpaqueRT>();
            prevHiZ = camDataExtSet.GetOrCreateExtension<PrevHiZRT>();
            // Hack to tell unity to store previous frame object to world vectors...
            // Not used by SRP to enable motion vectors or depth but somehow still necessary :(
            if (camData.enableSSR)
            {
                camData.camera.depthTextureMode |= DepthTextureMode.MotionVectors | DepthTextureMode.Depth;
            }
            using (var builder = renderGraph.AddRenderPass<SLZGlobalsData>("Set SLZ Globals", out var passData, base.profilingSampler))
            {
                passData.cmd = renderingData.commandBuffer;
                passData.cam = camData.camera;
                passData.enableSSR = camData.enableSSR;
                passData.requireHiZ = camData.requiresDepthPyramid;
                passData.requireMinMax = camData.requiresMinMaxDepthPyr;
                TextureHandle prevOpaqueHandle = renderGraph.ImportTexture(RTHandles.Alloc(prevOpaque.renderTexture));
                //builder.ReadTexture(prevOpaqueHandle);
                passData.opaqueTex = prevOpaqueHandle;
                TextureHandle hiZHandle = renderGraph.ImportTexture(prevHiZ.handle);
                //builder.ReadTexture(hiZHandle);
                passData.hiZTex = hiZHandle;
                passData.opaqueID = SLZGlobals.instance.opaqueTexID;
                passData.hiZID = SLZGlobals.instance.prevHiZTexID;
                passData.ssrDisabledKW = SLZGlobals.instance.SSRDisabledKW;
                passData.hiZEnabledKW = SLZGlobals.instance.HiZEnabledKW;
                passData.hiZMinMaxKW = SLZGlobals.instance.HiZMinMaxKW;

                if (camData.xrRendering)
                {
                    passData.generateXrOcclusionMeshDistance = true;
                    passData.xrPass = camData.xrUniversal;
                    passData.xrOcclusionMeshTex = 
                        builder.CreateTransientTexture(tempOcclusionMask(camData.cameraTargetDescriptor));
                    passData.xrOcclusionMeshTexID = ((RTHandle)passData.xrOcclusionMeshTex).nameID;
                }

                builder.AllowPassCulling(false);
                builder.SetRenderFunc((SLZGlobalsData data, RenderGraphContext context) =>
                {
                    ExecutePass(data, ref data.cmd);
                });
            }
        }
    }
}
