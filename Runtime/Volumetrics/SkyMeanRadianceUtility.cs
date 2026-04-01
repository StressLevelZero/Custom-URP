using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace SLZ
{
    public static class SkyMeanRadianceUtility
    {
        static readonly int id_startIdx           = Shader.PropertyToID("StartRayIdx");
        static readonly int id__SkyTexture        = Shader.PropertyToID("_SkyTexture");
        static readonly int id_EnvLightSamples    = Shader.PropertyToID("EnvLightSamples");
        static readonly int id_PerDispatchRayCount= Shader.PropertyToID("PerDispatchRayCount");
        static readonly int id__OutColor          = Shader.PropertyToID("_OutColor");
        static readonly int id__GlobalSeed        = Shader.PropertyToID("_GlobalSeed");
        static readonly int id__MipLevel          = Shader.PropertyToID("_MipLevel");

        static readonly Vector4[] s_Zero = { Vector4.zero };

        public static int FindKernel(ComputeShader cs)
        {
            if (cs == null) throw new ArgumentNullException(nameof(cs));

            try { return cs.FindKernel("KSkyNoGeo_MeanRadiance"); }
            catch { }

            try { return cs.FindKernel("CSMain"); }
            catch { }

            throw new InvalidOperationException(
                $"Could not find 'KSkyNoGeo_MeanRadiance' or 'CSMain' on compute shader '{cs.name}'.");
        }

        public static void Dispatch(
            CommandBuffer cmd,
            ComputeShader cs,
            int kernel,
            Texture skyTexture,
            ComputeBuffer outColorBuffer,
            int environmentSampleCount,
            uint globalSeed = 0x51A7C3Du,
            float mipLevel = 0.0f,
            int startRayIndex = 0)
        {
            if (cmd == null) throw new ArgumentNullException(nameof(cmd));
            if (cs == null) throw new ArgumentNullException(nameof(cs));
            if (skyTexture == null) throw new ArgumentNullException(nameof(skyTexture));
            if (outColorBuffer == null) throw new ArgumentNullException(nameof(outColorBuffer));

            outColorBuffer.SetData(s_Zero);

            int samples = Mathf.Max(1, environmentSampleCount);

            cmd.SetComputeTextureParam(cs, kernel, id__SkyTexture, skyTexture);
            cmd.SetComputeIntParam(cs, id_EnvLightSamples, samples);
            cmd.SetComputeIntParam(cs, id_PerDispatchRayCount, samples);
            cmd.SetComputeIntParam(cs, id_startIdx, startRayIndex);
            cmd.SetComputeIntParam(cs, id__GlobalSeed, unchecked((int)globalSeed));
            cmd.SetComputeFloatParam(cs, id__MipLevel, mipLevel);
            cmd.SetComputeBufferParam(cs, kernel, id__OutColor, outColorBuffer);

            cmd.DispatchCompute(cs, kernel, 1, 1, 1);
        }
    }
}