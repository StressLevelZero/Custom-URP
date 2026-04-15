// =============================================================================
// VolumetricClipmapManager (toroidal / direct-write rewrite)
// =============================================================================
//
// MIGRATION NOTES — REQUIRES MATCHING SHADER CHANGES
//
// This version replaces the old "snapshot the camera-centered region, fill
// scratch, copy to final" pipeline with a toroidal clipmap. Each level is a
// wrap-addressed window over an absolute voxel grid: voxel v is stored at
// texel ((v - origin) mod res + res) mod res. When the camera shifts the
// window by N voxels, only the N-thick slab(s) along the shift axes need to
// be regenerated; the rest of the texture is reused as-is.
//
// Density kernels (ClipMapDensity / ClipMapGen / ClipMapClear) MUST be
// updated to consume the new uniform set:
//
//   uniform float  _ClipmapVoxelSize;   // world units per voxel
//   uniform int    _ClipmapResolution;  // texture resolution (per axis)
//   uniform int3   _SlabVoxelBase;      // absolute voxel coord of thread (0,0,0)
//   uniform int3   _SlabVoxelSize;      // voxels covered by this dispatch
//
// Inside the kernel:
//
//   [numthreads(4,4,4)]
//   void ClipMapDensity(uint3 tid : SV_DispatchThreadID) {
//       int3 local = (int3)tid;
//       if (any(local >= _SlabVoxelSize)) return;
//
//       int3 absVoxel  = _SlabVoxelBase + local;
//       int  r         = _ClipmapResolution;
//       int3 wrapCoord = ((absVoxel % r) + r) % r;
//
//       float3 worldPos = ((float3)absVoxel + 0.5) * _ClipmapVoxelSize;
//
//       // ... evaluate density at worldPos ...
//       Result[wrapCoord] = density;  // (or accumulate as before)
//   }
//
// The OLD uniforms ClipmapScale, ClipmapWorldPosition, _RegionOffset,
// _RegionSize, PreResult are no longer set by C# and should be removed from
// the shader.
//
// SCATTER-SIDE CHANGES
//
// The scatter compute shader's per-level inputs change from
// (_ClipmapPositionN, _ClipmapScaleN) to (_ClipmapVoxelOriginN,
// _ClipmapVoxelSizeN). All four clipmap RTs are now created with
// TextureWrapMode.Repeat, so the trilinear sampler handles toroidal wrap-
// around at the seam for free. Sample like:
//
//   float3 relVoxel = worldPos / voxelSize - (float3)voxelOrigin;
//   float3 uvw      = (relVoxel + 0.5) / (float)resolution;
//   // sample with wrap=Repeat — DO NOT modulo manually, the seam needs the
//   // sampler's wrap to interpolate across it correctly.
//
// =============================================================================

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

public sealed class VolumetricClipmapManager : IDisposable
{
    public const int kClipLevels = 4;

    // Used when external systems change the clipmap
    private uint _seenRevision;

    // Final clipmaps (per level). Written directly — no staging buffer.
    private readonly RenderTexture[] _clipFinal = new RenderTexture[kClipLevels];

    // Per-level wrap-window state, in absolute voxel coordinates.
    private readonly Vector3Int[] _voxelOrigin       = new Vector3Int[kClipLevels]; // published origin
    private readonly Vector3Int[] _targetVoxelOrigin = new Vector3Int[kClipLevels]; // latest desired origin
    private readonly bool[]       _voxelOriginValid  = new bool[kClipLevels];       // false until first publish
    private readonly bool[]       _pendingLevel      = new bool[kClipLevels];
    private readonly int[]        _framesWaiting     = new int[kClipLevels];        // aging counter for fairness

    // Active incremental build (one level at a time).
    private BuildState _build;

    // Compute shader / kernels
    private ComputeShader _clipCS;
    private int _kGen, _kClear;

    // Per-CS kernel cache for media. Avoids per-build FindKernel(string).
    private readonly Dictionary<ComputeShader, int> _mediaKernelCache = new Dictionary<ComputeShader, int>();

    // Allocation signature
    private readonly int[] _levelRes = new int[kClipLevels];
    private bool _initialized;
    private bool _initialCleared;

    // Scratch array reused across every SetComputeIntParams call in this class.
    // Avoids the per-call allocation from the `params int[]` overload. Safe to
    // share because SetComputeIntParams copies the values into the command
    // buffer synchronously before returning.
    private readonly int[] _scratchInt3 = new int[3];
    private int[] ScratchFromVec3(Vector3Int vec3INT)
    {
        _scratchInt3[0] = vec3INT.x; 
        _scratchInt3[1] = vec3INT.y; 
        _scratchInt3[2] = vec3INT.z;
        return _scratchInt3;
    }

    private enum BuildStage
    {
        None = 0,
        ClearSlab,
        LocalMedia,
        BakedAreas,
        AdvanceSlab,
        GenerateMips,
    }

    private struct Slab
    {
        public Vector3Int min;   // absolute voxel coord of low corner
        public Vector3Int size;  // voxel extent
    }

    private struct BuildState
    {
        public bool active;
        public int level;
        public int res;
        public float voxelSize;
        public Vector3Int targetOrigin;     // O' snapshot at BeginBuild
        public Slab slab0, slab1, slab2;    // up to 3 slabs (X / Y / Z axis)
        public int slabCount;
        public int slabIdx;
        public BuildStage stage;
        public int localMediaIndex;
        public int bakedAreaIndex;
    }

    // ----- Property IDs -----
    private static readonly int ID_Result               = Shader.PropertyToID("Result");
    private static readonly int ID_VolumeMap            = Shader.PropertyToID("VolumeMap");
    private static readonly int ID_VolumeWorldSize      = Shader.PropertyToID("VolumeWorldSize");
    private static readonly int ID_VolumeWorldPosition  = Shader.PropertyToID("VolumeWorldPosition");

    // New per-dispatch slab uniforms (replace _RegionOffset/_RegionSize and the
    // ClipmapScale/ClipmapWorldPosition pair).
    private static readonly int ID_ClipmapVoxelSize     = Shader.PropertyToID("_ClipmapVoxelSize");
    private static readonly int ID_ClipmapResolution    = Shader.PropertyToID("_ClipmapResolution");
    private static readonly int ID_SlabVoxelBase        = Shader.PropertyToID("_SlabVoxelBase");
    private static readonly int ID_SlabVoxelSize        = Shader.PropertyToID("_SlabVoxelSize");
    private static readonly int ID_ClearColor           = Shader.PropertyToID("clearColor");

    private static readonly int VolumeDensity           = Shader.PropertyToID("VolumeDensity");
    private static readonly int VolumeFalloff           = Shader.PropertyToID("VolumeFalloff");

    private static readonly int ID_UseVolumeTexture     = Shader.PropertyToID("_UseVolumeTexture"); // 0/1
    private static readonly int ID_ShapeType            = Shader.PropertyToID("_ShapeType");        // 0=Box, 1=Sphere

    // Scatter-side bindings
    private static readonly int ID_VolumetricClipmapTexture0 = Shader.PropertyToID("_VolumetricClipmapTexture0");
    private static readonly int ID_VolumetricClipmapTexture1 = Shader.PropertyToID("_VolumetricClipmapTexture1");
    private static readonly int ID_VolumetricClipmapTexture2 = Shader.PropertyToID("_VolumetricClipmapTexture2");
    private static readonly int ID_VolumetricClipmapTexture3 = Shader.PropertyToID("_VolumetricClipmapTexture3");

    // NEW scatter wrap-window descriptors. The scatter shader uses these to
    // compute toroidal sample coordinates. Replaces _ClipmapScaleN /
    // _ClipmapPositionN from the old API.
    private static readonly int ID_ClipmapVoxelSize0   = Shader.PropertyToID("_ClipmapVoxelSize0");
    private static readonly int ID_ClipmapVoxelSize1   = Shader.PropertyToID("_ClipmapVoxelSize1");
    private static readonly int ID_ClipmapVoxelSize2   = Shader.PropertyToID("_ClipmapVoxelSize2");
    private static readonly int ID_ClipmapVoxelSize3   = Shader.PropertyToID("_ClipmapVoxelSize3");

    private static readonly int ID_ClipmapVoxelOrigin0 = Shader.PropertyToID("_ClipmapVoxelOrigin0");
    private static readonly int ID_ClipmapVoxelOrigin1 = Shader.PropertyToID("_ClipmapVoxelOrigin1");
    private static readonly int ID_ClipmapVoxelOrigin2 = Shader.PropertyToID("_ClipmapVoxelOrigin2");
    private static readonly int ID_ClipmapVoxelOrigin3 = Shader.PropertyToID("_ClipmapVoxelOrigin3");

    private static readonly int ID_ClipmapResolution0  = Shader.PropertyToID("_ClipmapResolution0");
    private static readonly int ID_ClipmapResolution1  = Shader.PropertyToID("_ClipmapResolution1");
    private static readonly int ID_ClipmapResolution2  = Shader.PropertyToID("_ClipmapResolution2");
    private static readonly int ID_ClipmapResolution3  = Shader.PropertyToID("_ClipmapResolution3");

    // ----- Profiling samplers -----
    private static readonly ProfilingSampler s_RecordTick    = new ProfilingSampler("Clipmap.RecordTick");
    private static readonly ProfilingSampler s_InitialClear  = new ProfilingSampler("Clipmap.InitialClear");
    private static readonly ProfilingSampler s_ClearSlab     = new ProfilingSampler("Clipmap.ClearSlab");
    private static readonly ProfilingSampler s_LocalMedia    = new ProfilingSampler("Clipmap.LocalMedia");
    private static readonly ProfilingSampler s_BakedAreas    = new ProfilingSampler("Clipmap.BakedAreas");
    private static readonly ProfilingSampler s_GenerateMips  = new ProfilingSampler("Clipmap.GenerateMips");
    private static readonly ProfilingSampler s_BindToScatter = new ProfilingSampler("Clipmap.BindToScatter");

    // --------------------------------------------------------------------
    // Public API
    // --------------------------------------------------------------------

    public RenderTexture GetClipmap(int level) => _clipFinal[level];

    public Vector3Int GetActiveVoxelOrigin(int level) => _voxelOrigin[level];

    public void Dispose()
    {
        for (int i = 0; i < kClipLevels; i++)
            ReleaseRT(ref _clipFinal[i]);

        _clipCS = null;
        _initialized = false;
        _initialCleared = false;
        _build = default;

        _mediaKernelCache.Clear();

        for (int i = 0; i < kClipLevels; i++)
        {
            _levelRes[i] = 0;
            _voxelOriginValid[i] = false;
            _pendingLevel[i] = false;
            _framesWaiting[i] = 0;
        }
    }

    public void EnsureInitialized(VolumetricData data, ComputeShader clipmapCompute, string namePrefix)
    {
        if (clipmapCompute == null) throw new ArgumentNullException(nameof(clipmapCompute));
        if (data == null) throw new ArgumentNullException(nameof(data));

        bool needsReinit = !_initialized || _clipCS != clipmapCompute;

        for (int i = 0; i < kClipLevels; i++)
        {
            int res = Mathf.Max(1, GetLevel(data, i).ClipMapResolution);
            if (_levelRes[i] != res) needsReinit = true;
        }

        if (!needsReinit) return;

        Dispose();

        _clipCS  = clipmapCompute;
        _kGen    = _clipCS.FindKernel("ClipMapGen");
        _kClear  = _clipCS.FindKernel("ClipMapClear");

        for (int i = 0; i < kClipLevels; i++)
        {
            int res = Mathf.Max(1, GetLevel(data, i).ClipMapResolution);
            _levelRes[i] = res;

            var desc = new RenderTextureDescriptor
            {
                enableRandomWrite = true,
                dimension = TextureDimension.Tex3D,
                width = res,
                height = res,
                volumeDepth = res,
                graphicsFormat = GraphicsFormat.R16G16B16A16_SFloat,
                msaaSamples = 1,
                useMipMap = true,
                autoGenerateMips = false
            };

            _clipFinal[i] = new RenderTexture(desc)
            {
                name = $"{namePrefix}_ClipmapL{i}",
                // Repeat lets the trilinear sampler handle toroidal wrap-around
                // at the seam for free. The scatter shader MUST sample without
                // a manual modulo for this to interpolate correctly.
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Trilinear
            };
            _clipFinal[i].hideFlags = HideFlags.DontSave;
            _clipFinal[i].Create();

            _voxelOrigin[i]       = Vector3Int.zero;
            _targetVoxelOrigin[i] = Vector3Int.zero;
            _voxelOriginValid[i]  = false;
            _pendingLevel[i]      = true;
            _framesWaiting[i]     = 0;
        }

        _build = default;
        _initialized = true;
        _initialCleared = false;
    }

    public void ForceFullClipmapUpdate()
    {
        AbortActiveBuild();

        for (int i = 0; i < kClipLevels; i++)
        {
            _voxelOriginValid[i] = false;
            _pendingLevel[i]     = true;
            _framesWaiting[i]    = 0;
        }
    }

    /// <summary>
    /// Records incremental clipmap work into the command buffer.
    /// The 4th argument is a build-step budget, not a level count.
    /// </summary>
    public void RecordTick(CommandBuffer cmd, Vector3 camPos, VolumetricData data, int maxBuildStepsPerFrame)
    {
        if (!ValidateResources())
        {
            _initialized = false;
            _initialCleared = false;
            _build = default;
            return;
        }

        using (new ProfilingScope(cmd, s_RecordTick))
        {
            if (!_initialCleared)
            {
                using (new ProfilingScope(cmd, s_InitialClear))
                {
                    for (int i = 0; i < kClipLevels; i++)
                        RecordFullClear(cmd, _clipFinal[i]);
                }

                _initialCleared = true;
            }

            // External content changed: abort and force a full rebuild on every level.
            if (_seenRevision != VolumetricRegisters.ClipmapRevision)
            {
                _seenRevision = VolumetricRegisters.ClipmapRevision;
                AbortActiveBuild();

                for (int i = 0; i < kClipLevels; i++)
                {
                    _voxelOriginValid[i] = false;
                    _pendingLevel[i]     = true;
                    _framesWaiting[i]    = 0;
                }
            }

            // Update target voxel origin per level. Voxel-quantized snap means
            // sub-voxel motion never triggers a build, so the old distance
            // hysteresis is largely subsumed — but we still honour
            // ClipmapResampleThresholdDistance as a "minimum shift in voxels"
            // to batch slow drifts into bigger, less frequent updates.
            for (int i = 0; i < kClipLevels; i++)
            {
                var L = GetLevel(data, i);
                int res = _levelRes[i];
                float voxelSize = L.ClipmapScale / Mathf.Max(res, 1);

                Vector3Int desired = ComputeDesiredVoxelOrigin(camPos, voxelSize, res);

                if (!_voxelOriginValid[i])
                {
                    _targetVoxelOrigin[i] = desired;
                    _pendingLevel[i]      = true;
                    continue;
                }

                Vector3Int delta = desired - _voxelOrigin[i];
                int maxAbsDelta = Mathf.Max(Mathf.Abs(delta.x), Mathf.Max(Mathf.Abs(delta.y), Mathf.Abs(delta.z)));
                int minShift = Mathf.Max(1, Mathf.RoundToInt(L.ClipmapResampleThresholdDistance / Mathf.Max(voxelSize, 1e-6f)));

                if (!_pendingLevel[i])
                {
                    if (maxAbsDelta >= minShift)
                    {
                        _targetVoxelOrigin[i] = desired;
                        _pendingLevel[i]      = true;
                        _framesWaiting[i]     = 0;
                    }
                }
                else
                {
                    // Already pending — keep target up to date so the in-flight or
                    // queued build always works toward the latest desired origin.
                    _targetVoxelOrigin[i] = desired;
                }
            }

            // Aging: increment waiting counters for pending levels not currently being built.
            for (int i = 0; i < kClipLevels; i++)
            {
                if (_pendingLevel[i] && (!_build.active || _build.level != i))
                    _framesWaiting[i]++;
            }

            int remainingSteps = Mathf.Max(1, maxBuildStepsPerFrame);

            while (remainingSteps > 0)
            {
                if (!_build.active)
                {
                    int li = SelectFairPendingLevel();
                    if (li < 0)
                        break;

                    BeginBuild(li, data);

                    // BeginBuild may instantly publish a no-op shift; if so the
                    // level is no longer pending and we just loop to pick the next.
                    if (!_build.active)
                        continue;
                }

                if (!RecordBuildStep(cmd, data))
                    break;

                remainingSteps--;
            }
        }
    }

    private int SelectFairPendingLevel()
    {
        // Aging-dominated score with a small bias toward closer (lower-index)
        // levels for tiebreaking. Score = framesWaiting * 2 + (kClipLevels - i).
        int best = -1;
        int bestScore = -1;

        for (int i = 0; i < kClipLevels; i++)
        {
            if (!_pendingLevel[i]) continue;

            int score = _framesWaiting[i] * 2 + (kClipLevels - i);
            if (score > bestScore)
            {
                best = i;
                bestScore = score;
            }
        }

        return best;
    }

    public void RecordBindToScatter(CommandBuffer cmd, ComputeShader scatterCS, int scatterKernel, VolumetricData data)
    {
        using (new ProfilingScope(cmd, s_BindToScatter))
        {
            for (int i = 0; i < kClipLevels; i++)
            {
                int res = _levelRes[i];
                float voxelSize = GetLevel(data, i).ClipmapScale / Mathf.Max(res, 1);
                Vector3Int origin = GetBoundVoxelOrigin(i);

                int sizeId, originId, resId;
                switch (i)
                {
                    case 0: sizeId = ID_ClipmapVoxelSize0; originId = ID_ClipmapVoxelOrigin0; resId = ID_ClipmapResolution0; break;
                    case 1: sizeId = ID_ClipmapVoxelSize1; originId = ID_ClipmapVoxelOrigin1; resId = ID_ClipmapResolution1; break;
                    case 2: sizeId = ID_ClipmapVoxelSize2; originId = ID_ClipmapVoxelOrigin2; resId = ID_ClipmapResolution2; break;
                    default: sizeId = ID_ClipmapVoxelSize3; originId = ID_ClipmapVoxelOrigin3; resId = ID_ClipmapResolution3; break;
                }

                cmd.SetComputeFloatParam(scatterCS, sizeId, voxelSize);
                cmd.SetComputeIntParam  (scatterCS, resId,  res);
                cmd.SetComputeIntParams (scatterCS, originId, ScratchFromVec3(origin));
            }

            cmd.SetComputeTextureParam(scatterCS, scatterKernel, ID_VolumetricClipmapTexture0, _clipFinal[0]);
            cmd.SetComputeTextureParam(scatterCS, scatterKernel, ID_VolumetricClipmapTexture1, _clipFinal[1]);
            cmd.SetComputeTextureParam(scatterCS, scatterKernel, ID_VolumetricClipmapTexture2, _clipFinal[2]);
            cmd.SetComputeTextureParam(scatterCS, scatterKernel, ID_VolumetricClipmapTexture3, _clipFinal[3]);
        }
    }

    private Vector3Int GetBoundVoxelOrigin(int level)
    {
        if (_voxelOriginValid[level])
            return _voxelOrigin[level];

        // No published data yet — fall back to the in-flight build's target if
        // we have one, otherwise to the latest desired target. The texture
        // contents at this point are the InitialClear color, which is fine.
        if (_build.active && _build.level == level)
            return _build.targetOrigin;

        return _targetVoxelOrigin[level];
    }

    // --------------------------------------------------------------------
    // Incremental build flow
    // --------------------------------------------------------------------

    private void BeginBuild(int level, VolumetricData data)
    {
        var L = GetLevel(data, level);
        int res = _levelRes[level];
        float voxelSize = L.ClipmapScale / Mathf.Max(res, 1);

        VolumetricRegisters.EnsureVolumetricAreasSorted();

        var state = new BuildState
        {
            active        = true,
            level         = level,
            res           = res,
            voxelSize     = voxelSize,
            targetOrigin  = _targetVoxelOrigin[level],
            stage         = BuildStage.ClearSlab,
            slabIdx       = 0,
            localMediaIndex = 0,
            bakedAreaIndex  = 0,
        };

        if (!_voxelOriginValid[level])
        {
            // First build: full window as a single slab covering everything.
            state.slab0 = new Slab
            {
                min  = state.targetOrigin,
                size = new Vector3Int(res, res, res),
            };
            state.slabCount = 1;
        }
        else
        {
            state.slabCount = ComputeSlabs(
                _voxelOrigin[level],
                state.targetOrigin,
                res,
                out state.slab0,
                out state.slab1,
                out state.slab2);
        }

        _build = state;

        // No-op shift (target == origin). Clear pending without producing any
        // GPU work.
        if (state.slabCount == 0)
        {
            PublishCompletedBuild(ref _build, data);
        }
    }

    private void AbortActiveBuild()
    {
        if (!_build.active)
            return;

        _pendingLevel[_build.level] = true;
        _framesWaiting[_build.level] = 0;
        _build = default;
    }

    private bool RecordBuildStep(CommandBuffer cmd, VolumetricData data)
    {
        if (!_build.active)
            return false;

        var state = _build;
        var L = GetLevel(data, state.level);

        for (;;)
        {
            switch (state.stage)
            {
                case BuildStage.ClearSlab:
                {
                    Slab slab = GetSlab(in state, state.slabIdx);

                    using (new ProfilingScope(cmd, s_ClearSlab))
                    {
                        DispatchClearSlab(cmd, _clipFinal[state.level], state.res, state.voxelSize, slab);
                    }

                    state.localMediaIndex = 0;
                    state.bakedAreaIndex  = 0;
                    state.stage = BuildStage.LocalMedia;
                    _build = state;
                    return true;
                }

                case BuildStage.LocalMedia:
                {
                    Slab slab = GetSlab(in state, state.slabIdx);

                    using (new ProfilingScope(cmd, s_LocalMedia))
                    {
                        while (state.localMediaIndex < VolumetricRegisters.VolumetricMediaEntities.Count)
                        {
                            var media = VolumetricRegisters.VolumetricMediaEntities[state.localMediaIndex++];
                            if (media == null) continue;

                            var mediaCS = media.computeShader;
                            if (mediaCS == null) continue;

                            int mediaKernel = GetMediaKernel(mediaCS);
                            if (mediaKernel < 0) continue;

                            if (!ComputeSlabMediaIntersection(
                                    slab,
                                    state.voxelSize,
                                    media.Corner,
                                    media.NormalizedScale,
                                    out var subBase,
                                    out var subSize))
                            {
                                continue;
                            }

                            int gx = Mathf.Max(Mathf.CeilToInt(subSize.x / 4f), 1);
                            int gy = Mathf.Max(Mathf.CeilToInt(subSize.y / 4f), 1);
                            int gz = Mathf.Max(Mathf.CeilToInt(subSize.z / 4f), 1);

                            cmd.SetComputeFloatParam(mediaCS, ID_ClipmapVoxelSize, state.voxelSize);
                            cmd.SetComputeIntParam  (mediaCS, ID_ClipmapResolution, state.res);
                            cmd.SetComputeIntParams (mediaCS, ID_SlabVoxelBase, ScratchFromVec3(subBase));
                            cmd.SetComputeIntParams (mediaCS, ID_SlabVoxelSize, ScratchFromVec3(subSize));

                            cmd.SetComputeTextureParam(mediaCS, mediaKernel, ID_Result, _clipFinal[state.level]);

                            cmd.SetComputeVectorParam(mediaCS, ID_VolumeWorldSize,     ToV4(media.NormalizedScale));
                            cmd.SetComputeVectorParam(mediaCS, ID_VolumeWorldPosition, ToV4(media.Corner));

                            Texture volTex = media.volumeTexture != null
                                ? (Texture)media.volumeTexture
                                : (Texture)CoreUtils.blackVolumeTexture;

                            cmd.SetComputeTextureParam(mediaCS, mediaKernel, ID_VolumeMap, volTex);
                            cmd.SetComputeIntParam(mediaCS, ID_UseVolumeTexture, media.volumeTexture != null ? 1 : 0);

                            int shapeType = (media.shapeType == LocalVolumetricFog.ShapeType.Sphere) ? 1 : 0;
                            cmd.SetComputeIntParam(mediaCS, ID_ShapeType, shapeType);

                            cmd.SetComputeFloatParam(mediaCS, VolumeDensity, media.LocalExtinction());
                            cmd.SetComputeFloatParam(mediaCS, VolumeFalloff, media.falloffDistance);

                            cmd.DispatchCompute(mediaCS, mediaKernel, gx, gy, gz);

                            _build = state;
                            return true;
                        }
                    }

                    state.stage = BuildStage.BakedAreas;
                    continue;
                }

                case BuildStage.BakedAreas:
                {
                    Slab slab = GetSlab(in state, state.slabIdx);

                    using (new ProfilingScope(cmd, s_BakedAreas))
                    {
                        while (state.bakedAreaIndex < VolumetricRegisters.volumetricAreas.Count)
                        {
                            var area = VolumetricRegisters.volumetricAreas[state.bakedAreaIndex++];
                            if (area == null || area.bakedTexture == null) continue;

                            if (!ComputeSlabMediaIntersection(
                                    slab,
                                    state.voxelSize,
                                    area.Corner,
                                    area.NormalizedScale,
                                    out var subBase,
                                    out var subSize))
                            {
                                continue;
                            }

                            int gx = Mathf.Max(Mathf.CeilToInt(subSize.x / 4f), 1);
                            int gy = Mathf.Max(Mathf.CeilToInt(subSize.y / 4f), 1);
                            int gz = Mathf.Max(Mathf.CeilToInt(subSize.z / 4f), 1);

                            cmd.SetComputeFloatParam(_clipCS, ID_ClipmapVoxelSize, state.voxelSize);
                            cmd.SetComputeIntParam  (_clipCS, ID_ClipmapResolution, state.res);
                            cmd.SetComputeIntParams (_clipCS, ID_SlabVoxelBase, ScratchFromVec3(subBase));
                            cmd.SetComputeIntParams (_clipCS, ID_SlabVoxelSize, ScratchFromVec3(subSize));

                            cmd.SetComputeTextureParam(_clipCS, _kGen, ID_Result, _clipFinal[state.level]);
                            cmd.SetComputeTextureParam(_clipCS, _kGen, ID_VolumeMap, area.bakedTexture);
                            cmd.SetComputeVectorParam (_clipCS, ID_VolumeWorldSize,     ToV4(area.NormalizedScale));
                            cmd.SetComputeVectorParam (_clipCS, ID_VolumeWorldPosition, ToV4(area.Corner));

                            cmd.DispatchCompute(_clipCS, _kGen, gx, gy, gz);

                            _build = state;
                            return true;
                        }
                    }

                    state.stage = BuildStage.AdvanceSlab;
                    continue;
                }

                case BuildStage.AdvanceSlab:
                {
                    state.slabIdx++;
                    if (state.slabIdx < state.slabCount)
                    {
                        state.localMediaIndex = 0;
                        state.bakedAreaIndex  = 0;
                        state.stage = BuildStage.ClearSlab;
                        continue; // fall through to next slab without consuming a step
                    }

                    state.stage = BuildStage.GenerateMips;
                    continue;
                }

                case BuildStage.GenerateMips:
                {
                    using (new ProfilingScope(cmd, s_GenerateMips))
                    {
                        // TODO: with toroidal addressing, full-volume mip gen
                        // is wasteful when only a small slab actually changed.
                        // A custom partial-mip pass that respects the wrap
                        // seam would be a meaningful win, especially for far
                        // levels with frequent small shifts.
                        var final = _clipFinal[state.level];
                        if (final != null && final.IsCreated() && final.useMipMap)
                            cmd.GenerateMips(final);
                    }

                    PublishCompletedBuild(ref state, data);
                    _build = state;
                    return true;
                }

                default:
                    _build = state;
                    return false;
            }
        }
    }

    private void PublishCompletedBuild(ref BuildState state, VolumetricData data)
    {
        int level = state.level;

        _voxelOrigin[level]      = state.targetOrigin;
        _voxelOriginValid[level] = true;

        // If the camera kept walking during the build, the latest target may
        // have moved past the just-published origin — re-pend so the next tick
        // schedules another shift.
        if (_targetVoxelOrigin[level] == state.targetOrigin)
        {
            _pendingLevel[level]  = false;
            _framesWaiting[level] = 0;
        }
        else
        {
            _pendingLevel[level]  = true;
            _framesWaiting[level] = 0;
        }

        state = default;
    }

    // --------------------------------------------------------------------
    // Slab math
    // --------------------------------------------------------------------

    /// <summary>
    /// Decompose the move from origin O to O' (in voxel coordinates) into up
    /// to three non-overlapping slabs covering the new voxels of the wrap
    /// window. Each new voxel is written exactly once across all slabs, which
    /// matters because the density kernels accumulate.
    /// </summary>
    private static int ComputeSlabs(Vector3Int O, Vector3Int Op, int res, out Slab s0, out Slab s1, out Slab s2)
    {
        s0 = default; s1 = default; s2 = default;

        Vector3Int delta = Op - O;
        int dx = delta.x, dy = delta.y, dz = delta.z;

        // No motion.
        if (dx == 0 && dy == 0 && dz == 0)
            return 0;

        // Shift exceeds window on any axis -> no overlap, full rebuild.
        if (Mathf.Abs(dx) >= res || Mathf.Abs(dy) >= res || Mathf.Abs(dz) >= res)
        {
            s0 = new Slab { min = Op, size = new Vector3Int(res, res, res) };
            return 1;
        }

        // Kept overlap range, in absolute voxel coordinates, per axis.
        int keepMinX = Mathf.Max(O.x, Op.x), keepMaxX = Mathf.Min(O.x + res, Op.x + res);
        int keepMinY = Mathf.Max(O.y, Op.y), keepMaxY = Mathf.Min(O.y + res, Op.y + res);
        int keepMinZ = Mathf.Max(O.z, Op.z), keepMaxZ = Mathf.Min(O.z + res, Op.z + res);

        int n = 0;

        // X-axis slab: full new Y range, full new Z range.
        if (dx != 0)
        {
            int xLo = (dx > 0) ? Op.x + res - dx : Op.x;
            int xHi = (dx > 0) ? Op.x + res      : Op.x - dx;

            var slab = new Slab
            {
                min  = new Vector3Int(xLo, Op.y, Op.z),
                size = new Vector3Int(xHi - xLo, res, res),
            };
            WriteSlab(slab, n, ref s0, ref s1, ref s2);
            n++;
        }

        // Y-axis slab: restricted on X to the kept overlap so it doesn't
        // double-write the X slab; full new Z range.
        if (dy != 0)
        {
            int yLo = (dy > 0) ? Op.y + res - dy : Op.y;
            int yHi = (dy > 0) ? Op.y + res      : Op.y - dy;

            int xSize = keepMaxX - keepMinX;
            int ySize = yHi - yLo;

            if (xSize > 0 && ySize > 0)
            {
                var slab = new Slab
                {
                    min  = new Vector3Int(keepMinX, yLo, Op.z),
                    size = new Vector3Int(xSize, ySize, res),
                };
                WriteSlab(slab, n, ref s0, ref s1, ref s2);
                n++;
            }
        }

        // Z-axis slab: restricted on both X and Y to the kept overlap.
        if (dz != 0)
        {
            int zLo = (dz > 0) ? Op.z + res - dz : Op.z;
            int zHi = (dz > 0) ? Op.z + res      : Op.z - dz;

            int xSize = keepMaxX - keepMinX;
            int ySize = keepMaxY - keepMinY;
            int zSize = zHi - zLo;

            if (xSize > 0 && ySize > 0 && zSize > 0)
            {
                var slab = new Slab
                {
                    min  = new Vector3Int(keepMinX, keepMinY, zLo),
                    size = new Vector3Int(xSize, ySize, zSize),
                };
                WriteSlab(slab, n, ref s0, ref s1, ref s2);
                n++;
            }
        }

        return n;
    }

    private static void WriteSlab(Slab slab, int idx, ref Slab s0, ref Slab s1, ref Slab s2)
    {
        if (idx == 0)      s0 = slab;
        else if (idx == 1) s1 = slab;
        else               s2 = slab;
    }

    private static Slab GetSlab(in BuildState state, int idx)
    {
        if (idx == 0) return state.slab0;
        if (idx == 1) return state.slab1;
        return state.slab2;
    }

    /// <summary>
    /// Intersect a slab (absolute voxel AABB) with a media volume (world-space
    /// AABB) and return the absolute-voxel sub-region to dispatch over.
    /// </summary>
    private static bool ComputeSlabMediaIntersection(
        Slab slab, float voxelSize,
        Vector3 mediaCorner, Vector3 mediaSize,
        out Vector3Int subBase, out Vector3Int subSize)
    {
        Vector3 slabMinW = (Vector3)slab.min * voxelSize;
        Vector3 slabMaxW = (Vector3)(slab.min + slab.size) * voxelSize;

        Vector3 mediaMinW = mediaCorner;
        Vector3 mediaMaxW = mediaCorner + mediaSize;

        Vector3 interMin = Vector3.Max(slabMinW, mediaMinW);
        Vector3 interMax = Vector3.Min(slabMaxW, mediaMaxW);

        if (interMin.x >= interMax.x || interMin.y >= interMax.y || interMin.z >= interMax.z)
        {
            subBase = default; subSize = default;
            return false;
        }

        float invVoxel = 1f / Mathf.Max(voxelSize, 1e-6f);

        int loX = Mathf.FloorToInt(interMin.x * invVoxel);
        int loY = Mathf.FloorToInt(interMin.y * invVoxel);
        int loZ = Mathf.FloorToInt(interMin.z * invVoxel);
        int hiX = Mathf.CeilToInt (interMax.x * invVoxel);
        int hiY = Mathf.CeilToInt (interMax.y * invVoxel);
        int hiZ = Mathf.CeilToInt (interMax.z * invVoxel);

        // Clamp to the slab's voxel range.
        int slabHiX = slab.min.x + slab.size.x;
        int slabHiY = slab.min.y + slab.size.y;
        int slabHiZ = slab.min.z + slab.size.z;

        loX = Mathf.Max(loX, slab.min.x); hiX = Mathf.Min(hiX, slabHiX);
        loY = Mathf.Max(loY, slab.min.y); hiY = Mathf.Min(hiY, slabHiY);
        loZ = Mathf.Max(loZ, slab.min.z); hiZ = Mathf.Min(hiZ, slabHiZ);

        if (hiX <= loX || hiY <= loY || hiZ <= loZ)
        {
            subBase = default; subSize = default;
            return false;
        }

        subBase = new Vector3Int(loX, loY, loZ);
        subSize = new Vector3Int(hiX - loX, hiY - loY, hiZ - loZ);
        return true;
    }

    private static Vector3Int ComputeDesiredVoxelOrigin(Vector3 camPos, float voxelSize, int res)
    {
        // Snap camera to voxel grid, then offset by half-res so the camera
        // sits at the center of the wrap window.
        float invVoxel = 1f / Mathf.Max(voxelSize, 1e-6f);
        int cx = Mathf.FloorToInt(camPos.x * invVoxel);
        int cy = Mathf.FloorToInt(camPos.y * invVoxel);
        int cz = Mathf.FloorToInt(camPos.z * invVoxel);
        int half = res / 2;
        return new Vector3Int(cx - half, cy - half, cz - half);
    }

    // --------------------------------------------------------------------
    // Internal: clear / kernel cache / validation
    // --------------------------------------------------------------------

    private int GetMediaKernel(ComputeShader cs)
    {
        if (_mediaKernelCache.TryGetValue(cs, out int kernel))
            return kernel;

        try
        {
            kernel = cs.FindKernel("ClipMapDensity");
        }
        catch
        {
            kernel = -1;
        }

        _mediaKernelCache[cs] = kernel;
        return kernel;
    }

    private void DispatchClearSlab(CommandBuffer cmd, RenderTexture rt, int res, float voxelSize, Slab slab)
    {
        int gx = Mathf.Max(Mathf.CeilToInt(slab.size.x / 4f), 1);
        int gy = Mathf.Max(Mathf.CeilToInt(slab.size.y / 4f), 1);
        int gz = Mathf.Max(Mathf.CeilToInt(slab.size.z / 4f), 1);

        cmd.SetComputeFloatParam(_clipCS, ID_ClipmapVoxelSize, voxelSize);
        cmd.SetComputeIntParam  (_clipCS, ID_ClipmapResolution, res);
        cmd.SetComputeIntParams (_clipCS, ID_SlabVoxelBase, ScratchFromVec3(slab.min));
        cmd.SetComputeIntParams (_clipCS, ID_SlabVoxelSize, ScratchFromVec3(slab.size));

        cmd.SetComputeTextureParam(_clipCS, _kClear, ID_Result, rt);
        cmd.SetComputeVectorParam (_clipCS, ID_ClearColor, SkyManager.GetAmbientSkyColor());
        cmd.DispatchCompute       (_clipCS, _kClear, gx, gy, gz);
    }

    private void RecordFullClear(CommandBuffer cmd, RenderTexture rt)
    {
        // Initial-cleared, before any build runs. The voxelSize doesn't matter
        // for the clear kernel (it doesn't read worldPos), so we pass 1.
        int res = rt.width;
        var slab = new Slab
        {
            min  = Vector3Int.zero,
            size = new Vector3Int(res, res, res),
        };
        DispatchClearSlab(cmd, rt, res, 1f, slab);
    }

    private bool ValidateResources()
    {
        if (!_initialized) return false;
        if (_clipCS == null) return false;

        for (int i = 0; i < kClipLevels; i++)
        {
            if (!_clipFinal[i] || !_clipFinal[i].IsCreated())
                return false;
        }

        return true;
    }

    // --------------------------------------------------------------------
    // Misc helpers
    // --------------------------------------------------------------------

    private static VolumetricData.ClipmapLevelData GetLevel(VolumetricData data, int index)
    {
        switch (index)
        {
            case 0: return data.ClipmapLevel0;
            case 1: return data.ClipmapLevel1;
            case 2: return data.ClipmapLevel2;
            default: return data.ClipmapLevel3;
        }
    }

    private static Vector4 ToV4(Vector3 v) => new Vector4(v.x, v.y, v.z, 0f);

    private static void ReleaseRT(ref RenderTexture rt)
    {
        if (rt == null) return;
        rt.Release();
        CoreUtils.Destroy(rt);
        rt = null;
    }
}