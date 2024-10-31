using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering.RenderGraphModule;
using UnityEngine.Rendering;

namespace UnityEngine.Rendering.Universal.SLZVolumetrics
{

    public class VolumetricsRenderFeature : ScriptableRendererFeature
    {
        public const string resultTextureName = "_VolumetricResult";
        public const string shaderCBName = "VolumetricsCB";
        public const string volumetricKWName = "_VOLUMETRICS_ENABLED";

        [Serializable]
        public class VolumetricsRenderFeatureSettings
        {
            [SerializeField] internal ComputeShader FroxelFogCompute;
            [SerializeField] internal ComputeShader FroxelIntegrationCompute;
            [SerializeField] internal ComputeShader FroxelLocalFogCompute;
            [SerializeField] internal ComputeShader ClipmapCompute;
            [SerializeField] internal ComputeShader BlurCompute;
        }

        [SerializeField] internal VolumetricsRenderFeatureSettings settings;
        [SerializeField] public VolumetricData defaultVolumetricData;

        VolumetricsRenderPass pass;

        public override void Create()
        {
            pass = new VolumetricsRenderPass(settings);
            pass.renderPassEvent = RenderPassEvent.BeforeRenderingPrePasses;
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            renderer.EnqueuePass(pass);
        }

        public class VolumetricsRenderPass : ScriptableRenderPass
        {
            static int ID_VolumetricResult              = Shader.PropertyToID(resultTextureName);
            static int ID_Result                        = Shader.PropertyToID("Result");
            static int ID_InLightingTexture             = Shader.PropertyToID("InLightingTexture");
            static int ID_InTex                         = Shader.PropertyToID("InTex");
            static int ID_LightProjectionTextureArray   = Shader.PropertyToID("LightProjectionTextureArray");
            static int ID_VolumetricClipmapTexture      = Shader.PropertyToID("_VolumetricClipmapTexture");
            static int ID_VolumetricClipmapTexture2     = Shader.PropertyToID("_VolumetricClipmapTexture2");
            static int ID_PreResult                     = Shader.PropertyToID("PreResult");
            static int ID_VolumeMap                     = Shader.PropertyToID("VolumeMap");
            static int ID_PreviousFrameLighting         = Shader.PropertyToID("PreviousFrameLighting");
            static int ID_HistoryBuffer                 = Shader.PropertyToID("HistoryBuffer");
            static int ID_LeftEyeMatrix                 = Shader.PropertyToID("LeftEyeMatrix");
            static int ID_RightEyeMatrix                = Shader.PropertyToID("RightEyeMatrix");
            static int ID_ClipmapScale0                 = Shader.PropertyToID("ClipmapScale");
            static int ID_ClipmapScale1                 = Shader.PropertyToID("_ClipmapScale");
            static int ID_ClipmapScale2                 = Shader.PropertyToID("_ClipmapScale2");
            static int ID_ClipmapWorldPosition          = Shader.PropertyToID("ClipmapWorldPosition");
            static int ID_VBufferUnitDepthTexelSpacing  = Shader.PropertyToID("_VBufferUnitDepthTexelSpacing");
            static int ID_VolZBufferParams              = Shader.PropertyToID("_VolZBufferParams");
            static int ID_GlobalExtinction              = Shader.PropertyToID("_GlobalExtinction");
            static int ID_StaticLightMultiplier         = Shader.PropertyToID("_StaticLightMultiplier");
            static int ID_GlobalScattering              = Shader.PropertyToID("_GlobalScattering");
            static int ID_VolumeWorldSize               = Shader.PropertyToID("VolumeWorldSize");
            static int ID_VolumeWorldPosition           = Shader.PropertyToID("VolumeWorldPosition");
            static int ID_media_sphere_buffer_length    = Shader.PropertyToID("media_sphere_buffer_length");
            static int ID_media_sphere_buffer           = Shader.PropertyToID("media_sphere_buffer");
            static int ID_PerFrameConstBuffer           = Shader.PropertyToID("PerFrameCB");
            static int ID_PreviousFrameMatrix           = Shader.PropertyToID("PreviousFrameMatrix");
            static int ID_ClipmapScale                  = Shader.PropertyToID("_ClipmapScale");
            static int ID_ClipmapTransform              = Shader.PropertyToID("_ClipmapPosition");

            private class PassData
            {
                VolumetricsRenderFeatureSettings rfSettings;

            }

            VolumetricsRenderFeatureSettings rfSettings;


            internal VolumetricsRenderPass(VolumetricsRenderFeatureSettings settings)
            {
                rfSettings = settings;
            }

            public override void Execute(ScriptableRenderContext ctx, ref RenderingData renderingData)
            {

            }
            private static void ExecutePass(ScriptableRenderContext context, PassData data, ref RenderingData renderingData, bool yFlip)
            {

            }

            internal override void RecordRenderGraph(RenderGraph renderGraph,
                ref RenderingData renderingData
                )
            { 
                
            }
        }
    }
}
