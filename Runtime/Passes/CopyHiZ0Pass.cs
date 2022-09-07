
using System;

namespace UnityEngine.Rendering.Universal.Internal
{
    public class SetHiZ0GlobalPass : ScriptableRenderPass
    {
        private RenderTargetIdentifier source { get; set; }
        private int prevHiZ0TextureID;

        public SetHiZ0GlobalPass(RenderPassEvent evt)
        {
            renderPassEvent = evt;
        }
        public void Setup(RenderTargetIdentifier source, int prevHiZ0ID)
        {
            this.source = source;
            prevHiZ0TextureID = prevHiZ0ID;
        }
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            CommandBuffer cmd = CommandBufferPool.Get();

            cmd.SetGlobalTexture(prevHiZ0TextureID, source);
            context.ExecuteCommandBuffer(cmd);
            cmd.Release();
        }
    }
    /// <summary>
    /// Copy the given color buffer to the given destination color buffer.
    ///
    /// You can use this pass to copy a color buffer to the destination,
    /// so you can use it later in rendering. For example, you can copy
    /// the opaque texture to use it for distortion effects.
    /// </summary>
    public class CopyHiZ0Pass : ScriptableRenderPass
    {
        //const int mipTruncation = 3;
        //static int sizeID = Shader.PropertyToID("_Size");
        static int sourceID = Shader.PropertyToID("_Source");
        static int destinationID = Shader.PropertyToID("_Destination");
        static int opaqueTextureDimID = Shader.PropertyToID("_CameraOpaqueTexture_Dim");

        int m_SampleOffsetShaderHandle;
        Material m_SamplingMaterial;
        Downsampling m_DownsamplingMethod;
        Material m_CopyColorMaterial;
        ComputeShader m_ColorPyramidCompute;
        public bool m_RequiresMips;

        private int m_MipLevels;
        private int[] m_Size;
        private int downsampleKernelID;
        private int gaussianKernelID;

        private RenderTargetIdentifier source { get; set; }
        private RTPermanentHandle destination { get; set; }
        private RenderTargetHandle tempBuffer { get; set; }
        private RenderTextureDescriptor tempDescriptor;

        /// <summary>
        /// Create the CopyColorPass
        /// </summary>
        public CopyHiZ0Pass(RenderPassEvent evt, Material samplingMaterial, Material copyColorMaterial = null)
        {
            base.profilingSampler = new ProfilingSampler(nameof(CopyColorPass));

            m_SamplingMaterial = samplingMaterial;
            m_CopyColorMaterial = copyColorMaterial;
            renderPassEvent = evt;
            m_DownsamplingMethod = Downsampling.None;
            m_MipLevels = 1;
            base.useNativeRenderPass = false;
        }

        /// <summary>
        /// Configure the pass with the source and destination to execute on.
        /// </summary>
        /// <param name="source">Source Render Target</param>
        /// <param name="destination">Destination Render Target</param>
        public void Setup(RenderTargetIdentifier source, RTPermanentHandle destination)
        {
            this.source = source;
            this.destination = destination;
            //m_DownsamplingMethod = downsampling;
            //m_RequiresMips = RequiresMips;
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            
            bool requiresMinMax = renderingData.cameraData.requiresMinMaxDepthPyr;
            var descriptor = renderingData.cameraData.cameraTargetDescriptor;
            descriptor.colorFormat = requiresMinMax ? RenderTextureFormat.RGHalf : RenderTextureFormat.RHalf;
            descriptor.width /= 2;
            descriptor.height /= 2;
            descriptor.depthBufferBits = 0;
            descriptor.msaaSamples = 1;
            descriptor.useMipMap = false;
            descriptor.autoGenerateMips = false;
            descriptor.sRGB = false;
            descriptor.enableRandomWrite = false;
            destination.GetRenderTexture(descriptor, renderingData.cameraData.camera.name, "PrevHiZ");
            //cmd.GetTemporaryRT(destination.id, descriptor, FilterMode.Point);
        }

        /// <inheritdoc/>
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (m_SamplingMaterial == null)
            {
                Debug.LogErrorFormat("Missing {0}. {1} render pass will not execute. Check for missing reference in the renderer resources.", m_SamplingMaterial, GetType().Name);
                return;
            }

            CommandBuffer cmd = CommandBufferPool.Get();

            using (new ProfilingScope(cmd, ProfilingSampler.Get(URPProfileId.StoreHiZ0)))
            {
                //RenderTargetIdentifier oldHiZRT = destination.Identifier();

                ScriptableRenderer.SetRenderTarget(cmd, destination.renderTexture, BuiltinRenderTextureType.CameraTarget, clearFlag,
                    clearColor);

                bool useDrawProceduleBlit = renderingData.cameraData.xr.enabled;
                RenderingUtils.Blit(cmd, source, destination.renderTexture, m_CopyColorMaterial, 0, useDrawProceduleBlit);
            }
            // In shader, we need to know how many mip levels to 1x1 and not actually how many mips there are, so re-add mipTruncation to the true number of mips
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        /// <inheritdoc/>
        public override void OnCameraCleanup(CommandBuffer cmd)
        {
            if (cmd == null)
                throw new ArgumentNullException("cmd");

            //if (destination != RenderTargetHandle.CameraTarget)
            //{
            //    cmd.ReleaseTemporaryRT(destination.id);
            //    destination = RenderTargetHandle.CameraTarget;
            //}
        }
    }
}
