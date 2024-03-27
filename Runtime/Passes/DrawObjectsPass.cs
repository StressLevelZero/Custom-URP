using System;
using System.Collections.Generic;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Experimental.Rendering.RenderGraphModule;
using UnityEngine.Profiling;

namespace UnityEngine.Rendering.Universal.Internal
{
    /// <summary>
    /// Extension of DrawObjectPass that also output Rendering Layers Texture as second render target.
    /// </summary>
    internal class DrawObjectsWithRenderingLayersPass : DrawObjectsPass
    {
        RTHandle[] m_ColorTargetIndentifiers;
        RTHandle m_DepthTargetIndentifiers;

        public DrawObjectsWithRenderingLayersPass(URPProfileId profilerTag, bool opaque, RenderPassEvent evt, RenderQueueRange renderQueueRange, LayerMask layerMask, StencilState stencilState, int stencilReference) :
            base(profilerTag, opaque, evt, renderQueueRange, layerMask, stencilState, stencilReference)
        {
            m_ColorTargetIndentifiers = new RTHandle[2];
        }

        public void Setup(RTHandle colorAttachment, RTHandle renderingLayersTexture, RTHandle depthAttachment)
        {
            if (colorAttachment == null)
                throw new ArgumentException("Color attachment can not be null", "colorAttachment");
            if (renderingLayersTexture == null)
                throw new ArgumentException("Rendering layers attachment can not be null", "renderingLayersTexture");
            if (depthAttachment == null)
                throw new ArgumentException("Depth attachment can not be null", "depthAttachment");

            m_ColorTargetIndentifiers[0] = colorAttachment;
            m_ColorTargetIndentifiers[1] = renderingLayersTexture;
            m_DepthTargetIndentifiers = depthAttachment;
        }

        public override void Configure(CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor)
        {
            ConfigureTarget(m_ColorTargetIndentifiers, m_DepthTargetIndentifiers);
        }

        protected override void OnExecute(CommandBuffer cmd)
        {
            CoreUtils.SetKeyword(cmd, ShaderKeywordStrings.WriteRenderingLayers, true);
        }
    }

    /// <summary>
    /// Draw  objects into the given color and depth target
    ///
    /// You can use this pass to render objects that have a material and/or shader
    /// with the pass names UniversalForward or SRPDefaultUnlit.
    /// </summary>
    public class DrawObjectsPass : ScriptableRenderPass
    {

#if DEBUG_NO_SHADERS
        static Material s_defaultMat;
        static Material defaultMat
        {
            get
            {
                if (s_defaultMat == null)
                {
                    Shader s = Shader.Find("Hidden/DUMMY_SHADER");
                    s_defaultMat = new Material(s);
                }
                return s_defaultMat;
            }
        }
#endif

        

        FilteringSettings m_FilteringSettings;
        RenderStateBlock m_RenderStateBlock;
        List<ShaderTagId> m_ShaderTagIdList = new List<ShaderTagId>();
        string m_ProfilerTag;
        ProfilingSampler m_ProfilingSampler;
        bool m_IsOpaque;
        bool m_DrawSkybox;

        public bool canDrawSkybox;

        public bool vkVRSHackOn = false;
        public RTHandle colorTarget;
        RTHandle[] vrsColorTargets = new RTHandle[2];
        public RTHandle depthTarget;
        UniversalRenderer caller; // Keep a reference to the current running renderer so we can check if VRS is enabled on it during the configuration stage

        /// <summary>
        /// Used to indicate whether transparent objects should receive shadows or not.
        /// </summary>
        public bool m_ShouldTransparentsReceiveShadows;

        PassData m_PassData;
        bool m_UseDepthPriming;

        static readonly int s_DrawObjectPassDataPropID = Shader.PropertyToID("_DrawObjectPassData");

        // SLZ MODIFIED
        public bool useMotionVectorData;
        static GlobalKeyword s_DrawProcedural = GlobalKeyword.Create("DRAW_SKY_PROCEDURAL");
        static readonly int s_WorldSpaceLightPos0 = Shader.PropertyToID("_WorldSpaceLightPosSun");
        static readonly int s_LightColor0 = Shader.PropertyToID("_LightColorSun");

        // END SLZ MODIFIED

        /// <summary>
        /// Creates a new <c>DrawObjectsPass</c> instance.
        /// </summary>
        /// <param name="profilerTag"></param>
        /// <param name="shaderTagIds"></param>
        /// <param name="opaque"></param>
        /// <param name="evt">The <c>RenderPassEvent</c> to use.</param>
        /// <param name="renderQueueRange"></param>
        /// <param name="layerMask"></param>
        /// <param name="stencilState"></param>
        /// <param name="stencilReference"></param>
        /// <seealso cref="ShaderTagId"/>
        /// <seealso cref="RenderPassEvent"/>
        /// <seealso cref="RenderQueueRange"/>
        /// <seealso cref="LayerMask"/>
        /// <seealso cref="StencilState"/>
        public DrawObjectsPass(string profilerTag, ShaderTagId[] shaderTagIds, bool opaque, RenderPassEvent evt, RenderQueueRange renderQueueRange, LayerMask layerMask, StencilState stencilState, int stencilReference)
        {
            base.profilingSampler = new ProfilingSampler(nameof(DrawObjectsPass));
            m_PassData = new PassData();
            m_ProfilerTag = profilerTag;
            m_ProfilingSampler = new ProfilingSampler(profilerTag);
            foreach (ShaderTagId sid in shaderTagIds)
                m_ShaderTagIdList.Add(sid);
            renderPassEvent = evt;
            m_FilteringSettings = new FilteringSettings(renderQueueRange, layerMask);
            m_RenderStateBlock = new RenderStateBlock(RenderStateMask.Nothing);
            m_IsOpaque = opaque;
            m_ShouldTransparentsReceiveShadows = false;

            if (stencilState.enabled)
            {
                m_RenderStateBlock.stencilReference = stencilReference;
                m_RenderStateBlock.mask = RenderStateMask.Stencil;
                m_RenderStateBlock.stencilState = stencilState;
            }
        }

        /// <summary>
        /// Creates a new <c>DrawObjectsPass</c> instance.
        /// </summary>
        /// <param name="profilerTag"></param>
        /// <param name="opaque"></param>
        /// <param name="evt"></param>
        /// <param name="renderQueueRange"></param>
        /// <param name="layerMask"></param>
        /// <param name="stencilState"></param>
        /// <param name="stencilReference"></param>
        /// <seealso cref="RenderPassEvent"/>
        /// <seealso cref="RenderQueueRange"/>
        /// <seealso cref="LayerMask"/>
        /// <seealso cref="StencilState"/>
        public DrawObjectsPass(string profilerTag, bool opaque, RenderPassEvent evt, RenderQueueRange renderQueueRange, LayerMask layerMask, StencilState stencilState, int stencilReference)
            : this(profilerTag,
            new ShaderTagId[] { new ShaderTagId("SRPDefaultUnlit"), new ShaderTagId("UniversalForward"), new ShaderTagId("UniversalForwardOnly") },
            opaque, evt, renderQueueRange, layerMask, stencilState, stencilReference)
        { }

        internal DrawObjectsPass(URPProfileId profileId, bool opaque, RenderPassEvent evt, RenderQueueRange renderQueueRange, LayerMask layerMask, StencilState stencilState, int stencilReference)
            : this(profileId.GetType().Name, opaque, evt, renderQueueRange, layerMask, stencilState, stencilReference)
        {
            m_ProfilingSampler = ProfilingSampler.Get(profileId);
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            m_DrawSkybox = canDrawSkybox && renderingData.cameraData.camera.clearFlags == CameraClearFlags.Skybox;
            caller = renderingData.cameraData.renderer as UniversalRenderer;
            base.OnCameraSetup(cmd, ref renderingData);
        }

        //SLZ MODIFIED // Added Configure, used to signal to the vulkan VRS plugin that this passes framebuffer needs a VRS attachment, and to prevent reuse of cached framebuffers without the VRS attachment
        public override void Configure(CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor)
        {

            //if (vkVRSHackOn && caller != null && caller.s_IsUsingVkVRS)
            //{
            //    // Duplicate the color target, this is the only way to actually tell from vkCreateFramebuffer that we need a VRS attachment. Also prevents caching and reuse of framebuffers from passes w/o VRS
            //    vrsColorTargets[0] = vrsColorTargets[1] = colorTarget;
            //    //vrsColorTargets[2] = dummyRTH;
            //    ConfigureTarget(vrsColorTargets, depthTarget);
            //
            //    //ConfigureColorStoreAction(RenderBufferStoreAction.DontCare, 1);
            //}
            //else
            {
                enableFoveatedRendering = false;
                // if VRS was enabled previously, then the target will remain until reset
                //ResetTarget();
            }
        }

        /// <inheritdoc/>
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            m_PassData.m_IsOpaque = m_IsOpaque;
            m_PassData.m_RenderStateBlock = m_RenderStateBlock;
            m_PassData.m_FilteringSettings = m_FilteringSettings;
            m_PassData.m_ShaderTagIdList = m_ShaderTagIdList;
            m_PassData.m_ProfilingSampler = m_ProfilingSampler;
            m_PassData.drawSkybox = m_DrawSkybox;
            m_PassData.pass = this;
            //m_PassData.m_UseMotionVectorData = useMotionVectorData;
            m_PassData.m_UseMotionVectorData = renderingData.cameraData.enableSSR;
            CameraSetup(renderingData.commandBuffer, m_PassData, ref renderingData);
            ExecutePass(context, m_PassData, ref renderingData, renderingData.cameraData.IsCameraProjectionMatrixFlipped());
        }

        private static void CameraSetup(CommandBuffer cmd, PassData data, ref RenderingData renderingData)
        {
            if (renderingData.cameraData.renderer.useDepthPriming && data.m_IsOpaque && (renderingData.cameraData.renderType == CameraRenderType.Base || renderingData.cameraData.clearDepth))
            {
                data.m_RenderStateBlock.depthState = new DepthState(false, CompareFunction.Equal);
                data.m_RenderStateBlock.mask |= RenderStateMask.Depth;
            }
            else if (data.m_RenderStateBlock.depthState.compareFunction == CompareFunction.Equal)
            {
                data.m_RenderStateBlock.depthState = new DepthState(true, CompareFunction.LessEqual);
                data.m_RenderStateBlock.mask |= RenderStateMask.Depth;
            }
        }

        private static void ExecutePass(ScriptableRenderContext context, PassData data, ref RenderingData renderingData, bool yFlip)
        {
            var cmd = renderingData.commandBuffer;
            using (new ProfilingScope(cmd, data.m_ProfilingSampler))
            {
                // Global render pass data containing various settings.
                // x,y,z are currently unused
                // w is used for knowing whether the object is opaque(1) or alpha blended(0)
                Vector4 drawObjectPassData = new Vector4(0.0f, 0.0f, 0.0f, (data.m_IsOpaque) ? 1.0f : 0.0f);
                cmd.SetGlobalVector(s_DrawObjectPassDataPropID, drawObjectPassData);
                // scaleBias.x = flipSign
                // scaleBias.y = scale
                // scaleBias.z = bias
                // scaleBias.w = unused
                float flipSign = yFlip ? -1.0f : 1.0f;
                Vector4 scaleBias = (flipSign < 0.0f)
                    ? new Vector4(flipSign, 1.0f, -1.0f, 1.0f)
                    : new Vector4(flipSign, 0.0f, 1.0f, 1.0f);
                cmd.SetGlobalVector(ShaderPropertyId.scaleBiasRt, scaleBias);

                // Set a value that can be used by shaders to identify when AlphaToMask functionality may be active
                // The material shader alpha clipping logic requires this value in order to function correctly in all cases.
                float alphaToMaskAvailable = ((renderingData.cameraData.cameraTargetDescriptor.msaaSamples > 1) && data.m_IsOpaque) ? 1.0f : 0.0f;
                cmd.SetGlobalFloat(ShaderPropertyId.alphaToMaskAvailable, alphaToMaskAvailable);

                // TODO RENDERGRAPH: do this as a separate pass, so no need of calling OnExecute here...
                data.pass.OnExecute(cmd);

                //context.ExecuteCommandBuffer(cmd);

                if (data.drawSkybox)
                {
                    Material skybox = RenderSettings.skybox;
                    if (skybox)
                    {
                        Light sun = RenderSettings.sun;
                        Vector4 sunDir;
                        Vector4 lightColor;
                        if (sun && sun.isActiveAndEnabled)
                        {
                            sunDir = -sun.transform.forward;
                            lightColor = (Vector4)sun.color * sun.intensity;
                        }
                        else 
                        { 
                            sunDir = new Vector4(0,0,-1,0);
                            lightColor = Color.black;
                        }
                        skybox.SetVector(s_WorldSpaceLightPos0, sunDir);
                        skybox.SetVector(s_LightColor0, lightColor);
                        cmd.EnableKeyword(s_DrawProcedural);
                        cmd.DrawProcedural(Matrix4x4.identity, skybox, 0, MeshTopology.Triangles, 3, 1);
                        cmd.DisableKeyword(s_DrawProcedural);
                    }
                }
                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();

                Camera camera = renderingData.cameraData.camera;
                var sortFlags = (data.m_IsOpaque) ? renderingData.cameraData.defaultOpaqueSortFlags : SortingCriteria.CommonTransparent;
                if (renderingData.cameraData.renderer.useDepthPriming && data.m_IsOpaque && (renderingData.cameraData.renderType == CameraRenderType.Base || renderingData.cameraData.clearDepth))
                    sortFlags = SortingCriteria.SortingLayer | SortingCriteria.RenderQueue | SortingCriteria.OptimizeStateChanges | SortingCriteria.CanvasOrder;

                var filterSettings = data.m_FilteringSettings;

#if UNITY_EDITOR
                // When rendering the preview camera, we want the layer mask to be forced to Everything
                if (renderingData.cameraData.isPreviewCamera)
                {
                    filterSettings.layerMask = -1;
                }
#endif

                DrawingSettings drawSettings = RenderingUtils.CreateDrawingSettings(data.m_ShaderTagIdList, ref renderingData, sortFlags);

                // SLZ MODIFIED
                if (data.m_UseMotionVectorData)
                {
					// Was used for sampling the color of the fragment from the previous frame. Not doing temporal averaging anymore
					// This breaks GPU instancing and causes batching issues
                    //drawSettings.perObjectData = drawSettings.perObjectData | PerObjectData.MotionVectors;
                }
                // END SLZ MODIFIED

                var activeDebugHandler = GetActiveDebugHandler(ref renderingData);
#if !DEBUG_NO_SHADERS
                if (activeDebugHandler != null)
                {
                    activeDebugHandler.DrawWithDebugRenderState(context, cmd, ref renderingData, ref drawSettings, ref filterSettings, ref data.m_RenderStateBlock,
                        (ScriptableRenderContext ctx, ref RenderingData data, ref DrawingSettings ds, ref FilteringSettings fs, ref RenderStateBlock rsb) =>
                        {
                            ctx.DrawRenderers(data.cullResults, ref ds, ref fs, ref rsb);
                        });
                }
                else
                {
                    context.DrawRenderers(renderingData.cullResults, ref drawSettings, ref filterSettings, ref data.m_RenderStateBlock);

                    // Render objects that did not match any shader pass with error shader
                    RenderingUtils.RenderObjectsWithError(context, ref renderingData.cullResults, camera, filterSettings, SortingCriteria.None);
                }
#else
                drawSettings.overrideMaterial = defaultMat;
                context.DrawRenderers(renderingData.cullResults, ref drawSettings, ref filterSettings);
#endif
                // Clean up
                CoreUtils.SetKeyword(cmd, ShaderKeywordStrings.WriteRenderingLayers, false);
                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();
            }
        }

        private class PassData
        {
            internal TextureHandle m_Albedo;
            internal TextureHandle m_Depth;

            internal RenderingData m_RenderingData;

            internal bool m_IsOpaque;
            internal RenderStateBlock m_RenderStateBlock;
            internal FilteringSettings m_FilteringSettings;
            internal List<ShaderTagId> m_ShaderTagIdList;
            internal ProfilingSampler m_ProfilingSampler;

            internal bool m_ShouldTransparentsReceiveShadows;

            internal DrawObjectsPass pass;
            internal bool drawSkybox;
            // SLZ MODIFIED
            internal bool m_UseMotionVectorData;
            // END SLZ MODIFIED
        }

        internal void Render(RenderGraph renderGraph, TextureHandle colorTarget, TextureHandle depthTarget, TextureHandle mainShadowsTexture, TextureHandle additionalShadowsTexture, ref RenderingData renderingData)
        {
            Camera camera = renderingData.cameraData.camera;

            using (var builder = renderGraph.AddRenderPass<PassData>("Draw Objects Pass", out var passData,
                m_ProfilingSampler))
            {
                passData.m_Albedo = builder.UseColorBuffer(colorTarget, 0);
                passData.m_Depth = builder.UseDepthBuffer(depthTarget, DepthAccess.Write);

                if (mainShadowsTexture.IsValid())
                    builder.ReadTexture(mainShadowsTexture);
                if (additionalShadowsTexture.IsValid())
                    builder.ReadTexture(additionalShadowsTexture);

                passData.m_RenderingData = renderingData;

                builder.AllowPassCulling(false);

                passData.m_IsOpaque = m_IsOpaque;
                passData.m_RenderStateBlock = m_RenderStateBlock;
                passData.m_FilteringSettings = m_FilteringSettings;
                passData.m_ShaderTagIdList = m_ShaderTagIdList;
                passData.m_ProfilingSampler = m_ProfilingSampler;

                passData.m_ShouldTransparentsReceiveShadows = m_ShouldTransparentsReceiveShadows;

                passData.pass = this;

                // SLZ MODIFIED
                passData.m_UseMotionVectorData = renderingData.cameraData.enableSSR;
                // END SLZ MODIFIED

                builder.SetRenderFunc((PassData data, RenderGraphContext context) =>
                {
                    ref var renderingData = ref data.m_RenderingData;

                    // TODO RENDERGRAPH figure out where to put XR proj flip logic so that it can be auto handled in render graph
#if ENABLE_VR && ENABLE_XR_MODULE
                    if (renderingData.cameraData.xr.enabled)
                    {
                        // SetRenderTarget might alter the internal device state(winding order).
                        // Non-stereo buffer is already updated internally when switching render target. We update stereo buffers here to keep the consistency.
                        bool renderIntoTexture = data.m_Albedo != renderingData.cameraData.xr.renderTarget;
                        renderingData.cameraData.PushBuiltinShaderConstantsXR(renderingData.commandBuffer, renderIntoTexture);
                        XRSystemUniversal.MarkShaderProperties(renderingData.commandBuffer, renderingData.cameraData.xrUniversal, renderIntoTexture);
                    }
#endif

                    // Currently we only need to call this additional pass when the user
                    // doesn't want transparent objects to receive shadows
                    if (!data.m_IsOpaque && !data.m_ShouldTransparentsReceiveShadows)
                        TransparentSettingsPass.ExecutePass(context.cmd, data.m_ShouldTransparentsReceiveShadows);

                    bool yFlip = renderingData.cameraData.IsRenderTargetProjectionMatrixFlipped(data.m_Albedo, data.m_Depth);
                    CameraSetup(context.cmd, data, ref renderingData);
                    ExecutePass(context.renderContext, data, ref renderingData, yFlip);
                });

            }
        }

        /// <summary>
        /// Called before ExecutePass draws the objects.
        /// </summary>
        /// <param name="cmd">The command buffer to use.</param>
        protected virtual void OnExecute(CommandBuffer cmd) { }
    }
}
