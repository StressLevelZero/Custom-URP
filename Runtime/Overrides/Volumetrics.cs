using System;

namespace UnityEngine.Rendering.Universal
{
    [Serializable, VolumeComponentMenu("Atmospherics/Volumetrics")]
    public sealed class Volumetrics : VolumeComponent
    {
        static readonly int m_GlobalExtinction       = Shader.PropertyToID("_GlobalExtinction");
        // static readonly int m_FogBaseHeight          = Shader.PropertyToID("_FogBaseHeight");
        // static readonly int m_FogMaxHeight           = Shader.PropertyToID("_FogMaxHeight");
        static readonly int m_StaticLightMultiplier  = Shader.PropertyToID("_StaticLightMultiplier");

        static readonly int m_PanicRefresh           = Shader.PropertyToID("_PanicRefresh");
       // static readonly int m_PanicRefreshDelta      = Shader.PropertyToID("_PanicRefreshDelta"); // optional debug

        
        // ------------------------
        // Volumetric Controls
        // ------------------------
        [Space, Header("Volumetric Controls")]
        [Tooltip("Controls the global fog Density.")]
        public MinFloatParameter FogViewDistance = new MinFloatParameter(50, 1f);

        // [Tooltip("Height in world space where fog hits max density.")]
        // public FloatParameter FogBaseHeight = new FloatParameter(0);
        //
        // [Tooltip("Height in world space where fog is minimum density.")]
        // public FloatParameter FogMaxHeight = new FloatParameter(50);

        //[Tooltip("Controls the global fog Density."), HideInInspector]
        //public ClampedFloatParameter MaxRenderDistance = new ClampedFloatParameter(50, 1f, 3000f); // Disabled until hooked up

        [Header("ADVANCED SETTINGS — Don't use for normal circumstances")]
        [Tooltip("Baked static light multiplier.")]
        [AdditionalProperty] // hidden unless Advanced/Additional Properties are shown
        public MinFloatParameter GlobalStaticLightMultiplier = new MinFloatParameter(1f, .1f);


        // ------------------------
        // Temporal Panic Refresh
        // ------------------------
        [Header("Temporal Panic Refresh")]
        [Tooltip("When enabled, detects large parameter jumps and forces a temporal refresh for that frame.")]
        [AdditionalProperty]
        public BoolParameter PanicRefreshEnabled = new BoolParameter(true);

        [Tooltip("If the normalized delta exceeds this value, _PanicRefresh is set to 1 for that frame.")]
        [AdditionalProperty]
        public ClampedFloatParameter PanicRefreshThreshold = new ClampedFloatParameter(0.35f, 0.0f, 2.0f);

        // [Tooltip("Meters that count as ~1.0 delta for height params (base/max height). Bigger = less sensitive.")]
        // [AdditionalProperty]
        // public MinFloatParameter PanicHeightScaleMeters = new MinFloatParameter(25.0f, 0.001f);


        // ------------------------
        // Runtime snapshot state (per stack instance)
        // ------------------------
        [NonSerialized] private bool _panicPrevValid;
        [NonSerialized] private Snapshot _panicPrev;
        [NonSerialized] private int _panicLastEvalFrame = -1;
        [NonSerialized] private float _panicThisFrame;       // 0 or 1
        //[NonSerialized] private float _panicDeltaThisFrame;  // optional debug

        private struct Snapshot
        {
            public float extinction;
            public float staticLightMul;
            // public float baseHeight;
            // public float maxHeight;
        }

        private static float RelDiff(float a, float b)
        {
            float denom = Mathf.Max(Mathf.Abs(a), Mathf.Abs(b), 1e-4f);
            return Mathf.Abs(a - b) / denom;
        }

        private Snapshot CaptureSnapshot()
        {
            Snapshot s;

            s.extinction     = VolumeRenderingUtils.ExtinctionFromMeanFreePath(FogViewDistance.value);
            s.staticLightMul = GlobalStaticLightMultiplier.value;
            // s.baseHeight     = FogBaseHeight.value;
            // s.maxHeight      = FogMaxHeight.value;
            return s;
        }

        private float ComputePanicDelta(in Snapshot curr, in Snapshot prev)
        {
            float d = 0.0f;

            // Relative changes (scale-invariant)
            d = Mathf.Max(d, RelDiff(curr.extinction,     prev.extinction));
            d = Mathf.Max(d, RelDiff(curr.staticLightMul, prev.staticLightMul));

            // Heights in meters -> normalized by PanicHeightScaleMeters
            //float hScale = Mathf.Max(PanicHeightScaleMeters.value, 1e-4f);
            // d = Mathf.Max(d, Mathf.Abs(curr.baseHeight - prev.baseHeight) / hScale);
            // d = Mathf.Max(d, Mathf.Abs(curr.maxHeight  - prev.maxHeight)  / hScale);

            return d;
        }

        private void EvaluatePanicOncePerFrame()
        {
            int frame = Time.frameCount;
            if (_panicLastEvalFrame == frame)
                return;

            _panicLastEvalFrame = frame;
            _panicThisFrame = 0.0f;
            //_panicDeltaThisFrame = 0.0f;

            Snapshot curr = CaptureSnapshot();

            if (PanicRefreshEnabled.value && _panicPrevValid)
            {
                float delta = ComputePanicDelta(curr, _panicPrev);
               // _panicDeltaThisFrame = delta;
                if (delta >= PanicRefreshThreshold.value)
                    _panicThisFrame = 1.0f;
            }

            _panicPrev = curr;
            _panicPrevValid = true;
        }

        public void SetGlobalsOnCmdBuffer(CommandBuffer cmd)
        {
            EvaluatePanicOncePerFrame();
            cmd.SetGlobalFloat(m_PanicRefresh, _panicThisFrame);
           // cmd.SetGlobalFloat(m_PanicRefreshDelta, _panicDeltaThisFrame); // optional debug

            cmd.SetGlobalFloat(m_GlobalExtinction, VolumeRenderingUtils.ExtinctionFromMeanFreePath(FogViewDistance.value));
            cmd.SetGlobalFloat(m_StaticLightMultiplier, GlobalStaticLightMultiplier.value);
            // cmd.SetGlobalFloat(m_FogBaseHeight, FogBaseHeight.value);
            // cmd.SetGlobalFloat(m_FogMaxHeight, FogMaxHeight.value);

            // same as SkyManager.SetSkyMips (fixed defaults)
           // cmd.SetGlobalVector(SkyManager.ID_MipFogParam, k_DefaultMipFogParams);

            // No SkyTexture override anymore; ensure SkyManager has a valid sky bound.
           // SkyManager.CheckSky();
        }
    }
}
