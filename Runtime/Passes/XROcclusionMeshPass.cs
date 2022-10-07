#if ENABLE_VR && ENABLE_XR_MODULE

using System;
using UnityEngine.Experimental.Rendering;

namespace UnityEngine.Rendering.Universal
{
    /// <summary>
    /// Draw the XR occlusion mesh into the current depth buffer when XR is enabled.
    /// </summary>
    public class XROcclusionMeshPass : ScriptableRenderPass
    {
        bool isDepth;
        public XROcclusionMeshPass(RenderPassEvent evt)
        {
            base.profilingSampler = new ProfilingSampler(nameof(XROcclusionMeshPass));
            renderPassEvent = evt;
        }

        public void Setup(bool isDepth)
        {
            this.isDepth = isDepth;
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            if (isDepth)
            {
                RenderTextureDescriptor desc = renderingData.cameraData.cameraTargetDescriptor;

                // When depth priming is in use the camera target should not be overridden so the Camera's MSAA depth attachment is used.
                if (renderingData.cameraData.renderer.useDepthPriming && (renderingData.cameraData.renderType == CameraRenderType.Base || renderingData.cameraData.clearDepth))
                {
                    ConfigureTarget(renderingData.cameraData.renderer.cameraDepthTarget, desc.depthStencilFormat, desc.width, desc.height, 1, true);

                    ConfigureClear(ClearFlag.Depth, Color.black);
                }
            }
        }

        /// <inheritdoc/>
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (!renderingData.cameraData.xr.enabled)
                return;

            CommandBuffer cmd = CommandBufferPool.Get();

            renderingData.cameraData.xr.RenderOcclusionMesh(cmd);

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }
        public override void OnCameraCleanup(CommandBuffer cmd)
        {
            if (cmd == null)
                throw new ArgumentNullException("cmd");
        }
    }
}

#endif
