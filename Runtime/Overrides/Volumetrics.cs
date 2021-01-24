using System;


namespace UnityEngine.Rendering.Universal
{


    [Serializable, VolumeComponentMenu("Atmospherics/Volumetrics")]
    public sealed class Volumetrics : VolumeComponent
    {
        static readonly int m_MipFogParam = Shader.PropertyToID("_MipFogParameters");
        static readonly int m_GlobalExtinction = Shader.PropertyToID("_GlobalExtinction");
        static readonly int m_StaticLightMultiplier = Shader.PropertyToID("_StaticLightMultiplier");
        static readonly int m_VolumetricAlbedo = Shader.PropertyToID("_GlobalScattering");
        [Header("Fog Mipmap controls")]
        [Tooltip("How close the mipfog starts")]
        public MinFloatParameter mipFogNear = new MinFloatParameter( 0.0f , 0 );
        [Tooltip("Where the mipfog ends")]
        public MinFloatParameter mipFogFar = new MinFloatParameter( 1, 1 );
        [Tooltip("Max mip level.")]
        public ClampedFloatParameter mipFogMaxMip = new ClampedFloatParameter(1.0f, 0.0f, 1);

        [Space, Header("Voulmetric Controls")]
        [Tooltip("Controls the global fog Density.")]
        public MinFloatParameter GlobalStaticLightMultiplier = new MinFloatParameter(1f, .1f);
        [Tooltip("Controls the global fog Density.")]
        public MinFloatParameter FogViewDistance = new MinFloatParameter(50, 1f);
        [Tooltip("Controls the global fog Density."),HideInInspector]
        public ClampedFloatParameter MaxRenderDistance = new ClampedFloatParameter(50, 1f, 3000f); //Disabled until hooked up
        public ColorParameter VolumetricAlbedo = new ColorParameter(Color.white,false);

        //       public bool IsActive() => intensity.value > 0f && (type.value != FilmGrainLookup.Custom || texture.value != null);

        //       public bool IsTileCompatible() => true;

        internal void PushFogShaderParameters()
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
            Shader.SetGlobalFloat(m_GlobalExtinction, VolumeRenderingUtils.ExtinctionFromMeanFreePath(FogViewDistance.value) ); //ExtinctionFromMeanFreePath
            Shader.SetGlobalFloat(m_StaticLightMultiplier, GlobalStaticLightMultiplier.value);
            Shader.SetGlobalVector(m_VolumetricAlbedo, VolumetricAlbedo.value);
        }
    }

    //[Serializable]
    //public sealed class VolumetricLookupParameter : VolumeParameter<FilmGrainLookup> { public VolumetricLookupParameter(FilmGrainLookup value, bool overrideState = false) : base(value, overrideState) { } }
}
