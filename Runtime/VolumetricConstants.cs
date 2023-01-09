using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace UnityEngine.Rendering.Universal
{
    public class VolumetricConstants
    {
        static VolumetricConstants s_Instance;
        public static VolumetricConstants instance
        {
            get
            {
                if (s_Instance == null)
                {
                    s_Instance = new VolumetricConstants();
                }
                return s_Instance;
            }
        }

        private GlobalKeyword VolumetricKW;
       // public static string volumetricKWName = "_VOLUMETRICS_ENABLED";
        int resultTexID = Shader.PropertyToID(VolumetricRendering.resultTextureName);
        int constantBufferID = Shader.PropertyToID(VolumetricRendering.shaderCBName);

        VolumetricConstants()
        {
            VolumetricKW = GlobalKeyword.Create(VolumetricRendering.volumetricKWName);
        }


        public void EnableVolumetrics(RenderTexture volumetricResult, ComputeBuffer shaderConstants)
        {
            Shader.SetKeyword(VolumetricKW, true);
            Shader.SetGlobalTexture(resultTexID, volumetricResult);
            Shader.SetGlobalConstantBuffer(constantBufferID, shaderConstants, 0, VolumetricRendering.ShaderConstantsSize);
        }

        public void DisableVolumetrics()
        {
            Shader.SetKeyword(VolumetricKW, false);
        }
    }
}
