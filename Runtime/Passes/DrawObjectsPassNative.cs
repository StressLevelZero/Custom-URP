using System;
using System.Collections.Generic;
using System.Net.Mail;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Experimental.Rendering.RenderGraphModule;
using UnityEngine.Profiling;
using Unity.Collections;

namespace UnityEngine.Rendering.Universal.Internal
{
   
    /// <summary>
    /// Draw  objects into the given color and depth target
    ///
    /// You can use this pass to render objects that have a material and/or shader
    /// with the pass names UniversalForward or SRPDefaultUnlit.
    /// </summary>
    public class DrawObjectsPassNative : ScriptableRenderPass
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

        Material m_CopySubpassInputMaterial;

        public bool canDrawSkybox;
        public bool overrideTargets = false;

        public RTHandle colorTarget;
        public RTHandle opaqueSubpassTarget;
        public RTHandle depthTarget;
        public int msaaSampleCount = 1;
        public RenderTextureDescriptor cameraTextureDescriptor;
        UniversalRenderer caller; // Keep a reference to the current running renderer so we can check if VRS is enabled on it during the configuration stage
        NativeArray<AttachmentDescriptor> attachmentDescriptors;
        /// <summary>
        /// Used to indicate if the active target of the pass is the back buffer
        /// </summary>
        public bool m_IsActiveTargetBackBuffer; // TODO: Remove this when we remove non-RG path

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
        static GlobalKeyword s_SubpassInput0Kw = GlobalKeyword.Create("_SUBPASS_INPUT_0");
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
        public DrawObjectsPassNative(string profilerTag, ShaderTagId[] shaderTagIds, bool opaque, RenderPassEvent evt, RenderQueueRange renderQueueRange, LayerMask layerMask, StencilState stencilState, int stencilReference, Material subpassCopyMat)
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
            m_IsActiveTargetBackBuffer = false;
            m_CopySubpassInputMaterial = subpassCopyMat;
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
        public DrawObjectsPassNative(string profilerTag, bool opaque, RenderPassEvent evt, RenderQueueRange renderQueueRange, LayerMask layerMask, StencilState stencilState, int stencilReference, Material subpassCopyMat)
            : this(profilerTag,
            new ShaderTagId[] { new ShaderTagId("SRPDefaultUnlit"), new ShaderTagId("UniversalForward"), new ShaderTagId("UniversalForwardOnly") },
            opaque, evt, renderQueueRange, layerMask, stencilState, stencilReference, subpassCopyMat)
        { }

        internal DrawObjectsPassNative(URPProfileId profileId, bool opaque, RenderPassEvent evt, RenderQueueRange renderQueueRange, LayerMask layerMask, StencilState stencilState, int stencilReference, Material subpassCopyMat)
            : this(profileId.GetType().Name, opaque, evt, renderQueueRange, layerMask, stencilState, stencilReference, subpassCopyMat)
        {
            m_ProfilingSampler = ProfilingSampler.Get(profileId);
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            m_DrawSkybox = canDrawSkybox && renderingData.cameraData.camera.clearFlags == CameraClearFlags.Skybox;
            caller = renderingData.cameraData.renderer as UniversalRenderer;
            base.OnCameraSetup(cmd, ref renderingData);
        }

        // SLZ MODIFIED 
        public override void Configure(CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor)
        {
            this.cameraTextureDescriptor = cameraTextureDescriptor;

            if (true)
            {
                ConfigureTarget(colorTarget, depthTarget);
            }
            
            ConfigureInputAttachments(opaqueSubpassTarget, true);
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
            m_PassData.m_RenderingData = renderingData;
            m_PassData.m_RenderStateBlock = m_RenderStateBlock;
            m_PassData.m_FilteringSettings = m_FilteringSettings;
            m_PassData.m_ShaderTagIdList = m_ShaderTagIdList;
            m_PassData.m_ProfilingSampler = m_ProfilingSampler;
            m_PassData.drawSkybox = m_DrawSkybox;
            m_PassData.m_IsActiveTargetBackBuffer = m_IsActiveTargetBackBuffer;
            m_PassData.colorTarget = colorTarget;
            m_PassData.opaqueSubpassTarget = opaqueSubpassTarget;
            m_PassData.depthTarget = depthTarget;
            m_PassData.cameraTextureDescriptor = cameraTextureDescriptor;
            m_PassData.pass = this;
            m_PassData.copySubpassInputMat = m_CopySubpassInputMaterial;
            //m_PassData.m_UseMotionVectorData = useMotionVectorData;
            //m_PassData.m_UseMotionVectorData = renderingData.cameraData.enableSSR;
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


                RenderTextureDescriptor desc = data.cameraTextureDescriptor;//renderingData.cameraData.cameraTargetDescriptor;//data.colorTarget.rt == null ? data.cameraTextureDescriptor : data.colorTarget.rt.descriptor;
                if ((desc.width == 32 && desc.height == 32)) return;
                //Vector2Int resColor = data.colorTarget.rtHandleProperties.currentRenderTargetSize;
                //Vector2Int resDepth = data.depthTarget.rtHandleProperties.currentRenderTargetSize;
                //Vector2Int resOpaque = data.opaqueSubpassTarget.rtHandleProperties.currentRenderTargetSize;
                //Debug.Log($"Color: {resColor.x} x {resColor.y}, Depth: {resDepth.x}, {resDepth.y}, Opaque: {resOpaque.x}, {resOpaque.y}, ");
                int colorIdx = 0;
                int inputIdx = 1;
                int depthIdx = 2;

                NativeArray<AttachmentDescriptor> subpassAttachments = new NativeArray<AttachmentDescriptor>(depthIdx + 1, Allocator.Temp);
                subpassAttachments[colorIdx] = new AttachmentDescriptor()
                {
                    loadStoreTarget = new RenderTargetIdentifier(data.colorTarget.nameID, 0, CubemapFace.Unknown, -1),
                    loadAction = RenderBufferLoadAction.Clear,
                    storeAction = RenderBufferStoreAction.Store,
                    clearColor = renderingData.cameraData.backgroundColor,
                    clearStencil = 0u,
                    clearDepth = 1.0f,

                    graphicsFormat = desc.graphicsFormat,
                };

                /* Original opaque texture attachment
                subpassAttachments[1] = new AttachmentDescriptor()
                {
                    loadStoreTarget = new RenderTargetIdentifier(BuiltinRenderTextureType.None, 0, CubemapFace.Unknown, -1),
                    loadAction = RenderBufferLoadAction.DontCare,
                    storeAction = RenderBufferStoreAction.DontCare,
                    clearColor = renderingData.cameraData.backgroundColor,
                    clearStencil = 0u,
                    clearDepth = 1.0f,
                    graphicsFormat = desc.graphicsFormat,
                };
                */
                
                
                subpassAttachments[inputIdx] = new AttachmentDescriptor()
                {
                    loadStoreTarget = new RenderTargetIdentifier(BuiltinRenderTextureType.None, 0, CubemapFace.Unknown, -1),
                    loadAction = RenderBufferLoadAction.DontCare,
                    storeAction = RenderBufferStoreAction.DontCare,
                    clearColor = renderingData.cameraData.backgroundColor,
                    clearStencil = 0u,
                    clearDepth = 1.0f,
                    graphicsFormat = GraphicsFormat.R16_UNorm,
                };
                

                RenderTargetIdentifier depthID = data.depthTarget.nameID == new RenderTargetIdentifier(BuiltinRenderTextureType.CameraTarget) ? 
                    new RenderTargetIdentifier(BuiltinRenderTextureType.Depth,0,CubemapFace.Unknown,-1) : 
                    new RenderTargetIdentifier(data.depthTarget.nameID,0,CubemapFace.Unknown,-1);

                subpassAttachments[depthIdx] = new AttachmentDescriptor()
                {
                    loadStoreTarget = depthID,
                    loadAction = RenderBufferLoadAction.Clear,
                    storeAction = RenderBufferStoreAction.DontCare,
                    clearColor = renderingData.cameraData.backgroundColor,
                    clearStencil = 0u,
                    clearDepth = 1.0f,
                    graphicsFormat = desc.depthStencilFormat,
                };

                //Debug.Log($"{ data.depthTarget.nameID.ToString()}\n{data.colorTarget.nameID.ToString()}\n{data.opaqueSubpassTarget.nameID}");

                NativeArray<int> colorAttachment = new NativeArray<int>(1, Allocator.Temp);
                
                NativeArray<int> inputAttachment = new NativeArray<int>(1, Allocator.Temp);

                var activeDebugHandler = GetActiveDebugHandler(ref renderingData);

                Camera camera = renderingData.cameraData.camera;
                SortingCriteria sortFlagsOpaque = renderingData.cameraData.defaultOpaqueSortFlags;
                SortingCriteria sortFlagsTransparent = SortingCriteria.CommonTransparent;

                DrawingSettings drawSettingsOpaque = RenderingUtils.CreateDrawingSettings(data.m_ShaderTagIdList, ref renderingData, sortFlagsOpaque);
                DrawingSettings drawSettingsTransparent = RenderingUtils.CreateDrawingSettings(data.m_ShaderTagIdList, ref renderingData, sortFlagsTransparent);

                // Global render pass data containing various settings.
                // x,y,z are currently unused
                // w is used for knowing whether the object is opaque(1) or alpha blended(0)
                Vector4 drawObjectPassDataOpaque = new Vector4(0.0f, 0.0f, 0.0f, 1.0f);
                Vector4 drawObjectPassDataTransparent = new Vector4(0.0f, 0.0f, 0.0f, 0.0f);


                var filterSettingsOpaque = new FilteringSettings(RenderQueueRange.opaque, data.m_FilteringSettings.layerMask);
                var filterSettingsTransparent = new FilteringSettings(RenderQueueRange.transparent, data.m_FilteringSettings.layerMask);

#if UNITY_EDITOR
                // When rendering the preview camera, we want the layer mask to be forced to Everything
                if (renderingData.cameraData.isPreviewCamera)
                {
                    filterSettingsOpaque.layerMask = -1;
                    filterSettingsTransparent.layerMask = -1;
                }
#endif
                //Vector2Int res = data.colorTarget.rtHandleProperties.currentRenderTargetSize;
               
                //Debug.Log($"Renderpass dimensions {desc.width}, {desc.height}, {desc.volumeDepth}");
                context.BeginRenderPass(desc.width, desc.height, desc.volumeDepth, Math.Max(desc.msaaSamples, 1), subpassAttachments, depthIdx);
                subpassAttachments.Dispose();

                colorAttachment[0] = colorIdx; // originally 1, wrote to a separate attachment that would be copied to the backbuffer at the beginning of the transparent pass
                {
                    context.BeginSubPass(colorAttachment);
                   // cmd.ClearRenderTarget(true, true, Color.black);
#if ENABLE_VR && ENABLE_XR_MODULE
                    if (data.m_RenderingData.cameraData.xr.enabled && data.m_IsActiveTargetBackBuffer)
                    {
                        cmd.SetViewport(data.m_RenderingData.cameraData.xr.GetViewport());
                    }
#endif
                   
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

                    cmd.SetGlobalVector(s_DrawObjectPassDataPropID, drawObjectPassDataOpaque);

                    // TODO RENDERGRAPH: do this as a separate pass, so no need of calling OnExecute here...
                    data.pass.OnExecute(cmd);

                    context.ExecuteCommandBuffer(cmd);
                    cmd.Clear();
#if !DEBUG_NO_SHADERS
                    if (activeDebugHandler != null)
                    {
                        activeDebugHandler.DrawWithDebugRenderState(context, cmd, ref renderingData, ref drawSettingsOpaque, ref filterSettingsOpaque, ref data.m_RenderStateBlock,
                            (ScriptableRenderContext ctx, ref RenderingData data, ref DrawingSettings ds, ref FilteringSettings fs, ref RenderStateBlock rsb) =>
                            {
                                ctx.DrawRenderers(data.cullResults, ref ds, ref fs, ref rsb);
                            });
                    }
                    else
                    {
                        context.DrawRenderers(renderingData.cullResults, ref drawSettingsOpaque, ref filterSettingsOpaque, ref data.m_RenderStateBlock);

                        // Render objects that did not match any shader pass with error shader
                        RenderingUtils.RenderObjectsWithError(context, ref renderingData.cullResults, camera, filterSettingsOpaque, SortingCriteria.None);
                    }
#else
                    drawSettings.overrideMaterial = defaultMat;
                    context.DrawRenderers(renderingData.cullResults, ref drawSettingsOpaque, ref filterSettingsOpaque);
#endif
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
                                sunDir = new Vector4(0, 0, -1, 0);
                                lightColor = Color.black;
                            }
                            skybox.SetVector(s_WorldSpaceLightPos0, sunDir);
                            skybox.SetVector(s_LightColor0, lightColor);
                            cmd.EnableKeyword(s_DrawProcedural);
                            cmd.DrawProcedural(Matrix4x4.identity, skybox, 0, MeshTopology.Triangles, 3, 1);
                            cmd.DisableKeyword(s_DrawProcedural);
                            cmd.EnableKeyword(s_SubpassInput0Kw);
                        }
                    }

                    context.ExecuteCommandBuffer(cmd);
                    cmd.Clear();
                    context.EndSubPass();
                }


                // new - copy depth to input attachment 1
                // colorAttachment[0] = inputIdx;
                // inputAttachment[0] = colorIdx;
                // {
                //     context.BeginSubPass(colorAttachment, inputAttachment, true);
                //     //cmd.DrawProcedural(Matrix4x4.identity, data.copySubpassInputMat, 0, MeshTopology.Triangles, 3, 1);
                //     //context.ExecuteCommandBuffer(cmd);
                //     //cmd.Clear();
                //     context.EndSubPass();
                // }
                var fence = cmd.CreateGraphicsFence(GraphicsFenceType.AsyncQueueSynchronisation, SynchronisationStageFlags.AllGPUOperations);
                colorAttachment[0] = colorIdx;
                inputAttachment[0] = depthIdx;

                {
                    context.BeginSubPass(colorAttachment, inputAttachment, true);
                    cmd.EnableKeyword(s_SubpassInput0Kw);
                    colorAttachment.Dispose();
                    inputAttachment.Dispose();
                    /* original, copy opaque attachment to backbuffer
                    cmd.DrawProcedural(Matrix4x4.identity, data.copySubpassInputMat, 0, MeshTopology.Triangles, 3, 1);
                    context.ExecuteCommandBuffer(cmd);
                    cmd.Clear();
                    */

#if !DEBUG_NO_SHADERS
                    if (activeDebugHandler != null)
                    {
                        activeDebugHandler.DrawWithDebugRenderState(context, cmd, ref renderingData, ref drawSettingsTransparent, ref filterSettingsTransparent, ref data.m_RenderStateBlock,
                            (ScriptableRenderContext ctx, ref RenderingData data, ref DrawingSettings ds, ref FilteringSettings fs, ref RenderStateBlock rsb) =>
                            {
                                ctx.DrawRenderers(data.cullResults, ref ds, ref fs, ref rsb);
                            });
                    }
                    else
                    {
                        context.DrawRenderers(renderingData.cullResults, ref drawSettingsTransparent, ref filterSettingsTransparent, ref data.m_RenderStateBlock);

                        // Render objects that did not match any shader pass with error shader
                        RenderingUtils.RenderObjectsWithError(context, ref renderingData.cullResults, camera, filterSettingsTransparent, SortingCriteria.None);
                    }
#else
                drawSettings.overrideMaterial = defaultMat;
                context.DrawRenderers(renderingData.cullResults, ref drawSettingsTransparent, ref filterSettingsTransparent);
#endif


                    // Clean up
                    CoreUtils.SetKeyword(cmd, ShaderKeywordStrings.WriteRenderingLayers, false);
                    cmd.DisableKeyword(s_SubpassInput0Kw);

                    context.ExecuteCommandBuffer(cmd);
                    cmd.Clear();
                    context.EndSubPass();
                }
              
                context.EndRenderPass();


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
			internal bool m_IsActiveTargetBackBuffer;

            internal DrawObjectsPassNative pass;
            internal bool drawSkybox;
            // SLZ MODIFIED
            internal bool m_UseMotionVectorData;
            internal RTHandle opaqueSubpassTarget;
            internal RTHandle depthTarget;
            internal RTHandle colorTarget;
            internal RenderTextureDescriptor cameraTextureDescriptor;
            internal Material copySubpassInputMat;
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
                passData.m_IsActiveTargetBackBuffer = m_IsActiveTargetBackBuffer;

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
