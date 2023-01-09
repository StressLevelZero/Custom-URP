#if ENABLE_VR && ENABLE_XR_MODULE
using UnityEngine.Experimental.Rendering.RenderGraphModule;
using UnityEngine.Experimental.Rendering;

namespace UnityEngine.Rendering.Universal
{
    /// <summary>
    /// Draw the XR occlusion mesh into the current depth buffer when XR is enabled.
    /// </summary>
    public class XROcclusionMeshPass : ScriptableRenderPass
    {
        PassData m_PassData;
        bool isDepth;
        public XROcclusionMeshPass(RenderPassEvent evt, bool isDepth)
        {
            base.profilingSampler = new ProfilingSampler(nameof(XROcclusionMeshPass));
            renderPassEvent = evt;
            m_PassData = new PassData();
            base.profilingSampler = new ProfilingSampler("XR Occlusion Pass");
            this.isDepth = isDepth;
        }

        // SLZ MODIFIED

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            if (isDepth)
            {
                RenderTextureDescriptor desc = renderingData.cameraData.cameraTargetDescriptor;

                // When depth priming is in use the camera target should not be overridden so the Camera's MSAA depth attachment is used.
                if (renderingData.cameraData.renderer.useDepthPriming && (renderingData.cameraData.renderType == CameraRenderType.Base || renderingData.cameraData.clearDepth))
                {
                    ConfigureTarget(renderingData.cameraData.renderer.cameraDepthTargetHandle);

                    ConfigureClear(ClearFlag.Depth, Color.black);
                }
            }
            else
            {
                ConfigureClear(ClearFlag.None, Color.black);
            }
        }

        // END SLZ MODIFIED

        private static void ExecutePass(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            var cmd = renderingData.commandBuffer;

            if (renderingData.cameraData.xr.hasValidOcclusionMesh)
            {
                renderingData.cameraData.xr.RenderOcclusionMesh(cmd);
                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();
            }
        }

        /// <inheritdoc/>
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            ExecutePass(context, ref renderingData);
        }

        private class PassData
        {
            internal RenderingData renderingData;
            internal TextureHandle cameraDepthAttachment;
            // SLZ MODIFIED
            internal bool isDepth;
            // END SLZ MODIFIED
        }

        internal void Render(RenderGraph renderGraph, in TextureHandle cameraDepthAttachment, ref RenderingData renderingData)
        {
            using (var builder = renderGraph.AddRenderPass<PassData>("XR Occlusion Pass", out var passData, base.profilingSampler))
            {
                passData.renderingData = renderingData;
                passData.cameraDepthAttachment = builder.UseDepthBuffer(cameraDepthAttachment, DepthAccess.Write);

                // SLZ MODIFIED
                passData.isDepth = this.isDepth;
                // END SLZ MODIFIED

                //  TODO RENDERGRAPH: culling? force culling off for testing
                builder.AllowPassCulling(false);

                builder.SetRenderFunc((PassData data, RenderGraphContext context) =>
                {
                    ExecutePass(context.renderContext, ref data.renderingData);
                });

                return;
            }
        }
    }
}

#endif
