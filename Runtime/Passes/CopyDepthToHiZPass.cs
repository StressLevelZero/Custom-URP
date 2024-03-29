using System;
using UnityEngine.Experimental.Rendering;

namespace UnityEngine.Rendering.Universal.Internal
{
    /// <summary>
    /// Copy the given depth buffer into the given destination depth buffer.
    ///
    /// You can use this pass to copy a depth buffer to a destination,
    /// so you can use it later in rendering. If the source texture has MSAA
    /// enabled, the pass uses a custom MSAA resolve. If the source texture
    /// does not have MSAA enabled, the pass uses a Blit or a Copy Texture
    /// operation, depending on what the current platform supports.
    /// </summary>
    public class CopyDepthToHiZPass : ScriptableRenderPass
    {
        private static int computeParamID = Shader.PropertyToID("data1");
        private static int computeParam2ID = Shader.PropertyToID("data2");
        private static int computeMipSourceID = Shader.PropertyToID("_MipSource");
        private static int computeMipDestID = Shader.PropertyToID("_MipDest");
        private static int computeMipDest2ID = Shader.PropertyToID("_MipDest2");
        
        private RenderTargetHandle source { get; set; }
        private RenderTargetHandle destination { get; set; }
        internal bool AllocateRT { get; set; }
        internal int MssaSamples { get; set; }
        private int mipLevels;
        private bool isArray;
        private bool requiresMinMax;
        Material m_CopyDepthToColorMaterial;
        public static ComputeShader m_HiZMipCompute;
        private GlobalKeyword m_StereoArrayKW;
        private LocalKeyword m_SRVSourceKW;
        private LocalKeyword m_MinMaxKW;
        public CopyDepthToHiZPass(RenderPassEvent evt, Material copyDepthToColorMaterial)
        {
            base.profilingSampler = new ProfilingSampler(nameof(CopyDepthPass));
            AllocateRT = true;
            m_CopyDepthToColorMaterial = copyDepthToColorMaterial;
            renderPassEvent = evt;
        }

        /// <summary>
        /// Configure the pass with the source and destination to execute on.
        /// </summary>
        /// <param name="source">Source Render Target</param>
        /// <param name="destination">Destination Render Targt</param>
        public void Setup(RenderTargetHandle source, RenderTargetHandle destination, bool requiresMinMax)
        {
            this.source = source;
            this.destination = destination;
            this.AllocateRT = true;// !destination.HasInternalRenderTargetId();
            this.MssaSamples = -1;
            this.requiresMinMax = requiresMinMax;
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            var descriptor = renderingData.cameraData.cameraTargetDescriptor;
            descriptor.colorFormat = requiresMinMax ? RenderTextureFormat.RGHalf : RenderTextureFormat.RHalf;
            descriptor.width /= 2;
            descriptor.height /= 2;
            descriptor.depthBufferBits = 0;
            descriptor.msaaSamples = 1;
            descriptor.useMipMap = true;
            descriptor.autoGenerateMips = false;
            descriptor.sRGB = false;
            descriptor.enableRandomWrite = true;
            mipLevels = Mathf.FloorToInt(
                Mathf.Max(
                    Mathf.Log(descriptor.width, 2),
                    Mathf.Log(descriptor.height, 2)
                    )
                ) + 1;
            isArray = descriptor.dimension == TextureDimension.Tex2DArray;
            if (this.AllocateRT)
                cmd.GetTemporaryRT(destination.id, descriptor, FilterMode.Point);



            // On Metal iOS, prevent camera attachments to be bound and cleared during this pass.
            ConfigureTarget(new RenderTargetIdentifier(destination.Identifier(), 0, CubemapFace.Unknown, -1), descriptor.depthStencilFormat, descriptor.width, descriptor.height, descriptor.msaaSamples, false);
            ConfigureClear(ClearFlag.None, Color.black);
        }

        /// <inheritdoc/>
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (m_CopyDepthToColorMaterial == null)
            {
                Debug.LogErrorFormat("Missing {0}. {1} render pass will not execute. Check for missing reference in the renderer resources.", m_CopyDepthToColorMaterial, GetType().Name);
                return;
            }
            CommandBuffer cmd = CommandBufferPool.Get();
            using (new ProfilingScope(cmd, ProfilingSampler.Get(URPProfileId.HiZPrepass)))
            {
                /*
                int cameraSamples = 0;
                if (MssaSamples == -1)
                {
                    RenderTextureDescriptor descriptor = renderingData.cameraData.cameraTargetDescriptor;
                    cameraSamples = descriptor.msaaSamples;
                }
                else
                    cameraSamples = MssaSamples;

                // When auto resolve is supported or multisampled texture is not supported, set camera samples to 1
                if (SystemInfo.supportsMultisampleAutoResolve || SystemInfo.supportsMultisampledTextures == 0)
                    cameraSamples = 1;
                cameraSamples = 1;
                CameraData cameraData = renderingData.cameraData;

                switch (cameraSamples)
                {
                    case 8:
                        cmd.DisableShaderKeyword(ShaderKeywordStrings.DepthMsaa2);
                        cmd.DisableShaderKeyword(ShaderKeywordStrings.DepthMsaa4);
                        cmd.EnableShaderKeyword(ShaderKeywordStrings.DepthMsaa8);
                        break;

                    case 4:
                        cmd.DisableShaderKeyword(ShaderKeywordStrings.DepthMsaa2);
                        cmd.EnableShaderKeyword(ShaderKeywordStrings.DepthMsaa4);
                        cmd.DisableShaderKeyword(ShaderKeywordStrings.DepthMsaa8);
                        break;

                    case 2:
                        cmd.EnableShaderKeyword(ShaderKeywordStrings.DepthMsaa2);
                        cmd.DisableShaderKeyword(ShaderKeywordStrings.DepthMsaa4);
                        cmd.DisableShaderKeyword(ShaderKeywordStrings.DepthMsaa8);
                        break;

                    // MSAA disabled, auto resolve supported or ms textures not supported
                    default:
                        cmd.DisableShaderKeyword(ShaderKeywordStrings.DepthMsaa2);
                        cmd.DisableShaderKeyword(ShaderKeywordStrings.DepthMsaa4);
                        cmd.DisableShaderKeyword(ShaderKeywordStrings.DepthMsaa8);
                        break;
                }

                cmd.SetGlobalTexture("_CameraDepthAttachment", source.Identifier());


#if ENABLE_VR && ENABLE_XR_MODULE
                // XR uses procedural draw instead of cmd.blit or cmd.DrawFullScreenMesh
                if (renderingData.cameraData.xr.enabled)
                {
                    // XR flip logic is not the same as non-XR case because XR uses draw procedure
                    // and draw procedure does not need to take projection matrix yflip into account
                    // We y-flip if
                    // 1) we are bliting from render texture to back buffer and
                    // 2) renderTexture starts UV at top
                    // XRTODO: handle scalebias and scalebiasRt for src and dst separately
                    bool isRenderToBackBufferTarget = destination.Identifier() == cameraData.xr.renderTarget && !cameraData.xr.renderTargetIsRenderTexture;
                    bool yflip = isRenderToBackBufferTarget && SystemInfo.graphicsUVStartsAtTop;
                    float flipSign = (yflip) ? -1.0f : 1.0f;
                    Vector4 scaleBiasRt = (flipSign < 0.0f)
                        ? new Vector4(flipSign, 1.0f, -1.0f, 1.0f)
                        : new Vector4(flipSign, 0.0f, 1.0f, 1.0f);
                    cmd.SetGlobalVector(ShaderPropertyId.scaleBiasRt, scaleBiasRt);

                    cmd.DrawProcedural(Matrix4x4.identity, m_CopyDepthToColorMaterial, 0, MeshTopology.Quads, 4);
                }
                else
#endif
                {
                    // Blit has logic to flip projection matrix when rendering to render texture.
                    // Currently the y-flip is handled in CopyDepthPass.hlsl by checking _ProjectionParams.x
                    // If you replace this Blit with a Draw* that sets projection matrix double check
                    // to also update shader.
                    // scaleBias.x = flipSign
                    // scaleBias.y = scale
                    // scaleBias.z = bias
                    // scaleBias.w = unused
                    // In game view final target acts as back buffer were target is not flipped
                    bool isGameViewFinalTarget = (cameraData.cameraType == CameraType.Game && destination == RenderTargetHandle.CameraTarget);
                    bool yflip = (cameraData.IsCameraProjectionMatrixFlipped()) && !isGameViewFinalTarget;
                    float flipSign = yflip ? -1.0f : 1.0f;
                    Vector4 scaleBiasRt = (flipSign < 0.0f)
                        ? new Vector4(flipSign, 1.0f, -1.0f, 1.0f)
                        : new Vector4(flipSign, 0.0f, 1.0f, 1.0f);
                    cmd.SetGlobalVector(ShaderPropertyId.scaleBiasRt, scaleBiasRt);

                    cmd.DrawMesh(RenderingUtils.fullscreenMesh, Matrix4x4.identity, m_CopyDepthToColorMaterial);
                }
                */
                int width = renderingData.cameraData.cameraTargetDescriptor.width;
                int height = renderingData.cameraData.cameraTargetDescriptor.height;
                int[] widthHeight = new int[4];
                int[] data2 = new int[4];
                widthHeight[0] = width;
                widthHeight[1] = height;
                int highestMip = mipLevels - 1;
                int i = 0;
                int slices = 1;
               
                if (m_SRVSourceKW == null)
                {
                    m_SRVSourceKW = new LocalKeyword(m_HiZMipCompute, "SRV_SOURCE");
                }
               

                m_MinMaxKW = new LocalKeyword(m_HiZMipCompute, "MIN_AND_MAX");
                if (requiresMinMax)
                {
                    cmd.EnableKeyword(m_HiZMipCompute, m_MinMaxKW);
                }
                else
                {
                    cmd.DisableKeyword(m_HiZMipCompute, m_MinMaxKW);
                }
                //if (m_StereoArrayKW == null)
                //{
                //m_StereoArrayKW = new GlobalKeyword("STEREO_INSTANCING_ON");
                //}
#if ENABLE_VR && ENABLE_XR_MODULE
                if (renderingData.cameraData.xr.enabled)
                {
                    slices = 2;
                }
#else
                cmd.DisableKeyword(m_HiZMipCompute, m_StereoArrayKW);
#endif
                do
                {

                    widthHeight[2] = width >> (i);
                    widthHeight[3] = height >> (i);

                    widthHeight[0] = width >> (i + 1);
                    widthHeight[0] = widthHeight[0] == 0 ? 1 : widthHeight[0];

                    widthHeight[1] = height >> (i + 1);
                    widthHeight[1] = widthHeight[1] == 0 ? 1 : widthHeight[1];



                    int UOdd = (widthHeight[2] & 1) != 0 ? 1 : 0;
                    int VOdd = (widthHeight[3] & 1) != 0 ? 1 : 0;

                    int UOdd2 = (widthHeight[0] & 1) != 0 ? 1 : 0;
                    int VOdd2 = (widthHeight[1] & 1) != 0 ? 1 : 0;

                    RenderTargetIdentifier src;
                    RenderTargetIdentifier dst;
                    bool inputSRV = false;
                    if (i == 0)
                    {
                        cmd.EnableKeyword(m_HiZMipCompute, m_SRVSourceKW);
                        src = source.Identifier();
                        dst = destination.Identifier();
                        inputSRV = true;
                    }
                    else
                    {
                        //if (inputSRV == false)
                        //{
                        cmd.DisableKeyword(m_HiZMipCompute, m_SRVSourceKW);
                        //}
                        src = destination.Identifier();
                        dst = destination.Identifier();
                        inputSRV = false;
                    }


                    if (UOdd == 1 || VOdd == 1)
                    {
                        data2[1] = UOdd;
                        data2[2] = VOdd;
                        data2[3] = UOdd & VOdd;
                        DispatchOdd(ref cmd, src, dst, widthHeight, data2, i - 1, slices, inputSRV);
                        i++;
                    }
                    else if (UOdd2 == 1 || VOdd2 == 1)
                    {
                        //Debug.Log(string.Format("{0} x {1} is odd?", widthHeight[0], widthHeight[1]));
                        DispatchEvenSingle(ref cmd, src, dst, widthHeight, data2, i - 1, slices, inputSRV);
                        i++;
                    }
                    else
                    {
                        int processLevels = Mathf.Min(mipLevels - i - 1, 2);
                        data2[0] = processLevels;
                        DispatchEvenMultiLevel(ref cmd, src, dst, widthHeight, data2, i - 1, slices, inputSRV);
                        i += 2;
                    }
                
                } while (i <= highestMip);

                /* Old method for passing mip dimension info to shaders, 
                 * Precalculates the ratios of each mip to mip 0. This isn't
                 * ideal as it takes up way too many registers. Better to just
                 * calculate mip 0 dimension / exp2(mipLevel)
                Vector4[] mipDims = new Vector4[15];
                float mip0Width = (float)(width >> 1);
                float mip0Height = (float)(height >> 1);
                for (int j = 0; j < mipLevels; j++)
                {
                    mipDims[j] = new Vector4();
                    mipDims[j].x = (float)Mathf.Max(width >> j+1, 1);
                    mipDims[j].y = (float)Mathf.Max(height >> j+1, 1);
                    mipDims[j].z = mip0Width / mipDims[j].x;
                    mipDims[j].w = mip0Height / mipDims[j].y;
                }
                */
                //float mipNum = BitConverter.Int32BitsToSingle(highestMip);
                Vector4 dim = new Vector4();

                dim.x = (float)(width >> 1);
                dim.y = (float)(height >> 1);
                dim.z = 1.0f / dim.x;
                dim.w = 1.0f / dim.y;
                SLZGlobals.instance.SetHiZGlobal(highestMip, dim);
                //Debug.Log(string.Format("Mip dim: {0} {1} {2} {3} {4}", mipDims[0], mipDims[2], mipDims[4], mipDims[6], mipDims[8]));
            }
            //Debug.Log("Last Mip: " + i);
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        void DispatchEvenSingle(ref CommandBuffer cmd, RenderTargetIdentifier source1, RenderTargetIdentifier dest1,
            int[] widthHeight, int[] data2, int currMipLevel, int slices, bool inputSRV)
        {
            int kernel = 0;
            cmd.SetComputeIntParams(m_HiZMipCompute, computeParamID, widthHeight);
            //cmd.SetComputeIntParams(m_HiZMipCompute, computeParam2ID, data2);
            if (inputSRV)
            {
                cmd.SetComputeTextureParam(m_HiZMipCompute, kernel, computeMipSourceID, source1);
            }
            else
            {
                cmd.SetComputeTextureParam(m_HiZMipCompute, kernel, computeMipSourceID, source1, currMipLevel);
            }
            cmd.SetComputeTextureParam(m_HiZMipCompute, kernel, computeMipDestID, dest1, currMipLevel + 1);
            cmd.DispatchCompute(m_HiZMipCompute, kernel, Mathf.CeilToInt(((float)widthHeight[0]) / 8.0f), Mathf.CeilToInt(((float)widthHeight[1]) / 8.0f), slices);
        }
        void DispatchEvenMultiLevel(ref CommandBuffer cmd, RenderTargetIdentifier source1, RenderTargetIdentifier dest1,
            int[] widthHeight, int[] data2, int currMipLevel, int slices, bool inputSRV)
        {
            cmd.SetComputeIntParams(m_HiZMipCompute, computeParamID, widthHeight);
            cmd.SetComputeIntParams(m_HiZMipCompute, computeParam2ID, data2);
            if (inputSRV)
            {
                cmd.SetComputeTextureParam(m_HiZMipCompute, 1, computeMipSourceID, source1);
            }
            else
            {
                cmd.SetComputeTextureParam(m_HiZMipCompute, 1, computeMipSourceID, source1, currMipLevel);
            }
            cmd.SetComputeTextureParam(m_HiZMipCompute, 1, computeMipDestID, dest1, currMipLevel + 1);
            cmd.SetComputeTextureParam(m_HiZMipCompute, 1, computeMipDest2ID, dest1, data2[0] > 1 ? currMipLevel + 2 : currMipLevel + 1);
            cmd.DispatchCompute(m_HiZMipCompute, 1, Mathf.CeilToInt(((float)widthHeight[0]) / 8.0f), Mathf.CeilToInt(((float)widthHeight[1]) / 8.0f), slices);
        }

        void DispatchOdd(ref CommandBuffer cmd, RenderTargetIdentifier source1, RenderTargetIdentifier dest1,
            int[] widthHeight, int[] data2, int currMipLevel, int slices, bool inputSRV)
        {
            cmd.SetComputeIntParams(m_HiZMipCompute, computeParamID, widthHeight);
            cmd.SetComputeIntParams(m_HiZMipCompute, computeParam2ID, data2);
            if (inputSRV)
            {
                cmd.SetComputeTextureParam(m_HiZMipCompute, 2, computeMipSourceID, source1);
            }
            else
            {
                cmd.SetComputeTextureParam(m_HiZMipCompute, 2, computeMipSourceID, source1, currMipLevel);
            }
            cmd.SetComputeTextureParam(m_HiZMipCompute, 2, computeMipDestID, dest1, currMipLevel + 1);
            cmd.DispatchCompute(m_HiZMipCompute, 2, Mathf.CeilToInt(((float)widthHeight[0]) / 8.0f), Mathf.CeilToInt(((float)widthHeight[1]) / 8.0f), slices);
        }

        /// <inheritdoc/>
        public override void OnCameraCleanup(CommandBuffer cmd)
        {
            if (cmd == null)
                throw new ArgumentNullException("cmd");

            if (this.AllocateRT)
                cmd.ReleaseTemporaryRT(destination.id);
            destination = RenderTargetHandle.CameraTarget;
        }
    }
}
