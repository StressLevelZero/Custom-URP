﻿using System;


namespace UnityEngine.Rendering.Universal
{


    [Serializable, VolumeComponentMenu("Atmospherics/Volumetrics")]
    public sealed class Volumetrics : VolumeComponent
    {
        //static readonly int m_MipFogParam = Shader.PropertyToID("_MipFogParameters");
        static readonly int m_GlobalExtinction = Shader.PropertyToID("_GlobalExtinction");
        static readonly int m_FogBaseHeight = Shader.PropertyToID("_FogBaseHeight");
        static readonly int m_FogMaxHeight = Shader.PropertyToID("_FogMaxHeight");
        static readonly int m_StaticLightMultiplier = Shader.PropertyToID("_StaticLightMultiplier");
        static readonly int m_VolumetricAlbedo = Shader.PropertyToID("_GlobalScattering");

        // Volumetric rendering scripts need to be aware that this script has
        // set the shader global variables so they don't overwrite them
        public static bool hasSetGlobals { get; private set; } 

        //static readonly int m_SkyTexture = Shader.PropertyToID("_SkyTexture");
        //static readonly int m_SkyMipCount = Shader.PropertyToID("_SkyMipCount");

        [Header("Fog Mipmap controls")]
        [Tooltip("How close the mipfog starts")]
        public MinFloatParameter mipFogNear = new MinFloatParameter( 0.0f , 0 );
        [Tooltip("Where the mipfog ends")]
        public MinFloatParameter mipFogFar = new MinFloatParameter( 1, 1 );
        [Tooltip("Max mip level.")]
        public ClampedFloatParameter mipFogMaxMip = new ClampedFloatParameter(1.0f, 0.0f, 1);
        public CubemapParameter SkyTexture = new CubemapParameter( null );


        [Space, Header("Voulmetric Controls")]

        [Tooltip("Controls the global fog Density.")]
        public MinFloatParameter FogViewDistance = new MinFloatParameter(50, 1f);
        [Tooltip("Height in world space where fog hits max density.")]
        public FloatParameter FogBaseHeight = new FloatParameter(0);
        [Tooltip("Height in world space where fog is minimum density.")]
        public FloatParameter FogMaxHeight = new FloatParameter(50);
        [Tooltip("Controls the global fog Density."),HideInInspector]
        public ClampedFloatParameter MaxRenderDistance = new ClampedFloatParameter(50, 1f, 3000f); //Disabled until hooked up
        [Tooltip("Baked static light multiplier.")]
        public MinFloatParameter GlobalStaticLightMultiplier = new MinFloatParameter(1f, .1f);
        public ColorParameter VolumetricAlbedo = new ColorParameter(Color.white,false);
        //       public bool IsActive() => intensity.value > 0f && (type.value != FilmGrainLookup.Custom || texture.value != null);
        //       public bool IsTileCompatible() => true;

        internal void PushFogShaderParameters()
        {
            hasSetGlobals = true;
            Shader.SetGlobalFloat(m_GlobalExtinction, VolumeRenderingUtils.ExtinctionFromMeanFreePath(FogViewDistance.value) ); //ExtinctionFromMeanFreePath
            Shader.SetGlobalFloat(m_StaticLightMultiplier, GlobalStaticLightMultiplier.value);
            Shader.SetGlobalFloat(m_FogBaseHeight, FogBaseHeight.value);
            Shader.SetGlobalFloat(m_FogMaxHeight, FogMaxHeight.value);
            Shader.SetGlobalVector(m_VolumetricAlbedo, VolumetricAlbedo.value);

            SkyManager.SetSkyMips(new Vector4(mipFogNear.value, mipFogFar.value, mipFogMaxMip.value, 0.0f));
          
            if (SkyTexture.overrideState)
            {
                // Disable the override if skytexture.value is null. For some reason, checking if a null texture is null causes a 0.15ms of Loading.IsObjectAvailable (when the actual rendering only takes 0.04ms!).
                // This doesn't seem to happen if the texture is non-null
                if (SkyTexture.value == null)
                {
                    SkyTexture.overrideState = false;
                }
                else
                {
                    SkyManager.SetSkyTexture(SkyTexture.value); // SkyTexture.value != null &&
                }
            }
            else SkyManager.CheckSky();
        }

        //private void OnValidate()
        //{
        //    PushFogShaderParameters();
        //}

    }
}
