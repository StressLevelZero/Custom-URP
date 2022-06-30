
using System;

namespace UnityEngine.Rendering.Universal.Internal
{
    /// <summary>
    /// Copy the given color buffer to the given destination color buffer.
    ///
    /// You can use this pass to copy a color buffer to the destination,
    /// so you can use it later in rendering. For example, you can copy
    /// the opaque texture to use it for distortion effects.
    /// </summary>
    public class CopyColorPass : ScriptableRenderPass
    {
        const int mipTruncation = 3;
        static int sizeID = Shader.PropertyToID("_Size");
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
        private RenderTargetHandle destination { get; set; }
        private RenderTargetHandle tempBuffer { get; set; }
        private RenderTextureDescriptor tempDescriptor;

        /// <summary>
        /// Create the CopyColorPass
        /// </summary>
        public CopyColorPass(RenderPassEvent evt, Material samplingMaterial, ComputeShader colorPyramid, Material copyColorMaterial = null)
        {
            base.profilingSampler = new ProfilingSampler(nameof(CopyColorPass));

            m_SamplingMaterial = samplingMaterial;
            m_CopyColorMaterial = copyColorMaterial;
            m_SampleOffsetShaderHandle = Shader.PropertyToID("_SampleOffset");
            renderPassEvent = evt;
            m_ColorPyramidCompute = colorPyramid;
            downsampleKernelID = m_ColorPyramidCompute.FindKernel("KColorDownsample");
            gaussianKernelID = m_ColorPyramidCompute.FindKernel("KColorGaussian");
            m_DownsamplingMethod = Downsampling.None;
            m_MipLevels = 1;
            base.useNativeRenderPass = false;
        }

        /// <summary>
        /// Configure the pass with the source and destination to execute on.
        /// </summary>
        /// <param name="source">Source Render Target</param>
        /// <param name="destination">Destination Render Target</param>
        public void Setup(RenderTargetIdentifier source, RenderTargetHandle destination, Downsampling downsampling, bool RequiresMips)
        {
            this.source = source;
            this.destination = destination;
            m_DownsamplingMethod = downsampling;
            m_RequiresMips = RequiresMips;
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            RenderTextureDescriptor descriptor = renderingData.cameraData.cameraTargetDescriptor;
            descriptor.msaaSamples = 1;
            descriptor.depthBufferBits = 0;
            if (m_DownsamplingMethod == Downsampling._2xBilinear)
            {
                descriptor.width /= 2;
                descriptor.height /= 2;
            }
            else if (m_DownsamplingMethod == Downsampling._4xBox || m_DownsamplingMethod == Downsampling._4xBilinear)
            {
                descriptor.width /= 4;
                descriptor.height /= 4;
            }
            descriptor.autoGenerateMips = false;
            if (m_RequiresMips)
            {
                descriptor.useMipMap = m_RequiresMips;
               
                descriptor.enableRandomWrite = true;
                // mips with smallest dimension of 1, 2, and 4 useless, and compute shader works on 8x8 blocks, so subtract 3 (mipTruncation) from the mip count
                m_MipLevels = Mathf.FloorToInt(
                    Mathf.Max( Mathf.Log(descriptor.width, 2), Mathf.Log(descriptor.height, 2))
                    ) + 1 - mipTruncation;
                descriptor.mipCount = m_MipLevels;
            }
            m_Size = new int[4] { descriptor.width, descriptor.height, 0, 0 };
            cmd.GetTemporaryRT(destination.id, descriptor, m_RequiresMips == true ? FilterMode.Trilinear : FilterMode.Bilinear);
            //cmd.GetTemporaryRT(destination.id, descriptor, FilterMode.Bilinear,);
            if (m_RequiresMips)
            {
                tempDescriptor = descriptor;
                tempDescriptor.width /= 2;
                tempDescriptor.height /= 2;
                tempDescriptor.useMipMap = false;
                tempDescriptor.enableRandomWrite = true;
            }
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

            //It is possible that the given color target is now the frontbuffer
            if (source == renderingData.cameraData.renderer.GetCameraColorFrontBuffer(cmd))
            {
                source = renderingData.cameraData.renderer.cameraColorTarget;
            }

            using (new ProfilingScope(cmd, ProfilingSampler.Get(URPProfileId.CopyColor)))
            {
                RenderTargetIdentifier opaqueColorRT = destination.Identifier();

                ScriptableRenderer.SetRenderTarget(cmd, opaqueColorRT, BuiltinRenderTextureType.CameraTarget, clearFlag,
                    clearColor);

                bool useDrawProceduleBlit = renderingData.cameraData.xr.enabled;
                switch (m_DownsamplingMethod)
                {
                    case Downsampling.None:
                        RenderingUtils.Blit(cmd, source, opaqueColorRT, m_CopyColorMaterial, 0, useDrawProceduleBlit);
                        break;
                    case Downsampling._2xBilinear:
                        RenderingUtils.Blit(cmd, source, opaqueColorRT, m_CopyColorMaterial, 0, useDrawProceduleBlit);
                        break;
                    case Downsampling._4xBox:
                        m_SamplingMaterial.SetFloat(m_SampleOffsetShaderHandle, 2);
                        RenderingUtils.Blit(cmd, source, opaqueColorRT, m_SamplingMaterial, 0, useDrawProceduleBlit);
                        break;
                    case Downsampling._4xBilinear:
                        RenderingUtils.Blit(cmd, source, opaqueColorRT, m_CopyColorMaterial, 0, useDrawProceduleBlit);
                        break;
                }
            }
            // In shader, we need to know how many mip levels to 1x1 and not actually how many mips there are, so re-add mipTruncation to the true number of mips
            Shader.SetGlobalVector(opaqueTextureDimID, 
                new Vector4( tempDescriptor.width * 2, tempDescriptor.height * 2, tempDescriptor.volumeDepth * 2, m_MipLevels + mipTruncation));

            if (m_MipLevels > 1)
            {
                int slices = 1;
#if ENABLE_VR && ENABLE_XR_MODULE
                if (renderingData.cameraData.xr.enabled)
                {
                    slices = 2;
                }
#endif
                int[] mipSize = new int[4];
                Array.Copy(m_Size, mipSize, 4);
                tempBuffer = new RenderTargetHandle();
                cmd.GetTemporaryRT(tempBuffer.id, tempDescriptor, FilterMode.Bilinear);
                cmd.SetComputeIntParams(m_ColorPyramidCompute, sizeID, mipSize);
                cmd.SetComputeTextureParam(m_ColorPyramidCompute, downsampleKernelID, destinationID, tempBuffer.Identifier(), 0);
                cmd.SetComputeTextureParam(m_ColorPyramidCompute, gaussianKernelID, sourceID, tempBuffer.Identifier(), 0);
                for (int i = 1; i < m_MipLevels; i++)
                {
                   
                    cmd.SetComputeTextureParam(m_ColorPyramidCompute, downsampleKernelID, sourceID, destination.Identifier(), i - 1);
                    
                    mipSize[0] = Mathf.Max(mipSize[0] >> 1, 1);
                    mipSize[1] = Mathf.Max(mipSize[1] >> 1, 1);
                    cmd.DispatchCompute(m_ColorPyramidCompute, downsampleKernelID, Mathf.CeilToInt((float)mipSize[0] / 8.0f + 0.00001f),
                                                                                   Mathf.CeilToInt((float)mipSize[1] / 8.0f + 0.00001f), slices); ;
                    
                    cmd.SetComputeIntParams(m_ColorPyramidCompute, sizeID, mipSize);
                    
                    cmd.SetComputeTextureParam(m_ColorPyramidCompute, gaussianKernelID, destinationID, destination.Identifier(), i);
                    cmd.DispatchCompute(m_ColorPyramidCompute, gaussianKernelID, Mathf.CeilToInt((float)mipSize[0] / 8.0f + 0.0001f),
                                                                                 Mathf.CeilToInt((float)mipSize[1] / 8.0f + 0.0001f), slices);
                    
                }
                cmd.ReleaseTemporaryRT(tempBuffer.id);
            }
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        /// <inheritdoc/>
        public override void OnCameraCleanup(CommandBuffer cmd)
        {
            if (cmd == null)
                throw new ArgumentNullException("cmd");

            if (destination != RenderTargetHandle.CameraTarget)
            {
                cmd.ReleaseTemporaryRT(destination.id);
                destination = RenderTargetHandle.CameraTarget;
            }
        }
    }
}
