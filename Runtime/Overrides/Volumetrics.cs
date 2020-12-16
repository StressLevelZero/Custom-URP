using System;


namespace UnityEngine.Rendering.Universal
{


    [Serializable, VolumeComponentMenu("Atmospherics/Volumetrics")]
    public sealed class Volumetrics : VolumeComponent
    {
        static readonly int m_MipFogParam = Shader.PropertyToID("_MipFogParameters");

        [Tooltip("How close the mipfog starts")]
        public MinFloatParameter mipFogNear = new MinFloatParameter( 0.0f , 0 );
        [Tooltip("Where the mipfog ends")]
        public MinFloatParameter mipFogFar = new MinFloatParameter( 1, 1 );
        [Tooltip("Max mip level.")]
        public ClampedFloatParameter mipFogMaxMip = new ClampedFloatParameter(1.0f, 0.0f, 1);

        [Tooltip("Controls froxel based volumetrics"), Header("Camera froxel Volumetrics")]
        public BoolParameter EnableFroxelVolumetrics = new BoolParameter(false);

        [Tooltip("Controls the global fog Density.")]
        public ClampedFloatParameter HomogeneousDensity = new ClampedFloatParameter(0f, 0.5f, 1f);

        [Tooltip("Controls the noisiness response curve based on scene luminance. Higher values mean less noise in light areas.")]
        public ClampedFloatParameter response = new ClampedFloatParameter(0.8f, 0f, 1f);

        [Tooltip("A tileable texture to use for the grain. The neutral value is 0.5 where no grain is applied.")]
        public NoInterpTextureParameter texture = new NoInterpTextureParameter(null);

        //       public bool IsActive() => intensity.value > 0f && (type.value != FilmGrainLookup.Custom || texture.value != null);

        //       public bool IsTileCompatible() => true;

        internal  void PushFogShaderParameters()
        {
            // TODO Handle user override
            //var fogSettings = hdCamera.volumeStack.GetComponent<Fog>();

            //if (!hdCamera.frameSettings.IsEnabled(FrameSettingsField.AtmosphericScattering) || !fogSettings.enabled.value)
            //{
            //    PushNeutralShaderParameters(cmd);
            //    return;
            //}

            //fogSettings.PushShaderParameters(hdCamera, cmd);

            //cmd.SetGlobalInt(HDShaderIDs._PBRFogEnabled, IsPBRFogEnabled(hdCamera) ? 1 : 0);

            Shader.SetGlobalVector(m_MipFogParam, new Vector4(mipFogNear.value, mipFogFar.value, mipFogMaxMip.value, 0.0f));
            Debug.Log("Pushed fog prams");


        }
    }

    //[Serializable]
    //public sealed class VolumetricLookupParameter : VolumeParameter<FilmGrainLookup> { public VolumetricLookupParameter(FilmGrainLookup value, bool overrideState = false) : base(value, overrideState) { } }
}
