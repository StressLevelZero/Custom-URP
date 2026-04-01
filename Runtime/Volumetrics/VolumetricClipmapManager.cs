using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

public sealed class VolumetricClipmapManager : IDisposable
{
    public const int kClipLevels = 4;

    // Used when external systems change the clipmap
    uint _seenRevision;

    // Final published clipmaps (per level)
    readonly RenderTexture[] _clipFinal = new RenderTexture[kClipLevels];

    // Shared unpublished staging buffer
    RenderTexture _scratch;
    int _scratchRes = -1;

    // Scheduling state
    readonly Vector3[] _activeCenter    = new Vector3[kClipLevels]; // published center
    readonly Vector3[] _targetCenter    = new Vector3[kClipLevels]; // latest desired center
    readonly Vector3[] _lastBuildCenter = new Vector3[kClipLevels]; // last completed build center
    readonly bool[]    _pendingLevel    = new bool[kClipLevels];
    readonly float[]   _levelUrgency    = new float[kClipLevels];

    // Active incremental build (shared scratch means only one at a time)
    BuildState _build;

    // Kernels
    ComputeShader _clipCS;
    int _kGen, _kClear, _kCopy, _kHeight;

    // Allocation signature
    readonly int[] _levelRes = new int[kClipLevels];
    bool _initialized;
    bool _initialCleared;

    enum BuildStage
    {
        None = 0,
        ClearScratch,
        LocalMedia,
        BakedAreas,
        CopyToFinal,
        GenerateMips
    }

    struct BuildState
    {
        public bool active;
        public int level;
        public int res;
        public Vector3 buildTargetCenter; // frozen center for the in-flight build
        public Vector3 buildOrigin;
        public BuildStage stage;
        public int localMediaIndex;
        public int bakedAreaIndex;
    }

    // ----- Property IDs -----
    static readonly int ID_Result               = Shader.PropertyToID("Result");
    static readonly int ID_PreResult            = Shader.PropertyToID("PreResult");
    static readonly int ID_VolumeMap            = Shader.PropertyToID("VolumeMap");
    static readonly int ID_VolumeWorldSize      = Shader.PropertyToID("VolumeWorldSize");
    static readonly int ID_VolumeWorldPosition  = Shader.PropertyToID("VolumeWorldPosition");
    static readonly int ID_RegionOffset         = Shader.PropertyToID("_RegionOffset");
    static readonly int ID_RegionSize           = Shader.PropertyToID("_RegionSize");

    static readonly int ID_Gen_ClipmapScale     = Shader.PropertyToID("ClipmapScale");
    static readonly int ID_ClipmapWorldPosition = Shader.PropertyToID("ClipmapWorldPosition");

    static readonly int VolumeDensity           = Shader.PropertyToID("VolumeDensity");
    static readonly int VolumeFalloff           = Shader.PropertyToID("VolumeFalloff");

    static readonly int ID_UseVolumeTexture     = Shader.PropertyToID("_UseVolumeTexture"); // 0/1
    static readonly int ID_ShapeType            = Shader.PropertyToID("_ShapeType");        // 0=Box, 1=Sphere

    // Scatter-side bindings
    static readonly int ID_VolumetricClipmapTexture0 = Shader.PropertyToID("_VolumetricClipmapTexture0");
    static readonly int ID_VolumetricClipmapTexture1 = Shader.PropertyToID("_VolumetricClipmapTexture1");
    static readonly int ID_VolumetricClipmapTexture2 = Shader.PropertyToID("_VolumetricClipmapTexture2");
    static readonly int ID_VolumetricClipmapTexture3 = Shader.PropertyToID("_VolumetricClipmapTexture3");

    static readonly int ID_ClipmapScale0 = Shader.PropertyToID("_ClipmapScale0");
    static readonly int ID_ClipmapScale1 = Shader.PropertyToID("_ClipmapScale1");
    static readonly int ID_ClipmapScale2 = Shader.PropertyToID("_ClipmapScale2");
    static readonly int ID_ClipmapScale3 = Shader.PropertyToID("_ClipmapScale3");

    static readonly int ID_ClipmapPosition0 = Shader.PropertyToID("_ClipmapPosition0");
    static readonly int ID_ClipmapPosition1 = Shader.PropertyToID("_ClipmapPosition1");
    static readonly int ID_ClipmapPosition2 = Shader.PropertyToID("_ClipmapPosition2");
    static readonly int ID_ClipmapPosition3 = Shader.PropertyToID("_ClipmapPosition3");

    public RenderTexture GetClipmap(int level) => _clipFinal[level];
    public Vector3 GetActiveCenter(int level) => _activeCenter[level];

    public void Dispose()
    {
        for (int i = 0; i < kClipLevels; i++)
            ReleaseRT(ref _clipFinal[i]);

        ReleaseRT(ref _scratch);
        _scratchRes = -1;

        _clipCS = null;
        _initialized = false;
        _initialCleared = false;
        _build = default;

        for (int i = 0; i < kClipLevels; i++)
            _levelRes[i] = 0;
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
        _kHeight = _clipCS.FindKernel("ClipMapHeight");
        _kCopy   = _clipCS.FindKernel("ClipMapCopy");

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
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Trilinear
            };
            _clipFinal[i].hideFlags = HideFlags.DontSave;
            _clipFinal[i].Create();

            _activeCenter[i]    = Vector3.negativeInfinity;
            _targetCenter[i]    = Vector3.negativeInfinity;
            _lastBuildCenter[i] = Vector3.negativeInfinity;

            _pendingLevel[i] = true;
            _levelUrgency[i] = float.PositiveInfinity;
        }

        _scratchRes = -1;
        _build = default;
        _initialized = true;
        _initialCleared = false;
    }

    public void ForceFullClipmapUpdate()
    {
        AbortActiveBuild();

        for (int i = 0; i < kClipLevels; i++)
        {
            _pendingLevel[i] = true;
            _levelUrgency[i] = float.PositiveInfinity;
        }
    }
    static Vector3 ComputeDesiredCenter(Vector3 camPos, float scale, int res)
    {
        float voxelSize = scale / Mathf.Max(res, 1);

        // Option A: half-voxel center offset
        return SnapToVoxel(camPos, scale, res) + Vector3.one * (0.5f * voxelSize);

        // Option B: if your shaders already sample voxel centers internally,
        // then just return SnapToVoxel(camPos, scale, res);
    }
    
    /// <summary>
    /// Records incremental clipmap work into the command buffer.
    /// The 4th argument is now a build-step budget, not a level count.
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

        if (!_initialCleared)
        {
            for (int i = 0; i < kClipLevels; i++)
                RecordClear(cmd, _clipFinal[i]);

            _initialCleared = true;
        }
        

        // External content changed: abort unpublished staging safely and rebuild.
        if (_seenRevision != VolumetricRegisters.ClipmapRevision)
        {
            _seenRevision = VolumetricRegisters.ClipmapRevision;
            AbortActiveBuild();

            for (int i = 0; i < kClipLevels; i++)
            {
                var L = GetLevel(data, i);
                _targetCenter[i] = SnapToVoxel(camPos, L.ClipmapScale, L.ClipMapResolution) + (Vector3.one * 0.5f);
                _pendingLevel[i] = true;
                _levelUrgency[i] = float.PositiveInfinity;
            }
        }

        // Keep the latest desired target up to date, even while a level is already pending.
        for (int i = 0; i < kClipLevels; i++)
        {
            var L = GetLevel(data, i);
            Vector3 snapped = ComputeDesiredCenter(camPos, L.ClipmapScale, L.ClipMapResolution);

            // No published data yet for this level:
            // seed once and keep it frozen until first publish.
            if (!IsFiniteCenter(_activeCenter[i]))
            {
                if (!IsFiniteCenter(_targetCenter[i]))
                    _targetCenter[i] = snapped;

                _pendingLevel[i] = true;
                _levelUrgency[i] = float.PositiveInfinity;
                continue;
            }

            float drift = DistInf(camPos, _activeCenter[i]);
            float thresh = Mathf.Max(1e-6f, L.ClipmapResampleThresholdDistance);

            if (!_pendingLevel[i])
            {
                if (drift >= thresh && !CentersEqual(snapped, _lastBuildCenter[i]))
                {
                    _targetCenter[i] = snapped;
                    _pendingLevel[i] = true;
                    _levelUrgency[i] = Mathf.Max(1f, drift / thresh);
                }
            }
            else
            {
                // Already pending, but this level has published data,
                // so it's safe to keep retargeting while old final stays live.
                _targetCenter[i] = snapped;
                _levelUrgency[i] = Mathf.Max(_levelUrgency[i], Mathf.Max(1f, drift / thresh));
            }
        }

        int remainingSteps = Mathf.Max(1, maxBuildStepsPerFrame);

        while (remainingSteps > 0)
        {
            if (!_build.active)
            {
                int li = SelectMostUrgentPendingLevel(_pendingLevel, _levelUrgency);
                if (li < 0)
                    break;

                BeginBuild(li, data);
            }

            if (!RecordBuildStep(cmd, data))
                break;

            remainingSteps--;
        }
    }

    public void RecordBindToScatter(CommandBuffer cmd, ComputeShader scatterCS, int scatterKernel, VolumetricData data)
    {
        cmd.SetComputeFloatParam(scatterCS, ID_ClipmapScale0, GetLevel(data, 0).ClipmapScale);
        cmd.SetComputeFloatParam(scatterCS, ID_ClipmapScale1, GetLevel(data, 1).ClipmapScale);
        cmd.SetComputeFloatParam(scatterCS, ID_ClipmapScale2, GetLevel(data, 2).ClipmapScale);
        cmd.SetComputeFloatParam(scatterCS, ID_ClipmapScale3, GetLevel(data, 3).ClipmapScale);

        cmd.SetComputeVectorParam(scatterCS, ID_ClipmapPosition0, ToV4(GetBoundCenter(0)));
        cmd.SetComputeVectorParam(scatterCS, ID_ClipmapPosition1, ToV4(GetBoundCenter(1)));
        cmd.SetComputeVectorParam(scatterCS, ID_ClipmapPosition2, ToV4(GetBoundCenter(2)));
        cmd.SetComputeVectorParam(scatterCS, ID_ClipmapPosition3, ToV4(GetBoundCenter(3)));

        cmd.SetComputeTextureParam(scatterCS, scatterKernel, ID_VolumetricClipmapTexture0, _clipFinal[0]);
        cmd.SetComputeTextureParam(scatterCS, scatterKernel, ID_VolumetricClipmapTexture1, _clipFinal[1]);
        cmd.SetComputeTextureParam(scatterCS, scatterKernel, ID_VolumetricClipmapTexture2, _clipFinal[2]);
        cmd.SetComputeTextureParam(scatterCS, scatterKernel, ID_VolumetricClipmapTexture3, _clipFinal[3]);
    }

    // --------------------------------------------------------------------
    // Incremental build flow
    // --------------------------------------------------------------------

    void BeginBuild(int level, VolumetricData data)
    {
        var L = GetLevel(data, level);
        int res = Mathf.Max(1, L.ClipMapResolution);

        EnsureScratch(res, $"ClipScratch_L{level}");
        VolumetricRegisters.EnsureVolumetricAreasSorted(); 
        _build = new BuildState
        {
            active = true,
            level = level,
            res = res,
            buildTargetCenter = _targetCenter[level],
            buildOrigin = _targetCenter[level] - 0.5f * L.ClipmapScale * Vector3.one,
            stage = BuildStage.ClearScratch,
            localMediaIndex = 0,
            bakedAreaIndex = 0
        };
    }

    void AbortActiveBuild()
    {
        if (!_build.active)
            return;

        _pendingLevel[_build.level] = true;
        _levelUrgency[_build.level] = float.PositiveInfinity;
        _build = default;
    }

    bool RecordBuildStep(CommandBuffer cmd, VolumetricData data)
    {
        if (!_build.active)
            return false;

        var state = _build;
        var L = GetLevel(data, state.level);
        int fullGroups = Mathf.Max(Mathf.CeilToInt(state.res / 4f), 1);

        for (;;)
        {
            switch (state.stage)
            {
                case BuildStage.ClearScratch:
                {
                    RecordClear(cmd, _scratch);
                    state.stage = BuildStage.LocalMedia;
                    _build = state;
                    return true;
                }

                case BuildStage.LocalMedia:
                {
                    while (state.localMediaIndex < VolumetricRegisters.VolumetricMediaEntities.Count)
                    {
                        var media = VolumetricRegisters.VolumetricMediaEntities[state.localMediaIndex++];
                        if (media == null) continue;

                        var mediaCS = media.computeShader;
                        if (mediaCS == null) continue;

                        int mediaKernel = mediaCS.FindKernel("ClipMapDensity");

                        Vector3 volMin  = media.Corner;
                        Vector3 volSize = media.NormalizedScale;

                        if (!ComputeClipmapRegion(
                                state.buildOrigin,
                                L.ClipmapScale,
                                state.res,
                                volMin,
                                volSize,
                                out var regionOffset,
                                out var regionSize))
                        {
                            continue;
                        }

                        int groupsX = Mathf.Max(Mathf.CeilToInt(regionSize.x / 4f), 1);
                        int groupsY = Mathf.Max(Mathf.CeilToInt(regionSize.y / 4f), 1);
                        int groupsZ = Mathf.Max(Mathf.CeilToInt(regionSize.z / 4f), 1);

                        cmd.SetComputeFloatParam(mediaCS, ID_Gen_ClipmapScale, L.ClipmapScale);
                        cmd.SetComputeVectorParam(mediaCS, ID_ClipmapWorldPosition, ToV4(state.buildOrigin));

                        cmd.SetComputeTextureParam(mediaCS, mediaKernel, ID_Result, _scratch);

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

                        cmd.SetComputeIntParams(mediaCS, ID_RegionOffset, regionOffset.x, regionOffset.y, regionOffset.z);
                        cmd.SetComputeIntParams(mediaCS, ID_RegionSize,   regionSize.x,   regionSize.y,   regionSize.z);

                        cmd.DispatchCompute(mediaCS, mediaKernel, groupsX, groupsY, groupsZ);

                        _build = state;
                        return true;
                    }

                    state.stage = BuildStage.BakedAreas;
                    continue;
                }

                case BuildStage.BakedAreas:
                {
                    while (state.bakedAreaIndex < VolumetricRegisters.volumetricAreas.Count)
                    {
                        var area = VolumetricRegisters.volumetricAreas[state.bakedAreaIndex++];
                        if (area?.bakedTexture is null) continue;

                        Vector3 volMin  = area.Corner;
                        Vector3 volSize = area.NormalizedScale;

                        if (!ComputeClipmapRegion(
                                state.buildOrigin,
                                L.ClipmapScale,
                                state.res,
                                volMin,
                                volSize,
                                out var regionOffset,
                                out var regionSize))
                        {
                            continue;
                        }

                        int groupsX = Mathf.Max(Mathf.CeilToInt(regionSize.x / 4f), 1);
                        int groupsY = Mathf.Max(Mathf.CeilToInt(regionSize.y / 4f), 1);
                        int groupsZ = Mathf.Max(Mathf.CeilToInt(regionSize.z / 4f), 1);
                        
                        cmd.SetComputeFloatParam(_clipCS, ID_Gen_ClipmapScale, L.ClipmapScale);
                        cmd.SetComputeVectorParam(_clipCS, ID_ClipmapWorldPosition, ToV4(state.buildOrigin));
                        
                        cmd.SetComputeTextureParam(_clipCS, _kGen, ID_Result, _scratch);
                        cmd.SetComputeTextureParam(_clipCS, _kGen, ID_VolumeMap, area.bakedTexture);
                        cmd.SetComputeVectorParam (_clipCS, ID_VolumeWorldSize,     ToV4(area.NormalizedScale));
                        cmd.SetComputeVectorParam (_clipCS, ID_VolumeWorldPosition, ToV4(area.Corner));

                        cmd.SetComputeIntParams(_clipCS, ID_RegionOffset, regionOffset.x, regionOffset.y, regionOffset.z);
                        cmd.SetComputeIntParams(_clipCS, ID_RegionSize,   regionSize.x,   regionSize.y,   regionSize.z);

                        cmd.DispatchCompute(_clipCS, _kGen, groupsX, groupsY, groupsZ);

                        _build = state;
                        return true;
                    }

                    state.stage = BuildStage.CopyToFinal;
                    continue;
                }

                case BuildStage.CopyToFinal:
                {
                    cmd.SetComputeTextureParam(_clipCS, _kCopy, ID_PreResult, _scratch);
                    cmd.SetComputeTextureParam(_clipCS, _kCopy, ID_Result, _clipFinal[state.level]);
                    cmd.DispatchCompute(_clipCS, _kCopy, fullGroups, fullGroups, fullGroups);

                    state.stage = BuildStage.GenerateMips;
                    _build = state;
                    return true;
                }

                case BuildStage.GenerateMips:
                {
                    var final = _clipFinal[state.level];
                    if (final != null && final.IsCreated() && final.useMipMap)
                        cmd.GenerateMips(final);

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

    void PublishCompletedBuild(ref BuildState state, VolumetricData data)
    {
        int level = state.level;
        Vector3 builtCenter = state.buildTargetCenter;

        _activeCenter[level] = builtCenter;
        _lastBuildCenter[level] = builtCenter;

        if (CentersEqual(_targetCenter[level], builtCenter))
        {
            _pendingLevel[level] = false;
            _levelUrgency[level] = 0f;
        }
        else
        {
            // Camera moved while this build was in flight.
            var L = GetLevel(data, level);
            float thresh = Mathf.Max(1e-6f, L.ClipmapResampleThresholdDistance);
            float drift = DistInf(_targetCenter[level], builtCenter);

            _pendingLevel[level] = true;
            _levelUrgency[level] = Mathf.Max(1f, drift / thresh);
        }

        state = default;
    }

    // --------------------------------------------------------------------
    // Internal: clear / scratch / validation
    // --------------------------------------------------------------------

    void RecordClear(CommandBuffer cmd, RenderTexture buffer)
    {
        int res = buffer.width;
        int gx = Mathf.Max(Mathf.CeilToInt(res / 4f), 1);

        cmd.SetComputeTextureParam(_clipCS, _kClear, ID_Result, buffer);
        cmd.SetComputeVectorParam(_clipCS, "clearColor", SkyManager.GetAmbientSkyColor());
        cmd.DispatchCompute(_clipCS, _kClear, gx, gx, gx);
    }

    void EnsureScratch(int res, string namePrefix)
    {
        if (_scratchRes == res && _scratch != null && _scratch.IsCreated())
            return;

        ReleaseRT(ref _scratch);

        var desc = new RenderTextureDescriptor
        {
            enableRandomWrite = true,
            dimension = TextureDimension.Tex3D,
            width = res,
            height = res,
            volumeDepth = res,
            graphicsFormat = GraphicsFormat.R16G16B16A16_SFloat,
            msaaSamples = 1,
            useMipMap = false,
            autoGenerateMips = false
        };

        _scratch = new RenderTexture(desc)
        {
            name = $"{namePrefix}_{res}",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Point
        };
        _scratch.hideFlags = HideFlags.DontSave;
        _scratch.Create();

        _scratchRes = res;
    }

    bool ValidateResources()
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
    // Math + region calc
    // --------------------------------------------------------------------

    static VolumetricData.ClipmapLevelData GetLevel(VolumetricData data, int index)
    {
        switch (index)
        {
            case 0: return data.ClipmapLevel0;
            case 1: return data.ClipmapLevel1;
            case 2: return data.ClipmapLevel2;
            default: return data.ClipmapLevel3;
        }
    }

    static Vector3 SnapToVoxel(Vector3 center, float scale, int res)
    {
        float v = scale / Mathf.Max(res, 1);
        return new Vector3(
            Mathf.Floor(center.x / v) * v,
            Mathf.Floor(center.y / v) * v,
            Mathf.Floor(center.z / v) * v
        );
    }

    static float DistInf(Vector3 a, Vector3 b)
    {
        Vector3 d = a - b;
        return Mathf.Max(Mathf.Abs(d.x), Mathf.Max(Mathf.Abs(d.y), Mathf.Abs(d.z)));
    }

    static int SelectMostUrgentPendingLevel(bool[] pending, float[] urgency)
    {
        int best = -1;
        float bestScore = -1f;

        for (int i = 0; i < pending.Length; i++)
        {
            if (pending[i] && urgency[i] > bestScore)
            {
                best = i;
                bestScore = urgency[i];
            }
        }
        return best;
    }

    static bool ComputeClipmapRegion(
        Vector3 clipMin, float clipScale, int res,
        Vector3 volMin, Vector3 volSize,
        out Vector3Int regionOffset, out Vector3Int regionSize)
    {
        Vector3 clipMax = clipMin + Vector3.one * clipScale;
        Vector3 volMax  = volMin + volSize;

        Vector3 interMin = Vector3.Max(clipMin, volMin);
        Vector3 interMax = Vector3.Min(clipMax, volMax);

        if (interMin.x >= interMax.x || interMin.y >= interMax.y || interMin.z >= interMax.z)
        {
            regionOffset = default;
            regionSize   = default;
            return false;
        }

        Vector3 clipSize = clipMax - clipMin;

        Vector3 uvMin = new Vector3(
            (interMin.x - clipMin.x) / clipSize.x,
            (interMin.y - clipMin.y) / clipSize.y,
            (interMin.z - clipMin.z) / clipSize.z
        );

        Vector3 uvMax = new Vector3(
            (interMax.x - clipMin.x) / clipSize.x,
            (interMax.y - clipMin.y) / clipSize.y,
            (interMax.z - clipMin.z) / clipSize.z
        );

        var minIdx = new Vector3Int(
            Mathf.Clamp(Mathf.FloorToInt(uvMin.x * res), 0, res - 1),
            Mathf.Clamp(Mathf.FloorToInt(uvMin.y * res), 0, res - 1),
            Mathf.Clamp(Mathf.FloorToInt(uvMin.z * res), 0, res - 1)
        );

        var maxIdx = new Vector3Int(
            Mathf.Clamp(Mathf.CeilToInt(uvMax.x * res) - 1, 0, res - 1),
            Mathf.Clamp(Mathf.CeilToInt(uvMax.y * res) - 1, 0, res - 1),
            Mathf.Clamp(Mathf.CeilToInt(uvMax.z * res) - 1, 0, res - 1)
        );

        var size = maxIdx - minIdx + Vector3Int.one;

        if (size.x <= 0 || size.y <= 0 || size.z <= 0)
        {
            regionOffset = default;
            regionSize   = default;
            return false;
        }

        regionOffset = minIdx;
        regionSize   = size;
        return true;
    }

    Vector3 GetBoundCenter(int level)
    {
        if (IsFiniteCenter(_activeCenter[level]))
            return _activeCenter[level];

        if (_build.active && _build.level == level)
            return _build.buildTargetCenter;

        if (IsFiniteCenter(_targetCenter[level]))
            return _targetCenter[level];

        return Vector3.zero;
    }

    static bool IsFiniteCenter(Vector3 v)
    {
        return !(float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z) ||
                 float.IsInfinity(v.x) || float.IsInfinity(v.y) || float.IsInfinity(v.z));
    }

    static bool CentersEqual(Vector3 a, Vector3 b)
    {
        return DistInf(a, b) <= 1e-5f;
    }

    static Vector4 ToV4(Vector3 v) => new Vector4(v.x, v.y, v.z, 0f);

    static void ReleaseRT(ref RenderTexture rt)
    {
        if (rt == null) return;
        rt.Release();
        CoreUtils.Destroy(rt);
        rt = null;
    }
}