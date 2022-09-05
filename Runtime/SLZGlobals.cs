using System.Collections;
using System.Collections.Generic;
using System.Runtime;
using System.Runtime.InteropServices;
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
       
        public GlobalKeyword HiZEnabledKW { get; private set; }
        public GlobalKeyword HiZMinMaxKW { get; private set; }
        public GlobalKeyword SSREnabledKW { get; private set; }

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
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }
    }
}
