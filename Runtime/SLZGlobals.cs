using System.Collections;
using System.Collections.Generic;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Linq;
using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering.RenderGraphModule;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace UnityEngine.Rendering.Universal
{
    public class SLZGlobals
    {
        static SLZGlobals s_Instance;
        // Blue Noise
        private ComputeBuffer BlueNoiseCB;
        private ComputeBuffer HiZDimBuffer;
        private float[] BlueNoiseDim; // width, height, depth, current slice index 
        private bool hasSetBNTextures;
#if UNITY_EDITOR
        private static long framecount = 0;
        private static double timeSinceStartup = 0.0;
#endif
        //private int HiZDimBufferID = Shader.PropertyToID("HiZDimBuffer");
        private int HiZMipNumID = Shader.PropertyToID("_HiZHighestMip");
        private int HiZDimID = Shader.PropertyToID("_HiZDim");
        private int SSRConstantsID = Shader.PropertyToID("SSRConstants");
        private int CameraOpaqueTextureID = Shader.PropertyToID("_CameraOpaqueTexture");
        private int PrevHiZ0TextureID = Shader.PropertyToID("_PrevHiZ0Texture");
        public int opaqueTexID { get { return CameraOpaqueTextureID; } }
        public int prevHiZTexID { get { return PrevHiZ0TextureID; } }

        public GlobalKeyword HiZEnabledKW { get; private set; }
        public GlobalKeyword HiZMinMaxKW { get; private set; }
        public GlobalKeyword SSREnabledKW { get; private set; }

        public SLZPerCameraRTStorage PerCameraOpaque;
        public SLZPerCameraRTStorage PerCameraPrevHiZ;
        private uint PerCameraPrevHiZIter = 0;
        private uint PerCameraOpaqueIter = 0;

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
            BlueNoiseDim = new float[4];
            hasSetBNTextures = false;
            SSRGlobalCB = new ComputeBuffer(8, sizeof(float), ComputeBufferType.Constant);
            HiZDimBuffer = new ComputeBuffer(15, Marshal.SizeOf<Vector4>());
            SSREnabledKW = GlobalKeyword.Create("_SLZ_SSR_ENABLED");
            HiZEnabledKW = GlobalKeyword.Create("_HIZ_ENABLED");
            HiZMinMaxKW = GlobalKeyword.Create("_HIZ_MIN_MAX_ENABLED");
            PerCameraOpaque = new SLZPerCameraRTStorage();
            PerCameraPrevHiZ = new SLZPerCameraRTStorage();
            
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

        public void SetHiZSSRKeyWords(bool enableSSR, bool requireHiZ, bool requireMinMax)
        {
            Shader.SetKeyword(SSREnabledKW, enableSSR);
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

        public void SetSSRGlobals(int maxSteps, int minMip, float hitRadius, float temporalWeight, float fov, int screenHeight)
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
            float expConst = Mathf.Exp(-framerateConst);
            float FRTemporal = (Mathf.Exp(-framerateConst * temporalWeight) - expConst) * (1.0f / (1.0f - expConst));
            //Debug.Log(FRTemporal);
            SSRGlobalArray._SSRTemporalWeight = Mathf.Clamp(1.0f - temporalWeight, 0.0078f, 1.0f); //Mathf.Clamp(FRTemporal, 0.0078f, 1.0f); 
            SSRGlobalArray._SSRSteps = maxSteps;
            SSRGlobalArray._SSRMinMip = minMip;
            float halfTan = Mathf.Tan(Mathf.Deg2Rad * (fov * 0.5f));
            SSRGlobalArray._SSRDistScale = halfTan / (0.5f * (float)screenHeight); // rcp(0.5*_ScreenParams.y * UNITY_MATRIX_P._m11)
            //Debug.Log("SSR scale: " + SSRGlobalArray[4]);
            SSRGlobalCB.SetData<SSRBufferData>(new List<SSRBufferData>() { SSRGlobalArray });
            Shader.SetGlobalConstantBuffer(SSRConstantsID, SSRGlobalCB, 0, SSRGlobalCB.count * SSRGlobalCB.stride);
        }

        public void SetSSRGlobalsCmd(ref CommandBuffer cmd, int maxSteps, int minMip, float hitRadius, float cameraNear, float cameraFar)
        {
            /*
             * 0 float _SSRHitRadius;
             * 1 float _SSREdgeFade;
             * 2 int _SSRSteps;
             * 3 none
             */
            float[] SSRGlobalArray = new float[4];
            //SSRGlobalArray[0] = 1.0f / (1.0f + hitRadius);//hitRadius;
            SSRGlobalArray[0] = hitRadius;
            SSRGlobalArray[1] = -cameraNear / (cameraFar - cameraNear) * (hitRadius * SSRGlobalArray[0]);
            SSRGlobalArray[2] = maxSteps;
            SSRGlobalArray[3] = BitConverter.Int32BitsToSingle(minMip);
            SSRGlobalCB.SetData(SSRGlobalArray);
            //Debug.Log("SSR scale: " + SSRGlobalArray[3]);
            cmd.SetGlobalConstantBuffer(SSRGlobalCB, SSRConstantsID, 0, SSRGlobalCB.count * SSRGlobalCB.stride);
        }


        public void SetBlueNoiseGlobals(Texture2DArray BlueNoiseRGBA, Texture2DArray BlueNoiseR)
        {
            if (BlueNoiseRGBA != null)
            {
                BlueNoiseDim = new float[8];
                BlueNoiseDim[0] = BlueNoiseRGBA.width;
                BlueNoiseDim[1] = BlueNoiseRGBA.height;
                BlueNoiseDim[2] = BlueNoiseRGBA.depth;
                BlueNoiseDim[3] = (float) Random.Range(0, BlueNoiseRGBA.width);
                BlueNoiseDim[4] = (float) Random.Range(0, BlueNoiseRGBA.height);
#if UNITY_EDITOR
                if (!EditorApplication.isPlaying)
                {
                    if (timeSinceStartup != EditorApplication.timeSinceStartup)
                    {
                        timeSinceStartup = EditorApplication.timeSinceStartup;
                        framecount++;
                    }
                    BlueNoiseDim[3] = (int)(framecount % BlueNoiseRGBA.depth);
                    //Debug.Log(BlueNoiseDim[3]);
                }
                else
#endif
                {
                    BlueNoiseDim[3] = Mathf.Abs((int)(Time.renderedFrameCount % BlueNoiseRGBA.depth));
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
                long depth = (long)BlueNoiseDim[2];
#if UNITY_EDITOR
                if (!EditorApplication.isPlaying)
                {
                    BlueNoiseDim[3] = (int)((Screen.currentResolution.refreshRate * EditorApplication.timeSinceStartup) % depth);
                }
                else
#endif
                {
                    BlueNoiseDim[3] = (int)((Time.timeSinceLevelLoadAsDouble * Screen.currentResolution.refreshRate) % depth);
                }
                BlueNoiseCB.SetData(BlueNoiseDim);
                Shader.SetGlobalConstantBuffer("BlueNoiseDim", BlueNoiseCB, 0, BlueNoiseCB.count * BlueNoiseCB.stride);
            }
        }

      

        public void RemoveTempRTStupid()
        {
            PerCameraOpaque.RemoveAllNull();
            PerCameraPrevHiZ.RemoveAllNull();
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
               
                if (s_Instance.PerCameraOpaque != null)
                {
                    s_Instance.PerCameraOpaque.Dispose();
                }
                if (s_Instance.PerCameraPrevHiZ != null)
                {
                    s_Instance.PerCameraPrevHiZ.Dispose();
                }
               
            }
            s_Instance = null;
        }

    }


    public class SLZGlobalsSetPass : ScriptableRenderPass
    {
        private bool enableSSR;
        private bool requireHiZ;
        private bool requireMinMax;

        private float ssrHitRadius;
        private int ssrMaxSteps;
        private int ssrMinMip;
        private float cameraNear;
        private float cameraFar;
        private Camera camera;
        private RTPermanentHandle prevOpaque;
        private RTPermanentHandle prevHiZ;
        public SLZGlobalsSetPass(RenderPassEvent evt)
        {
            renderPassEvent = evt;
        }
        public void Setup(CameraData camData)
        {
            enableSSR = camData.enableSSR;
            requireHiZ = camData.requiresDepthPyramid;
            requireMinMax = camData.requiresMinMaxDepthPyr;

            ssrHitRadius = camData.SSRHitRadius;
            ssrMaxSteps = camData.maxSSRSteps;
            ssrMinMip = camData.SSRMinMip;
            cameraNear = camData.camera.nearClipPlane;
            cameraFar = camData.camera.farClipPlane;

            prevOpaque = SLZGlobals.instance.PerCameraOpaque.GetHandle(camData.camera);
            prevHiZ = SLZGlobals.instance.PerCameraPrevHiZ.GetHandle(camData.camera);

            // Hack to tell unity to store previous frame object to world vectors...
            // Not used by SRP to enable motion vectors or depth but somehow still necessary :(
            if (camData.enableSSR)
            {
                camData.camera.depthTextureMode |= DepthTextureMode.MotionVectors | DepthTextureMode.Depth;
            }

            //Debug.Log("Setup for " + camData.camera.name);
        }


        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            SLZGlobalsData passData = new SLZGlobalsData();
            CameraData camData = renderingData.cameraData;
            prevOpaque = SLZGlobals.instance.PerCameraOpaque.GetHandle(camData.camera);
            prevHiZ = SLZGlobals.instance.PerCameraPrevHiZ.GetHandle(camData.camera);
            passData.cmd = renderingData.commandBuffer;
            passData.cam = camData.camera;
            passData.enableSSR = camData.enableSSR;
            passData.requireHiZ = camData.requiresDepthPyramid;
            passData.requireMinMax = camData.requiresMinMaxDepthPyr;
            passData.opaqueTex = prevOpaque.handle;
            passData.hiZTex = prevHiZ.handle;
            passData.opaqueID = SLZGlobals.instance.opaqueTexID;
            passData.hiZID = SLZGlobals.instance.prevHiZTexID;
            passData.ssrEnabledKW = SLZGlobals.instance.SSREnabledKW;
            passData.hiZEnabledKW = SLZGlobals.instance.HiZEnabledKW;
            passData.hiZMinMaxKW = SLZGlobals.instance.HiZMinMaxKW;

            ExecutePass(passData, ref passData.cmd);
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
            GlobalKeyword ssrEnabledKW = data.ssrEnabledKW;
            GlobalKeyword hiZEnabledKW = data.hiZEnabledKW;
            GlobalKeyword hiZMinMaxKW = data.hiZMinMaxKW;
           
            using (new ProfilingScope(cmd, ProfilingSampler.Get(URPProfileId.SetSLZGlobals)))
            {

                if (enableSSR)
                {
                    cmd.SetGlobalTexture(opaqueID, opaqueTex);
                    cmd.SetGlobalTexture(hiZID, hiZTex);
                }

                //SLZGlobals.instance.SetSSRGlobalsCmd(ref cmd, ssrMinMip, ssrMaxSteps, ssrHitRadius, cameraNear, cameraFar);
                cmd.SetKeyword(ssrEnabledKW, enableSSR);
                cmd.SetKeyword(hiZEnabledKW, requireHiZ);
                cmd.SetKeyword(hiZMinMaxKW, requireMinMax);
            }
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
            public GlobalKeyword ssrEnabledKW;
            public GlobalKeyword hiZEnabledKW;
            public GlobalKeyword hiZMinMaxKW;
        }

        internal void Render(RenderGraph renderGraph, ref RenderingData renderingData)
        {
            CameraData camData = renderingData.cameraData;
            prevOpaque = SLZGlobals.instance.PerCameraOpaque.GetHandle(camData.camera);
            prevHiZ = SLZGlobals.instance.PerCameraPrevHiZ.GetHandle(camData.camera);
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
                passData.ssrEnabledKW = SLZGlobals.instance.SSREnabledKW;
                passData.hiZEnabledKW = SLZGlobals.instance.HiZEnabledKW;
                passData.hiZMinMaxKW = SLZGlobals.instance.HiZMinMaxKW;
                builder.AllowPassCulling(false);
                builder.SetRenderFunc((SLZGlobalsData data, RenderGraphContext context) =>
                {
                    ExecutePass(data, ref data.cmd);
                });
            }
        }
    }
}
