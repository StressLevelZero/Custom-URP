#if FALSE
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;
using HTC.UnityPlugin.FoveatedRendering;


namespace UnityEngine.Rendering.Universal
{
    public class SLZVRSManager
    {
        static SLZVRSManager s_Instance;
        public static SLZVRSManager Instance
        { 
            get
            {
                if (s_Instance == null)
                {
                    s_Instance = new SLZVRSManager();
                }
                return s_Instance;
            }      
         }


        public bool hasIntialized { get; private set; }

        public void Initialize(float verticalFOV, float aspectRatio)
        {
            ViveFoveatedRenderingAPI.InitializeNativeLogger(str => Debug.Log(str));
            //Debug.Log("Called Initizalize");
            HTC.UnityPlugin.FoveatedRendering.RenderMode renderMode;
            if (XRSettings.enabled)
            {
                switch (XRSettings.stereoRenderingMode)
                {
                    case XRSettings.StereoRenderingMode.SinglePassInstanced:
                    case XRSettings.StereoRenderingMode.SinglePass:
                        renderMode = HTC.UnityPlugin.FoveatedRendering.RenderMode.RENDER_MODE_STEREO;
                        break;
                    default:
                        renderMode = HTC.UnityPlugin.FoveatedRendering.RenderMode.RENDER_MODE_MONO;
                        break;
                }
            }
            else
            {
                renderMode = HTC.UnityPlugin.FoveatedRendering.RenderMode.RENDER_MODE_MONO;
            }
            ViveFoveatedRenderingAPI.SetRenderMode(renderMode);

            ViveFoveatedRenderingAPI.SetNormalizedGazeDirection(new Vector3(0.0f, 0.0f, 1.0f), new Vector3(0.0f, 0.0f, 1.0f));
            GL.IssuePluginEvent(ViveFoveatedRenderingAPI.GetRenderEventFunc(), (int)EventID.UPDATE_GAZE);
            ViveFoveatedRenderingAPI.SetShadingRate(TargetArea.INNER, ShadingRate.X1_PER_4X4_PIXELS);
            ViveFoveatedRenderingAPI.SetShadingRate(TargetArea.PERIPHERAL, ShadingRate.X1_PER_4X4_PIXELS);
            ViveFoveatedRenderingAPI.SetShadingRate(TargetArea.MIDDLE, ShadingRate.X1_PER_4X4_PIXELS);
            hasIntialized = ViveFoveatedRenderingAPI.InitializeFoveatedRendering(verticalFOV, aspectRatio);
        }

        public void Dispose()
        {
            ViveFoveatedRenderingAPI.ReleaseFoveatedRendering();
            ViveFoveatedRenderingAPI.ReleaseNativeLogger();
        }
    }
    public class SLZFoveatedRenderingEnable : ScriptableRenderPass
    {     
        public SLZFoveatedRenderingEnable(RenderPassEvent evt)
        {
            renderPassEvent = evt;
        }
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (SLZVRSManager.Instance.hasIntialized)
            {
                CommandBuffer cmd = CommandBufferPool.Get();
                cmd.IssuePluginEvent(ViveFoveatedRenderingAPI.GetRenderEventFunc(), (int)EventID.ENABLE_FOVEATED_RENDERING);
                //cmd.ClearRenderTarget(false, true, Color.black);

                ViveFoveatedRenderingAPI.SetNormalizedGazeDirection(new Vector3(0.0f, 0.0f, 1.0f), new Vector3(0.0f, 0.0f, 1.0f));
                GL.IssuePluginEvent(ViveFoveatedRenderingAPI.GetRenderEventFunc(), (int)EventID.UPDATE_GAZE);
                ViveFoveatedRenderingAPI.SetShadingRate(TargetArea.INNER, ShadingRate.CULL);
                ViveFoveatedRenderingAPI.SetShadingRate(TargetArea.PERIPHERAL, ShadingRate.X1_PER_4X4_PIXELS);
                ViveFoveatedRenderingAPI.SetShadingRate(TargetArea.MIDDLE, ShadingRate.X1_PER_4X4_PIXELS);
                context.ExecuteCommandBuffer(cmd);
                CommandBufferPool.Release(cmd);
            }
        }
    }
    public class SLZFoveatedRenderingDisable : ScriptableRenderPass
    {
        public SLZFoveatedRenderingDisable(RenderPassEvent evt)
        {
            renderPassEvent = evt;
        }
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            CommandBuffer cmd = CommandBufferPool.Get();
            cmd.IssuePluginEvent(ViveFoveatedRenderingAPI.GetRenderEventFunc(), (int)EventID.DISABLE_FOVEATED_RENDERING);
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }
    }
}
#endif