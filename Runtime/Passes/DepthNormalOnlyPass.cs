using System;
using System.Collections.Generic;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Experimental.Rendering.RenderGraphModule;

namespace UnityEngine.Rendering.Universal.Internal
{
    /// <summary>
    /// Render all objects that have a 'DepthNormals' and/or 'DepthNormalsOnly' pass into the given depth and normal buffers.
    /// </summary>
    public class DepthNormalOnlyPass : ScriptableRenderPass
    {
        public UniversalRenderer caller;
        internal List<ShaderTagId> shaderTagIds { get; set; }

        private RTHandle depthHandle { get; set; }
        private RTHandle m_DepthHandle;
        private RTHandle normalHandle { get; set; }
        private RTHandle renderingLayersHandle { get; set; }
        internal bool enableRenderingLayers { get; set; } = false;
        private FilteringSettings m_FilteringSettings;
        private PassData m_PassData;
        // Constants
        private static readonly List<ShaderTagId> k_DepthNormals = new List<ShaderTagId> { new ShaderTagId("DepthNormals"), new ShaderTagId("DepthNormalsOnly") };
        private static readonly RTHandle[] k_ColorAttachment1 = new RTHandle[1];
        private static readonly RTHandle[] k_ColorAttachment2 = new RTHandle[2];

        static int s_NormalsID = Shader.PropertyToID("_CameraNormalsTexture");
        //static int s_DepthID = Shader.PropertyToID("_CameraDepthTexture");

        // SLZ MODIFIED 

        // toggle to control if this pass clears the screen or not. We added an XR occlusion mesh pass that renders before this depth prepass, so don't clear if in VR
        private bool m_ClearTarget = true;

        // VK VRS HACK. Makes the pass's 1st and 2nd color attachment the same to tell VkCreateFrameBuffer that it needs to add the Shading rate texture to the framebuffer
        public bool vkVRSHackOn = false;
        // END SLZ MODIFIED

        /// <summary>
        /// Creates a new <c>DepthNormalOnlyPass</c> instance.
        /// </summary>
        /// <param name="evt">The <c>RenderPassEvent</c> to use.</param>
        /// <param name="renderQueueRange">The <c>RenderQueueRange</c> to use for creating filtering settings that control what objects get rendered.</param>
        /// <param name="layerMask">The layer mask to use for creating filtering settings that control what objects get rendered.</param>
        /// <seealso cref="RenderPassEvent"/>
        /// <seealso cref="RenderQueueRange"/>
        /// <seealso cref="LayerMask"/>
        public DepthNormalOnlyPass(RenderPassEvent evt, RenderQueueRange renderQueueRange, LayerMask layerMask)
        {
            base.profilingSampler = new ProfilingSampler(nameof(DepthNormalOnlyPass));
            m_PassData = new PassData();
            m_FilteringSettings = new FilteringSettings(renderQueueRange, layerMask);
            renderPassEvent = evt;
            useNativeRenderPass = false;
            this.shaderTagIds = k_DepthNormals;
        }

        /// <summary>
        /// Finds the format to use for the normals texture.
        /// </summary>
        /// <returns>The GraphicsFormat to use with the Normals texture.</returns>
        public static GraphicsFormat GetGraphicsFormat()
        {
            // SLZ MODIFIED // Use RG format texture if we can, normals packed into hemi-oct format instead of full RGB vector
            GraphicsFormat normalsFormat;
            if (RenderingUtils.SupportsGraphicsFormat(GraphicsFormat.R8G8_SNorm, FormatUsage.Render))
                normalsFormat = GraphicsFormat.R8G8_SNorm; // Preferred format
            else if (RenderingUtils.SupportsGraphicsFormat(GraphicsFormat.R16G16_SFloat, FormatUsage.Render))
                normalsFormat = GraphicsFormat.R16G16_SFloat; // fallback
            else if (RenderingUtils.SupportsGraphicsFormat(GraphicsFormat.R8G8B8A8_SNorm, FormatUsage.Render))
                normalsFormat = GraphicsFormat.R8G8B8A8_SNorm; // fallback
            else if (RenderingUtils.SupportsGraphicsFormat(GraphicsFormat.R32G32_SFloat, FormatUsage.Render))
                normalsFormat = GraphicsFormat.R32G32_SFloat; // fallback
            else
                normalsFormat = GraphicsFormat.R32G32B32A32_SFloat; // fallback
            return normalsFormat;
            // END SLZ MODIFIED
        }

        /// <summary>
        /// Configures the pass.
        /// </summary>
        /// <param name="depthHandle">The <c>RTHandle</c> used to render depth to.</param>
        /// <param name="normalHandle">The <c>RTHandle</c> used to render normals.</param>
        /// <seealso cref="RTHandle"/>
        public void Setup(RTHandle depthHandle, RTHandle normalHandle, bool clearTarget = true)
        {
            this.depthHandle = depthHandle;
            this.normalHandle = normalHandle;
            this.enableRenderingLayers = false;
            // SLZ MODIFIED // Set if pass will clear the render target's depth
            m_ClearTarget = clearTarget;
            // END SLZ MODIFIED
        }

        /// <summary>
        /// Configures the pass.
        /// </summary>
        /// <param name="depthHandle">The <c>RTHandle</c> used to render depth to.</param>
        /// <param name="normalHandle">The <c>RTHandle</c> used to render normals.</param>
        /// <param name="decalLayerHandle">The <c>RTHandle</c> used to render decals.</param>
        /// <seealso cref="RTHandle"/>
        public void Setup(RTHandle depthHandle, RTHandle normalHandle, RTHandle decalLayerHandle, bool clearTarget = true)
        {
            Setup(depthHandle, normalHandle, clearTarget);
            this.renderingLayersHandle = decalLayerHandle;
            this.enableRenderingLayers = true;
        }


        /// <inheritdoc/>
        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            caller = renderingData.cameraData.renderer as UniversalRenderer;

            if (renderingData.cameraData.renderer.useDepthPriming && (renderingData.cameraData.renderType == CameraRenderType.Base || renderingData.cameraData.clearDepth))
                m_DepthHandle = caller.cameraDepthTargetHandle;
            else
                m_DepthHandle = depthHandle;
        }

        /// <summary>
        /// SLZ MODIFIED. Configure the targets for this pass. This differs from OnCameraSetup in that it executes AFTER the passes have been sorted by queue, so we can rely on
        /// the VRS render feature having set the correct flags for VRS on the renderer.
        /// </summary>
        /// <param name="cmd"></param>
        /// <param name="cameraTextureDescriptor"></param>
        public override void Configure(CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor)
        {
            RTHandle[] colorHandles;
            if (this.enableRenderingLayers)
            {
                k_ColorAttachment2[0] = normalHandle;
                k_ColorAttachment2[1] = renderingLayersHandle;
                colorHandles = k_ColorAttachment2;
            }
            else
            {
                if (vkVRSHackOn && caller.s_IsUsingVkVRS)
                {
                    k_ColorAttachment2[0] = normalHandle;
                    k_ColorAttachment2[1] = normalHandle;
                    colorHandles = k_ColorAttachment2;
                }
                else
                {
                    k_ColorAttachment1[0] = normalHandle;
                    colorHandles = k_ColorAttachment1;
                }
            }
            cmd.SetGlobalTexture(s_NormalsID, normalHandle);
           // cmd.SetGlobalTexture(s_DepthID, m_DepthHandle);
            ConfigureTarget(colorHandles, m_DepthHandle);

            if (m_ClearTarget)
            {
                ConfigureClear(ClearFlag.All, Color.black);
            }
            else
            {
                ConfigureClear(ClearFlag.ColorStencil, Color.black);
            }

            base.Configure(cmd, cameraTextureDescriptor);
        }

        private static void ExecutePass(ScriptableRenderContext context, PassData passData, ref RenderingData renderingData)
        {
            var cmd = renderingData.commandBuffer;
            var shaderTagIds = passData.shaderTagIds;
            var filteringSettings = passData.filteringSettings;
            using (new ProfilingScope(cmd, ProfilingSampler.Get(URPProfileId.DepthNormalPrepass)))
            {
                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();

                // Enable Rendering Layers
                if (passData.enableRenderingLayers)
                {
                    CoreUtils.SetKeyword(cmd, ShaderKeywordStrings.WriteRenderingLayers, true);
                    context.ExecuteCommandBuffer(cmd);
                    cmd.Clear();
                }

                // Draw
                var sortFlags = renderingData.cameraData.defaultOpaqueSortFlags;
                var drawSettings = RenderingUtils.CreateDrawingSettings(shaderTagIds, ref renderingData, sortFlags);
                drawSettings.perObjectData = PerObjectData.None;
                context.DrawRenderers(renderingData.cullResults, ref drawSettings, ref filteringSettings);


                // Clean up
                if (passData.enableRenderingLayers)
                {
                    CoreUtils.SetKeyword(cmd, ShaderKeywordStrings.WriteRenderingLayers, false);

                }
                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();
            }
        }

        /// <inheritdoc/>
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            m_PassData.shaderTagIds = this.shaderTagIds;
            m_PassData.filteringSettings = m_FilteringSettings;
            m_PassData.enableRenderingLayers = enableRenderingLayers;
            ExecutePass(context, m_PassData, ref renderingData);
        }

        /// <inheritdoc/>
        public override void OnCameraCleanup(CommandBuffer cmd)
        {
            if (cmd == null)
            {
                throw new ArgumentNullException("cmd");
            }
            normalHandle = null;
            depthHandle = null;
            renderingLayersHandle = null;

            // This needs to be reset as the renderer might change this in runtime (UUM-36069)
            shaderTagIds = k_DepthNormals;
        }

        private class PassData
        {
            internal TextureHandle cameraDepthTexture;
            internal TextureHandle cameraNormalsTexture;
            internal RenderingData renderingData;
            internal List<ShaderTagId> shaderTagIds;
            internal FilteringSettings filteringSettings;
            internal bool enableRenderingLayers;
        }

        internal void Render(RenderGraph renderGraph, out TextureHandle cameraNormalsTexture, out TextureHandle cameraDepthTexture, ref RenderingData renderingData)
        {
            const GraphicsFormat k_DepthStencilFormat = GraphicsFormat.D32_SFloat_S8_UInt;
            const int k_DepthBufferBits = 32;

            using (var builder = renderGraph.AddRenderPass<PassData>("DepthNormals Prepass", out var passData, base.profilingSampler))
            {
                var depthDescriptor = renderingData.cameraData.cameraTargetDescriptor;
                depthDescriptor.graphicsFormat = GraphicsFormat.None;
                depthDescriptor.depthStencilFormat = k_DepthStencilFormat;
                //depthDescriptor.depthBufferBits = k_DepthBufferBits;
                depthDescriptor.msaaSamples = 1;// Depth-Only pass don't use MSAA
                cameraDepthTexture = UniversalRenderer.CreateRenderGraphTexture(renderGraph, depthDescriptor, "_CameraDepthTexture", true);

                // TODO RENDERGRAPH: Handle Deferred case, see _CameraNormalsTexture logic in UniversalRenderer.cs
                var normalDescriptor = renderingData.cameraData.cameraTargetDescriptor;
                normalDescriptor.depthBufferBits = 0;
                // Never have MSAA on this depth texture. When doing MSAA depth priming this is the texture that is resolved to and used for post-processing.
                normalDescriptor.msaaSamples = 1;// Depth-Only pass don't use MSAA
                                                    // Find compatible render-target format for storing normals.
                                                    // Shader code outputs normals in signed format to be compatible with deferred gbuffer layout.
                                                    // Deferred gbuffer format is signed so that normals can be blended for terrain geometry.
                                                    // TODO: deferred

                normalDescriptor.graphicsFormat = GetGraphicsFormat();
                cameraNormalsTexture = UniversalRenderer.CreateRenderGraphTexture(renderGraph, normalDescriptor, "_CameraNormalsTexture", true);

                passData.cameraNormalsTexture = builder.UseColorBuffer(cameraNormalsTexture, 0);
                passData.cameraDepthTexture = builder.UseDepthBuffer(cameraDepthTexture, DepthAccess.Write);
                passData.renderingData = renderingData;
                passData.shaderTagIds = this.shaderTagIds;
                passData.filteringSettings = m_FilteringSettings;
                passData.enableRenderingLayers = enableRenderingLayers;

                //  TODO RENDERGRAPH: culling? force culling off for testing
                builder.AllowPassCulling(false);

                builder.SetRenderFunc((PassData data, RenderGraphContext context) =>
                {
                    ExecutePass(context.renderContext, data, ref data.renderingData);
                });

                return;
            }
        }
    }
}
