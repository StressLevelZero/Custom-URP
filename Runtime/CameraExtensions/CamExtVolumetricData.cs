using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace UnityEngine.Rendering.Universal
{
    public class CamExtVolumetricData : CameraDataExtension, IDisposable
    {
        public static CamExtVolumetricData TryGet(CameraDataExtSet dataSet, int type)
        {
            CameraDataExtension extData = dataSet.GetExtension(type);
            if (extData == null) 
            {
                CamExtVolumetricData newRt = new CamExtVolumetricData(type);
                dataSet.AddExtension(newRt);
                return newRt;
            }
#if UNITY_EDITOR
            if (extData.GetType() != typeof(CamExtVolumetricData)) 
            {
                Debug.LogError("Per-Camera data extensions: Tried to get CamExtVolumetricData with type ID " + type + ", but found type " + extData.GetType().Name + " for that ID!");
                return null;
            }
#endif

            return extData as CamExtVolumetricData;
        }


        public VolumetricData volumetricData;
        public float reprojectionAmount = 0.95f;
        public float sliceDistributionUniformity = 0.5f;
        public VolumetricRendering.BlurType FroxelBlur = VolumetricRendering.BlurType.None;

        // Default values when there's no volume
        public Color albedo = Color.white;
        public float meanFreePath = 15.0f;
        public float StaticLightMultiplier = 1.0f;

        public CamExtVolumetricData(int type)
        {
            this.type = type;
            volumetricData = new VolumetricData();
        }

        public CamExtVolumetricData(CamDataExtType type)
        {
            this.type = (int)type;
            volumetricData = new VolumetricData();
        }

        public CamExtVolumetricData(int type, Camera cam)
        {
            this.type = type;
            VolumetricRendering volSettings = cam.GetComponent<VolumetricRendering>();
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
