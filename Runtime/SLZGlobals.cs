using System.Collections;
using System.Collections.Generic;
using System.Runtime;
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
        private int[] BlueNoiseDim; // width, height, depth, current slice index 
        private bool hasSetBNTextures;

        private ComputeBuffer SSRGlobalCB;
        private SLZGlobals()
        {
            BlueNoiseCB = new ComputeBuffer(4, sizeof(uint), ComputeBufferType.Constant);
            BlueNoiseDim = new int[4];
            hasSetBNTextures = false;
            SSRGlobalCB = new ComputeBuffer(4, sizeof(float), ComputeBufferType.Constant);
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


        public void SetSSRGlobals()
        {
            /*
             * 0 float _SSRHitRadius;
             * 1 float _SSREdgeFade;
             * 2 int _SSRSteps;
             * 3 none
             */
            float[] SSRGlobalArray = new float[4];
            SSRGlobalArray[0] = 0.1f;
            SSRGlobalArray[1] = 0.1f;
            SSRGlobalArray[2] = 35.0f;
            SSRGlobalArray[3] = 0.0f;
            SSRGlobalCB.SetData(SSRGlobalArray);
            Shader.SetGlobalConstantBuffer("SSRConstants", SSRGlobalCB, 0, 16);
        }


        public void SetBlueNoiseGlobals(Texture2DArray BlueNoiseRGBA, Texture2DArray BlueNoiseR)
        {
            BlueNoiseDim[0] = BlueNoiseRGBA.width;
            BlueNoiseDim[1] = BlueNoiseRGBA.height;
            BlueNoiseDim[2] = BlueNoiseRGBA.depth;
#if UNITY_EDITOR
            if (!EditorApplication.isPlaying)
            {
                BlueNoiseDim[3] = (int)((30.0*EditorApplication.timeSinceStartup) % BlueNoiseRGBA.depth);
                //Debug.Log(BlueNoiseDim[3]);
            }
            else
#endif
            {
                BlueNoiseDim[3] = Mathf.Abs((int)(Time.renderedFrameCount % BlueNoiseRGBA.depth));
            }
            BlueNoiseCB.SetData(BlueNoiseDim);
            Shader.SetGlobalConstantBuffer("BlueNoiseDim", BlueNoiseCB, 0, 16);
            if (!hasSetBNTextures)
            {
                Shader.SetGlobalTexture("_BlueNoiseRGBA", BlueNoiseRGBA);
                Shader.SetGlobalTexture("_BlueNoiseR", BlueNoiseR);
                hasSetBNTextures = true;
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
                    BlueNoiseDim[3] = (int)((144.0*EditorApplication.timeSinceStartup) % depth);
                }
                else
#endif
                {
                    BlueNoiseDim[3] = (int)(Time.renderedFrameCount % depth);
                }
                BlueNoiseCB.SetData(BlueNoiseDim);
                Shader.SetGlobalConstantBuffer("BlueNoiseDim", BlueNoiseCB, 0, 16);
            }
        }

        public static void Dispose()
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
            //s_Instance = null;
        }
    }
}
