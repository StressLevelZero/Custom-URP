using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace UnityEngine.Rendering.Universal
{
    public class CamExtVolumetricData : CameraDataExtension, IDisposable
    {
        const int thisType = (int)CamDataExtType.VOLUMETRICS;

        public VolumetricData volumetricData;
        public float reprojectionAmount = 0.95f;
        public float sliceDistributionUniformity = 0.5f;
        public VolumetricRendering.BlurType FroxelBlur = VolumetricRendering.BlurType.None;

        // Default values when there's no volume
        public Color albedo = Color.white;
        public float meanFreePath = 15.0f;
        public float StaticLightMultiplier = 1.0f;

        public Vector3 lastClipmapUpdatePos;

        RenderTexture ClipmapBufferA;
        RenderTexture ClipmapBufferB;
        RenderTexture ClipmapBufferC;

        RenderTexture FroxelBufferA;
        RenderTexture FroxelBufferB;

        public CamExtVolumetricData() { }
        public override void Construct(Camera cam)
        {
            base.SetCamera(cam);
            VolumetricRendering volSettings = cam.GetComponent<VolumetricRendering>();
            if (volSettings != null)
            {
                volumetricData = volSettings.volumetricData;
                reprojectionAmount = volSettings.reprojectionAmount;
                sliceDistributionUniformity = volSettings.SliceDistributionUniformity;
                FroxelBlur = volSettings.FroxelBlur;

            }
        }

        public CamExtVolumetricData(Camera cam) : base(cam)
        {
            Construct(cam);
        }

        public CamExtVolumetricData(Camera cam, VolumetricRendering volSettings) : base(cam)
        {
            if (volSettings != null)
            {
                volumetricData = volSettings.volumetricData;
                reprojectionAmount = volSettings.reprojectionAmount;
                sliceDistributionUniformity = volSettings.SliceDistributionUniformity;
                FroxelBlur = volSettings.FroxelBlur;
            }
        }

        public override void Dispose()
        {

        }
    }
}
