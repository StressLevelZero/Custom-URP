using System;


namespace UnityEngine.Rendering.Universal
{
    [Serializable, VolumeComponentMenu("Atmospherics/Volumetrics")]
    public sealed class Volumetrics : VolumeComponent
    {
        [Space, Header("Volumetric Controls")]
        [Tooltip("Controls the global fog density.")]
        public MinFloatParameter FogViewDistance = new MinFloatParameter(50f, 1f);

        [Header("ADVANCED SETTINGS — Don't use for normal circumstances")]
        [Tooltip("Baked static light multiplier.")]
        [AdditionalProperty]
        public MinFloatParameter GlobalStaticLightMultiplier = new MinFloatParameter(1f, 0.1f);

        [Header("Temporal Panic Refresh")]
        [Tooltip("When enabled, detects large parameter jumps and forces a temporal refresh for that frame.")]
        [AdditionalProperty]
        public BoolParameter PanicRefreshEnabled = new BoolParameter(true);

        [Tooltip("If the normalized delta exceeds this value, _PanicRefresh is set to 1 for that frame.")]
        [AdditionalProperty]
        public ClampedFloatParameter PanicRefreshThreshold = new ClampedFloatParameter(0.35f, 0f, 2f);
    }
}

