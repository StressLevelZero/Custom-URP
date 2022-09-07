using System.Collections;
using System.Collections.Generic;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Linq;
using System;
using UnityEngine;
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

        private Dictionary<Camera, RenderTargetHandle> PerCameraOpaque;
        private Dictionary<Camera, RenderTargetHandle> PerCameraPrevHiZ;
        private uint PerCameraPrevHiZIter = 0;
        private uint PerCameraOpaqueIter = 0;

        private ComputeBuffer SSRGlobalCB;
        private SLZGlobals()
        {
            BlueNoiseCB = new ComputeBuffer(8, sizeof(float), ComputeBufferType.Constant);
            BlueNoiseDim = new float[4];
            hasSetBNTextures = false;
            SSRGlobalCB = new ComputeBuffer(4, sizeof(float), ComputeBufferType.Constant);
            HiZDimBuffer = new ComputeBuffer(15, Marshal.SizeOf<Vector4>());
            SSREnabledKW = GlobalKeyword.Create("_SLZ_SSR_ENABLED");
            HiZEnabledKW = GlobalKeyword.Create("_HIZ_ENABLED");
            HiZMinMaxKW = GlobalKeyword.Create("_HIZ_MIN_MAX_ENABLED");
            PerCameraOpaque = new Dictionary<Camera, RenderTargetHandle>();
            PerCameraPrevHiZ = new Dictionary<Camera, RenderTargetHandle>();
            
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

        public void SetSSRGlobals(int maxSteps, int minMip, float hitRadius, float cameraNear, float cameraFar)
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
            Shader.SetGlobalConstantBuffer(SSRConstantsID, SSRGlobalCB, 0, 16);
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
            cmd.SetGlobalConstantBuffer(SSRGlobalCB, SSRConstantsID, 0, 16);
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
                    Shader.SetGlobalConstantBuffer("BlueNoiseDim", BlueNoiseCB, 0, 16);
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
                Shader.SetGlobalConstantBuffer("BlueNoiseDim", BlueNoiseCB, 0, 16);
            }
        }

        public static void StaticRemoveTempRTsFromList(Camera cam)
        {
            CommandBuffer cmd = CommandBufferPool.Get();
            SLZGlobals.instance.RemoveCameraFromOpaqueList(cmd, cam);
            SLZGlobals.instance.RemoveCameraFromHiZ0List(cmd, cam);
            Graphics.ExecuteCommandBuffer(cmd);
            cmd.Release();
        }

        public void RemoveTempRTStupid()
        {
           
            CommandBuffer cmd = CommandBufferPool.Get();
            bool executeCmd = false;
            List<Camera> RemoveListOpq = new List<Camera>();
            foreach (var OpqPair in PerCameraOpaque)
            {
#if !UNITY_EDITOR
                if (OpqPair.Key == null || OpqPair.Key.isActiveAndEnabled == false)
#else
                if (OpqPair.Key == null)
#endif
                {
                    cmd.ReleaseTemporaryRT(OpqPair.Value.id);
                    RemoveListOpq.Add(OpqPair.Key);
                    executeCmd = true;
                }
            }
            for (int i = 0; i < RemoveListOpq.Count; i++)
            {
                PerCameraOpaque.Remove(RemoveListOpq[i]);
            }


            List<Camera> RemoveListHiZ = new List<Camera>();
            foreach (var HiZPair in PerCameraPrevHiZ)
            {
#if !UNITY_EDITOR
                if (HiZPair.Key == null || HiZPair.Key.isActiveAndEnabled == false)
#else
                if (HiZPair.Key == null)
#endif
                {
                    cmd.ReleaseTemporaryRT(HiZPair.Value.id);
                    RemoveListHiZ.Add(HiZPair.Key);
                    executeCmd = true;
                }
            }
            for (int i = 0; i < RemoveListOpq.Count; i++)
            {
                PerCameraPrevHiZ.Remove(RemoveListHiZ[i]);
            }


            if (executeCmd)
            {
                Debug.Log("Cleaned Old Textures: " + PerCameraOpaque.Count + " " + PerCameraPrevHiZ.Count + " " + PerCameraPrevHiZIter);
                Graphics.ExecuteCommandBuffer(cmd);
            }
            cmd.Release();
        }
        public void RemoveCameraFromOpaqueList(CommandBuffer cmd, Camera cam)
        {
            RenderTargetHandle handle;
            if (PerCameraOpaque.TryGetValue(cam, out handle))
            {
                cmd.ReleaseTemporaryRT(handle.id);
                PerCameraOpaque.Remove(cam);
            }
        }
        public RenderTargetHandle GetCameraOpaque(Camera cam)
        {
            RenderTargetHandle handle;
            if (PerCameraOpaque.TryGetValue(cam, out handle))
            {
                return handle;
            }
            else
            {
                handle = new RenderTargetHandle();
                handle.Init("_CameraOpaqueTexture" + PerCameraOpaqueIter);
                PerCameraOpaqueIter++;
                PerCameraOpaque.Add(cam, handle);
                return handle;
            }
        }

        public void RemoveCameraFromHiZ0List(CommandBuffer cmd, Camera cam)
        {
            RenderTargetHandle handle;
            if (PerCameraPrevHiZ.TryGetValue(cam, out handle))
            {
                cmd.ReleaseTemporaryRT(handle.id);
                PerCameraPrevHiZ.Remove(cam);
            }

        }
        public RenderTargetHandle GetCameraHiZ0(Camera cam)
        {
            RenderTargetHandle handle;
            if (PerCameraPrevHiZ.TryGetValue(cam, out handle))
            {
                return handle;
            }
            else
            {
                handle = new RenderTargetHandle();
                handle.Init("_PrevHiZ0Texture" + PerCameraPrevHiZIter);
                PerCameraPrevHiZIter++;
                PerCameraPrevHiZ.Add(cam, handle);
                return handle;
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
                CommandBuffer cmd = CommandBufferPool.Get();
                if (s_Instance.PerCameraOpaque != null)
                {
                    foreach ( RenderTargetHandle r in s_Instance.PerCameraOpaque.Values)
                    {
                        cmd.ReleaseTemporaryRT(r.id);
                    }
                }
                if (s_Instance.PerCameraPrevHiZ != null)
                {
                    foreach (RenderTargetHandle r in s_Instance.PerCameraPrevHiZ.Values)
                    {
                        cmd.ReleaseTemporaryRT(r.id);
                    }
                }
                Graphics.ExecuteCommandBuffer(cmd);
                s_Instance.PerCameraOpaque = null;
                s_Instance.PerCameraPrevHiZ = null;
                cmd.Release();
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
        private RenderTargetHandle prevOpaque;
        private RenderTargetHandle prevHiZ;
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

            prevOpaque = SLZGlobals.instance.GetCameraOpaque(camData.camera);
            prevHiZ = SLZGlobals.instance.GetCameraHiZ0(camData.camera);
        }
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (enableSSR)
            {
                // Hack to tell unity to store previous frame object to world vectors...
                // Not used by SRP to enable motion vectors or depth but somehow still necessary :(
                renderingData.cameraData.camera.depthTextureMode |= DepthTextureMode.MotionVectors | DepthTextureMode.Depth;
            }

            CommandBuffer cmd = CommandBufferPool.Get();
            //SLZGlobals.instance.SetSSRGlobalsCmd(ref cmd, ssrMinMip, ssrMaxSteps, ssrHitRadius, cameraNear, cameraFar);
            cmd.SetKeyword(SLZGlobals.instance.SSREnabledKW, enableSSR);
            cmd.SetKeyword(SLZGlobals.instance.HiZEnabledKW, requireHiZ);
            cmd.SetKeyword(SLZGlobals.instance.HiZMinMaxKW, requireMinMax);
            cmd.SetGlobalTexture(SLZGlobals.instance.opaqueTexID, prevOpaque.Identifier());
            cmd.SetGlobalTexture(SLZGlobals.instance.prevHiZTexID, prevHiZ.Identifier());
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }
    }
}
